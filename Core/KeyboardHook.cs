using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Vistia.Core;

/// <summary>Gancho global en hilo propio: primero la edición; Vistia después.</summary>
public sealed class KeyboardHook : IDisposable
{
    public event Action<IntPtr, string>? SpacePressed;
    public event Action<IntPtr, string>? SpaceVPressed;
    public event Action<uint, IntPtr, string>? PreviewKeyPressed;
    public event Action? CtrlSpacePressed;
    public Action<string>? Aviso;
    private volatile bool _activo = true;
    private int _cambioActivo;
    private int _cambioActivoVisto;
    public bool Activo
    {
        get => _activo;
        set { _activo = value; Interlocked.Increment(ref _cambioActivo); }
    }
    public bool Instalado => _hook != IntPtr.Zero;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_SPACE = 0x20;
    private const int VK_V = 0x56;
    private const int VK_ESCAPE = 0x1B;
    private const int WM_QUIT = 0x0012;
    private const uint LLKHF_INJECTED = 0x10;

    private delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(int idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    private IntPtr _hook;
    private readonly LowLevelHookProc _proc;
    private readonly SynchronizationContext _ui;
    private uint _ultimoAceptadoVk;
    private uint _ultimaEspacioTicks;
    private bool _espacioFisicoPresionado;
    private bool _comboEspacioV;
    private bool _vComboConsumida;
    private Thread? _hilo;
    private int _idHilo;
    private readonly ManualResetEvent _instalado = new(false);
    private int _muerto;

    public KeyboardHook(Action<string>? aviso = null)
    {
        Aviso = aviso;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _proc = HookProc;
        _hilo = new Thread(BucleHook) { IsBackground = true, Name = "Vistia-Gancho" };
        _hilo.Start();
        _instalado.WaitOne(TimeSpan.FromSeconds(10));
    }

    private void BucleHook()
    {
        _idHilo = GetCurrentThreadId();
        for (int intento = 1; ; intento++)
        {
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (_hook != IntPtr.Zero)
            {
                Aviso?.Invoke("GANCHO instalado (intento " + intento + ")");
                break;
            }
            Aviso?.Invoke("GANCHO fallo instalacion " + Marshal.GetLastWin32Error() + " intento " + intento);
            if (intento >= 5 || Volatile.Read(ref _muerto) != 0)
            {
                _instalado.Set();
                return;
            }
            Thread.Sleep(2000);
        }
        _instalado.Set();
        var msg = new MSG();
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        int cambioActivo = Volatile.Read(ref _cambioActivo);
        if (cambioActivo != _cambioActivoVisto)
        {
            _cambioActivoVisto = cambioActivo;
            LimpiarEstadoTeclas();
        }
        if (!Activo || nCode < 0) return CallNextHookEx(_hook, nCode, wParam, lParam);
        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if ((info.flags & LLKHF_INJECTED) != 0) return CallNextHookEx(_hook, nCode, wParam, lParam);

        bool subida = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;
        if (subida)
        {
            bool consumir = false;
            if (info.vkCode == VK_SPACE)
            {
                _espacioFisicoPresionado = false;
                if (_comboEspacioV)
                {
                    _comboEspacioV = false;
                    consumir = true;
                }
            }
            if (info.vkCode == VK_V && _vComboConsumida)
            {
                _vComboConsumida = false;
                consumir = true;
            }
            if (_ultimoAceptadoVk == info.vkCode)
            {
                _ultimoAceptadoVk = 0;
                consumir = true;
            }
            return consumir ? (IntPtr)1 : CallNextHookEx(_hook, nCode, wParam, lParam);
        }
        bool bajada = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
        if (!bajada) return CallNextHookEx(_hook, nCode, wParam, lParam);
        if (info.vkCode == VK_V)
        {
            if (_vComboConsumida) return (IntPtr)1;
            IntPtr fgV = GetForegroundWindow();
            if (_espacioFisicoPresionado && !EntradaTeclado.HayModificadores() &&
                !EntradaTeclado.EstaComponiendoIme(fgV) && !EntradaTeclado.EsControlEditableNativo(fgV) && !EntradaTeclado.TextoEnVisor)
            {
                // Si el Espacio inicial ya produjo una orden, la V solo completa
                // la combinación: no debe abrir/cerrar una segunda vez.
                bool espacioYaAceptado = _ultimoAceptadoVk == VK_SPACE;
                _comboEspacioV = true;
                _vComboConsumida = true;
                if (!espacioYaAceptado)
                {
                    string claseV = ClaseDe(fgV);
                    try { _ui.Post(_ => SpaceVPressed?.Invoke(fgV, claseV), null); } catch { }
                }
                return (IntPtr)1;
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }
        if (info.vkCode != VK_SPACE && info.vkCode != VK_ESCAPE) return CallNextHookEx(_hook, nCode, wParam, lParam);
        if (info.vkCode == VK_SPACE && _comboEspacioV) return (IntPtr)1;

        IntPtr fg = GetForegroundWindow();
        if (EntradaTeclado.EstaComponiendoIme(fg)) return CallNextHookEx(_hook, nCode, wParam, lParam);
        bool soloCtrl = info.vkCode == VK_SPACE && EntradaTeclado.SoloControl();
        if (EntradaTeclado.HayModificadores() && !soloCtrl) return CallNextHookEx(_hook, nCode, wParam, lParam);
        if (info.vkCode == VK_SPACE) _espacioFisicoPresionado = true;
        if (_ultimoAceptadoVk == info.vkCode) return (IntPtr)1; // repetición solo de una orden aceptada

        bool dobleEspacio = info.vkCode == VK_SPACE && _ultimaEspacioTicks != 0 && info.time - _ultimaEspacioTicks <= 500;
        bool aceptada = false;
        string clase = ClaseDe(fg);
        if (EntradaTeclado.EsVistaPropia(fg))
        {
            bool bloqueadaPorControl = EntradaTeclado.TextoEnVisor || (EntradaTeclado.InteractivoEnVisor && !dobleEspacio);
            if (!bloqueadaPorControl)
            {
                if (soloCtrl)
                {
                    try { _ui.Post(_ => CtrlSpacePressed?.Invoke(), null); } catch { }
                }
                else
                {
                    try { _ui.Post(_ => PreviewKeyPressed?.Invoke(info.vkCode, fg, clase), null); } catch { }
                }
                aceptada = true;
            }
        }
        else if (info.vkCode == VK_SPACE && !EntradaTeclado.EsControlEditableNativo(fg))
        {
            if (soloCtrl)
            {
                try { _ui.Post(_ => CtrlSpacePressed?.Invoke(), null); } catch { }
                aceptada = true;
            }
            else if (EntradaTeclado.EsAperturaArchivosValida(fg))
            {
                try { _ui.Post(_ => SpacePressed?.Invoke(fg, clase), null); } catch { }
                aceptada = true;
            }
        }

        if (!aceptada) return CallNextHookEx(_hook, nCode, wParam, lParam);
        _ultimoAceptadoVk = info.vkCode;
        if (info.vkCode == VK_SPACE && !soloCtrl) _ultimaEspacioTicks = info.time;
        return (IntPtr)1;
    }

    private void LimpiarEstadoTeclas()
    {
        _ultimoAceptadoVk = 0;
        _ultimaEspacioTicks = 0;
        _espacioFisicoPresionado = false;
        _comboEspacioV = false;
        _vComboConsumida = false;
    }

    private static string ClaseDe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "";
        var sb = new System.Text.StringBuilder(64);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Dispose()
    {
        Volatile.Write(ref _muerto, 1);
        if (_idHilo != 0) PostThreadMessage(_idHilo, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _hilo?.Join(1500);
    }
}

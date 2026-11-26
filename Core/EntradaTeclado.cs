using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace Vistia.Core;

/// <summary>Decide cuándo una tecla pertenece a Vistia sin bloquear el gancho global.</summary>
public static class EntradaTeclado
{
    public static IntPtr VistaHandle { get; set; }
    public static volatile bool InteractivoEnVisor;
    public static volatile bool TextoEnVisor;

    private static readonly object _candadoFoco = new();
    private static IntPtr _focoCache;
    private static bool _editableCache;
    private static bool _cacheValido;

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    private static extern int ImmGetCompositionString(IntPtr hImc, int dwIndex, IntPtr lpBuf, int dwBufLen);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hImc);

    [StructLayout(LayoutKind.Sequential)]
    private struct RectWin { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RectWin rcCaret;
    }

    static EntradaTeclado()
    {
        // UIA puede tardar al responder desde otro proceso. Se consulta fuera del
        // WH_KEYBOARD_LL, cuyo callback debe volver antes del límite de Windows.
        var hilo = new Thread(BucleContexto) { IsBackground = true, Name = "Vistia-Contexto-Foco" };
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
    }

    private static void BucleContexto()
    {
        while (true)
        {
            try
            {
                IntPtr foco = FocoReal();
                bool editable = foco != IntPtr.Zero && EsEditableCompleto(foco);
                lock (_candadoFoco)
                {
                    _focoCache = foco;
                    _editableCache = editable;
                    _cacheValido = true;
                }
            }
            catch { }
            Thread.Sleep(150);
        }
    }

    public static bool EsVistaPropia(IntPtr hwnd) => hwnd != IntPtr.Zero && hwnd == VistaHandle;

    public static bool HayModificadores()
    {
        return Presionada(0x10) || Presionada(0x11) || Presionada(0x12) || Presionada(0x5B) || Presionada(0x5C);
    }

    public static bool SoloControl() => Presionada(0x11) && !Presionada(0x10) && !Presionada(0x12) && !Presionada(0x5B) && !Presionada(0x5C);

    private static bool Presionada(int vk) => (GetKeyState(vk) & 0x8000) != 0;

    public static bool EstaComponiendoIme(IntPtr hwnd)
    {
        IntPtr ctx = ImmGetContext(hwnd);
        if (ctx == IntPtr.Zero) return false;
        try { return ImmGetCompositionString(ctx, 0x0008, IntPtr.Zero, 0) > 0; } // GCS_COMPSTR
        finally { ImmReleaseContext(hwnd, ctx); }
    }

    public static bool EsControlEditableNativo(IntPtr hwnd)
    {
        IntPtr foco = FocoReal();
        if (foco == IntPtr.Zero) return false;
        if (EsClaseEditable(ClaseDe(foco))) return true;
        lock (_candadoFoco)
        {
            if (_cacheValido && foco == _focoCache) return _editableCache;
        }
        // Fallback inmediato mientras llega la primera instantánea UIA.
        var info = InformacionGui();
        return info.hwndCaret != IntPtr.Zero;
    }

    private static bool EsEditableCompleto(IntPtr foco)
    {
        if (EsClaseEditable(ClaseDe(foco))) return true;
        try
        {
            var elemento = AutomationElement.FromHandle(foco);
            if (elemento == null) return false;
            var tipo = elemento.Current.ControlType;
            if (tipo != ControlType.Edit && tipo != ControlType.ComboBox && tipo != ControlType.Document) return false;
            if (tipo == ControlType.ComboBox) return true;
            try
            {
                var valor = (ValuePattern)elemento.GetCurrentPattern(ValuePattern.Pattern);
                return !valor.Current.IsReadOnly;
            }
            catch { return tipo == ControlType.Edit; }
        }
        catch { return false; }
    }

    private static bool EsClaseEditable(string clase)
    {
        return clase.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
               clase.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ||
               clase.Equals("Scintilla", StringComparison.OrdinalIgnoreCase) ||
               clase.Equals("AkelEditW", StringComparison.OrdinalIgnoreCase) ||
               clase.Equals("TMemo", StringComparison.OrdinalIgnoreCase) ||
               clase.Equals("TSynEdit", StringComparison.OrdinalIgnoreCase);
    }

    public static bool EsControlInteractivoNativo(IntPtr hwnd)
    {
        if (EsControlEditableNativo(hwnd)) return true;
        string clase = ClaseDe(FocoReal());
        return clase.Equals("Button", StringComparison.OrdinalIgnoreCase) ||
               clase.Equals("ComboBox", StringComparison.OrdinalIgnoreCase) ||
               clase.Equals("ComboBoxEx32", StringComparison.OrdinalIgnoreCase) ||
               clase.Equals("SysLink", StringComparison.OrdinalIgnoreCase) ||
               clase.Equals("ToolbarWindow32", StringComparison.OrdinalIgnoreCase);
    }

    public static bool EsAperturaArchivosValida(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || EsControlInteractivoNativo(hwnd) || EstaComponiendoIme(hwnd)) return false;
        string top = ClaseDe(hwnd);
        string foco = ClaseDe(FocoReal());
        bool lista = EsListaArchivos(foco);
        if (top == "#32770") return lista && foco != top;
        if (top is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW")
            return lista || foco == top;
        return false;
    }

    private static bool EsListaArchivos(string clase)
    {
        return clase is "SysListView32" or "SHELLDLL_DefView" or "DirectUIHWND" or "UIItemsView"
            or "ItemsView" or "ListView20WndClass" or "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW";
    }

    private static GuiThreadInfo InformacionGui()
    {
        var info = new GuiThreadInfo { cbSize = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        try { GetGUIThreadInfo(0, ref info); } catch { }
        return info;
    }

    private static IntPtr FocoReal()
    {
        var info = InformacionGui();
        return info.hwndFocus != IntPtr.Zero ? info.hwndFocus : IntPtr.Zero;
    }

    private static string ClaseDe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(96);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch { return ""; }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Vistia.Core;

namespace Vistia;

/// <summary>Presentación Win32: una vista nueva aparece al frente aunque la app esté en bandeja.</summary>
public partial class PreviewWindow
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private void PresentarAlFrente()
    {
        ShowActivated = true;
        bool temporal = !_fijado;
        Topmost = true; // también la no fijada, solo durante la presentación
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (!IsVisible) Show();
        ShowWindowAsync(hwnd, 9); // SW_RESTORE
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
        Activate();

        uint actual = GetCurrentThreadId();
        uint frente = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        bool unido = frente != 0 && frente != actual && AttachThreadInput(actual, frente, true);
        bool foco;
        try { foco = SetForegroundWindow(hwnd); }
        finally { if (unido) AttachThreadInput(actual, frente, false); }
        ServidorAssets.Registrar("PRESENTAR fijada=" + _fijado + " focoWin32=" + foco);

        if (temporal)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_fijado) return;
                Topmost = false;
                SetWindowPos(hwnd, HwndNoTopmost, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
            }, DispatcherPriority.Background);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Vistia.Core;

/// <summary>Resultado de leer una ventana de archivos: selección y/o carpeta actual.</summary>
public sealed class Seleccion
{
    public List<string> Rutas = new();
    public string Carpeta = "";
    public IntPtr HwndVista;
}

/// <summary>
/// Lectura de la selección del Explorador por COM del Shell (Shell.Application +
/// IWebBrowser2 + Document.SelectedItems), la técnica universal y estable que usa
/// PowerShell/VBScript: sin vtables manuales (la cadena IShellBrowser/IShellView
/// portada a mano provocaba violación de acceso y tumbaba el proceso al presionar
/// espacio, 7.115). Todo en un hilo STA con tiempo límite: nada cuelga ni muere.
/// </summary>
public static class ShellSelection
{
    /// <summary>Tiempo máximo de la lectura COM (ms) antes de devolver sin selección.</summary>
    private const int TiempoLimiteMs = 2500;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndAfter, string? clase, string? titulo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder texto, int max);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDlgItem(IntPtr hwnd, int id);

    /// <summary>Lee la selección (o la carpeta) de la ventana en primer plano.</summary>
    public static Seleccion? Leer(IntPtr hwndFg, bool escritorio)
    {
        if (escritorio)
        {
            // La ventana del escritorio no aparece en Shell.Windows(): se ofrece la
            // carpeta Escritorio como galería (la selección de íconos del fondo
            // requeriría la cadena COM frágil que tumbaba el proceso).
            return new Seleccion
            {
                Carpeta = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                HwndVista = hwndFg
            };
        }

        Seleccion? resultado = null;
        var hilo = new Thread(() => resultado = LeerConShell(hwndFg))
        {
            IsBackground = true,
            Name = "Vistia-Seleccion"
        };
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        if (hilo.Join(TiempoLimiteMs)) return resultado;
        return null; // el Explorador no respondió: mejor no tocar nada
    }

    /// <summary>Lectura por Shell.Application COM (hilo STA propio), con fallbacks
    /// gestionados para ventanas que este Windows aún no registra en Shell.Windows().</summary>
    private static Seleccion? LeerConShell(IntPtr hwndFg)
    {
        // 1) técnica Shell.Application (la más completa cuando la ventana está registrada)
        var tipo = Type.GetTypeFromProgID("Shell.Application");
        if (tipo != null)
        {
            try
            {
                dynamic shell = Activator.CreateInstance(tipo)!;
                try
                {
                    for (int intento = 0; intento < 2; intento++) // el registro ROT puede tardar
                    {
                        dynamic ventanas = shell.Windows();
                        foreach (dynamic ventana in ventanas)
                        {
                            try
                            {
                                if ((int)ventana.HWND != hwndFg.ToInt32()) continue;
                                dynamic doc = ventana.Document;

                                var sel = new Seleccion { HwndVista = hwndFg };
                                try
                                {
                                    dynamic carpetaActual = doc.Folder.Self.Path;
                                    if (carpetaActual is string c && Directory.Exists(c)) sel.Carpeta = c;
                                }
                                catch { }

                                dynamic items = doc.SelectedItems();
                                int n = items.Count;
                                for (int i = 0; i < n; i++)
                                {
                                    string? ruta = items.Item(i).Path as string;
                                    if (string.IsNullOrEmpty(ruta)) continue;
                                    if (ruta.StartsWith("::")) continue; // carpetas especiales (CLSID)
                                    if (File.Exists(ruta) || Directory.Exists(ruta)) sel.Rutas.Add(ruta);
                                }
                                if (sel.Carpeta != "" || sel.Rutas.Count > 0) return sel;
                                break; // coincide pero vacía: probar fallback
                            }
                            catch { continue; }
                        }
                        Thread.Sleep(400);
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
            catch { }
        }

        // 2) fallback GESTIONADO por UIAutomation (búsqueda FILTRADA por ControlType.List:
        //    rápida, sin recorrer el árbol completo). La lista "TabListView" (pestañas del
        //    Explorador) expone la RUTA COMPLETA de la carpeta actual como Name del ítem
        //    activo; la lista de ítems expone el archivo seleccionado por Value/Name.
        var fb = new Seleccion { HwndVista = hwndFg };
        System.Windows.Automation.AutomationElement? raiz = null;
        try { raiz = System.Windows.Automation.AutomationElement.FromHandle(hwndFg); }
        catch { raiz = null; }

        if (raiz != null)
        {
            try
            {
                var listas = raiz.FindAll(System.Windows.Automation.TreeScope.Descendants,
                    new System.Windows.Automation.PropertyCondition(
                        System.Windows.Automation.AutomationElement.ControlTypeProperty,
                        System.Windows.Automation.ControlType.List));
                foreach (System.Windows.Automation.AutomationElement lista in listas)
                {
                    try
                    {
                        var patron = (System.Windows.Automation.SelectionPattern)lista.GetCurrentPattern(
                            System.Windows.Automation.SelectionPattern.Pattern);
                        var elegidos = patron.Current.GetSelection();
                        if (elegidos.Length == 0) continue;

                        if (lista.Current.AutomationId == "TabListView")
                        {
                            // pestaña activa del Explorador: Name = ruta completa de la carpeta
                            string pestana = elegidos[0].Current.Name;
                            if (pestana.Length > 3 && pestana.IndexOf(":\\", StringComparison.Ordinal) >= 0
                                && Directory.Exists(pestana))
                            {
                                fb.Carpeta = pestana;
                            }
                            continue;
                        }

                        foreach (System.Windows.Automation.AutomationElement item in elegidos)
                        {
                            string nombre = null;
                            try
                            {
                                var valor = (System.Windows.Automation.ValuePattern)item.GetCurrentPattern(
                                    System.Windows.Automation.ValuePattern.Pattern);
                                nombre = valor.Current.Value;
                            }
                            catch
                            {
                                nombre = item.Current.Name;
                            }
                            if (string.IsNullOrEmpty(nombre)) continue;
                            if (nombre.IndexOf(":\\", StringComparison.Ordinal) >= 0)
                            {
                                // ya vino como ruta completa
                                if (File.Exists(nombre) || Directory.Exists(nombre)) fb.Rutas.Add(nombre);
                            }
                            else if (fb.Carpeta != "")
                            {
                                string ruta = Path.Combine(fb.Carpeta, nombre);
                                if (File.Exists(ruta) || Directory.Exists(ruta)) fb.Rutas.Add(ruta);
                            }
                        }
                        if (fb.Rutas.Count > 0) break;
                    }
                    catch { }
                }
            }
            catch { }
        }

        if (fb.Carpeta == "")
        {
            try
            {
                var titulo = new StringBuilder(512);
                GetWindowText(hwndFg, titulo, 512);
                string t = titulo.ToString();
                int corte = t.LastIndexOf(" - Explorador de archivos", StringComparison.Ordinal);
                if (corte > 0)
                {
                    string posible = t.Substring(0, corte).Trim();
                    if (Directory.Exists(posible)) fb.Carpeta = posible;
                }
            }
            catch { }
        }

        if (fb.Carpeta == "" && fb.Rutas.Count == 0) return null;
        return fb;
    }

    /// <summary>Encuentra la vista de íconos (SHELLDLL_DefView) bajo una ventana del escritorio.</summary>
    public static IntPtr BuscarDefView(IntPtr hwndFg)
    {
        IntPtr defView = FindWindowEx(hwndFg, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView != IntPtr.Zero) return defView;
        // En Win11 la vista puede vivir en un WorkerW hermano
        for (IntPtr worker = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "WorkerW", null);
             worker != IntPtr.Zero;
             worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null))
        {
            defView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero) return defView;
        }
        return IntPtr.Zero;
    }

    /// <summary>Lee rutas absolutas de un diálogo de abrir/guardar (#32770).</summary>
    public static Seleccion? LeerDeDialogo(IntPtr hwndDialogo)
    {
        var sel = new Seleccion { HwndVista = hwndDialogo };
        try
        {
            var texto = new StringBuilder(512);
            string? ruta = null;
            // caja de edición del diálogo (1148 es el clásico de abrir/guardar)
            IntPtr edicion = GetDlgItem(hwndDialogo, 1148);
            if (edicion != IntPtr.Zero)
            {
                GetWindowText(edicion, texto, 512);
                ruta = texto.ToString();
            }
            if (string.IsNullOrWhiteSpace(ruta)) return sel;

            // puede traer varias rutas entre comillas: "C:\a.txt" "C:\b.txt"
            var partes = new List<string>();
            if (ruta.StartsWith('"'))
            {
                foreach (var pedazo in ruta.Split('"'))
                    if (pedazo.Trim().Length > 0) partes.Add(pedazo);
            }
            else partes.Add(ruta);

            foreach (var p in partes)
            {
                try
                {
                    string absoluto = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p));
                    if (File.Exists(absoluto) || Directory.Exists(absoluto)) sel.Rutas.Add(absoluto);
                }
                catch { }
            }
        }
        catch { }
        return sel;
    }
}

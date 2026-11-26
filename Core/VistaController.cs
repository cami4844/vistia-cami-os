using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace Vistia.Core;

/// <summary>
/// Orquesta la vista: recibe el espacio, lee la selección, administra la lista
/// navegable de la carpeta y le pasa las órdenes a la ventana de vista.
/// </summary>
public sealed class VistaController
{
    private PreviewWindow? _ventana;
    private readonly Config _config;
    private readonly ServidorAssets _servidor;

    private List<string> _lista = new();
    private int _indice = -1;
    private int _secuenciaRuta;
    private int _secuenciaActivacion;
    private string _rutaActual = "";
    private IntPtr _hwndOrigen;
    private bool _navegable;

    public VistaController(Config config, ServidorAssets servidor)
    {
        _config = config;
        _servidor = servidor;
    }

    public event Action? EstadoCambio;

    public bool Visible => _ventana?.VisibleReal == true;
    public bool FijadoActual => _ventana?.Fijado == true;

    public async void Precalentar()
    {
        try { await AsegurarVentana(); }
        catch (Exception ex) { ServidorAssets.Registrar("PRECALENTAR EXCEPCION: " + ex.Message); }
    }

    /// <summary>Espacio pulsado en una ventana candidata, sin congelar la UI al leer COM.</summary>
    public async System.Threading.Tasks.Task<bool> EspacioAsync(IntPtr hwndFg, string clase)
    {
        int secuencia = ++_secuenciaActivacion;
        try
        {
            ServidorAssets.Registrar("ESPACIO clase=" + clase + " visible=" + Visible + " secuencia=" + secuencia);
            if (!EntradaTeclado.EsAperturaArchivosValida(hwndFg)) return false;
            if (Visible)
            {
                Cerrar(true);
                return true;
            }

            Seleccion? sel;
            if (clase == "#32770")
            {
                sel = await System.Threading.Tasks.Task.Run(() => ShellSelection.LeerDeDialogo(hwndFg));
            }
            else if (clase is "Progman" or "WorkerW")
            {
                if (ShellSelection.BuscarDefView(hwndFg) == IntPtr.Zero) return false;
                sel = await System.Threading.Tasks.Task.Run(() => ShellSelection.Leer(hwndFg, true));
            }
            else
            {
                sel = await System.Threading.Tasks.Task.Run(() => ShellSelection.Leer(hwndFg, false));
            }

            // Una lectura lenta de una pulsación anterior no puede abrir después.
            if (secuencia != _secuenciaActivacion)
            {
                ServidorAssets.Registrar("ESPACIO descartado por secuencia " + secuencia);
                return true;
            }
            if (sel == null || (sel.Rutas.Count == 0 && sel.Carpeta == ""))
            {
                ServidorAssets.Registrar("ESPACIO sin seleccion (rutas=0 carpeta vacia)");
                return false;
            }

            _hwndOrigen = sel.HwndVista;
            PrepararLista(sel);
            ServidorAssets.Registrar("ESPACIO lista=" + _lista.Count + " indice=" + _indice);
            if (_lista.Count == 0 || _indice < 0) return false;
            _ = AbrirEn(_indice);
            return true;
        }
        catch (Exception ex)
        {
            ServidorAssets.Registrar("ESPACIO EXCEPCION: " + ex);
            return false;
        }
    }

    /// <summary>Modo QA/determinista: abre una ruta concreta.</summary>
    public async void AbrirRutaDirecta(string ruta)
    {
        try
        {
            ServidorAssets.Registrar("DIRECTA: entra " + ruta);
            _lista = new List<string> { ruta };
            _indice = 0;
            _navegable = false;
            _hwndOrigen = IntPtr.Zero;
            await AsegurarVentana();
            ServidorAssets.Registrar("DIRECTA: ventana lista");
            await MostrarRuta(ruta);
            ServidorAssets.Registrar("DIRECTA: mostrada");
        }
        catch (Exception ex)
        {
            ServidorAssets.Registrar("DIRECTA EXCEPCION: " + ex);
            MessageBox.Show("Vistia --preview: " + ex.Message, "Vistia");
        }
    }

    private void PrepararLista(Seleccion sel)
    {
        _navegable = true;
        if (sel.Rutas.Count > 1)
        {
            _lista = sel.Rutas.ToList();
            _indice = 0;
            return;
        }

        if (sel.Rutas.Count == 1)
        {
            string ruta = sel.Rutas[0];
            string carpeta = Directory.Exists(ruta)
                ? (Path.GetDirectoryName(ruta) ?? ruta)
                : (Path.GetDirectoryName(ruta) ?? sel.Carpeta);
            _rutaActual = ruta;
            _lista = ListarCarpeta(carpeta, incluirCarpetas: Directory.Exists(ruta) || !File.Exists(ruta));
            _indice = _lista.FindIndex(r => string.Equals(r, ruta, StringComparison.OrdinalIgnoreCase));
            if (_indice < 0) { _lista.Insert(0, ruta); _indice = 0; }
            return;
        }

        // nada seleccionado: la carpeta actual
        string dir = sel.Carpeta;
        _lista = new List<string> { dir };
        _indice = 0;
    }

    private static List<string> ListarCarpeta(string carpeta, bool incluirCarpetas)
    {
        var res = new List<string>();
        try
        {
            var dir = new DirectoryInfo(carpeta);
            if (incluirCarpetas)
                res.AddRange(dir.EnumerateDirectories().Select(d => d.FullName));
            res.AddRange(dir.EnumerateFiles().Select(f => f.FullName));
            res.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return res;
    }

    public void Navegar(int delta)
    {
        if (!_navegable || _lista.Count <= 1 || _indice < 0) return;
        int nuevo = ((_indice + delta) % _lista.Count + _lista.Count) % _lista.Count;
        _indice = nuevo;
        _ = AbrirEn(_indice);
    }

    public void TeclaEnVista(uint vk)
    {
        if (Visible && !EntradaTeclado.InteractivoEnVisor) _ventana!.TeclaDesdeGancho(vk);
    }

    public void AlternarFijado()
    {
        if (Visible) _ventana!.AlternarFijado();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public void RestaurarFocoOrigen()
    {
        if (_hwndOrigen != IntPtr.Zero)
        {
            try { SetForegroundWindow(_hwndOrigen); } catch { }
        }
    }

    public void Cerrar(bool restaurarFoco = false)
    {
        if (_ventana?.Fijado == true)
        {
            _ventana.AvisarQueEstaFijada();
            return;
        }
        _ventana?.Dormir();
        if (restaurarFoco) RestaurarFocoOrigen();
        EstadoCambio?.Invoke();
    }

    public void AbrirConApp()
    {
        string ruta = _rutaActual;
        if (ruta == "" || (!File.Exists(ruta) && !Directory.Exists(ruta))) return;
        try
        {
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>Abre una entrada INTERNA del comprimido actual sin descomprimir al disco.</summary>
    public void ZipInterno(int indice, string ext, string nombre)
    {
        string ruta = _rutaActual;
        if (_ventana == null || ruta == "" || !File.Exists(ruta)) return;
        try
        {
            var (modo, _) = PreviewRouter.Clasificar("a." + ext);
            if (modo == ModoVista.ImagenNativa) modo = ModoVista.ImagenWeb; // interno: por el visor web
            if (modo == ModoVista.Zip || modo == ModoVista.Carpeta) modo = ModoVista.Tarjeta;
            string esc(string s) => JsonEncoderEscape.Escape(s);
            string modoJs = modo.ToString().ToLowerInvariant();
            string json = "{\"modo\":\"" + modoJs + "\",\"ext\":\"" + esc(ext) + "\",\"nombre\":" + esc(nombre) +
                          ",\"zipi\":" + indice + ",\"zipVolver\":1,\"zipNombre\":" + esc(Path.GetFileName(ruta)) +
                          ",\"tema\":\"" + _config.tema + "\",\"autogiro\":" + (_config.autogiro ? "true" : "false") + "}";
            _servidor.EstablecerEstadoDirecto(json);
            _ventana.EnviarEstadoAhora();
            ServidorAssets.Registrar("ZIP-INTERNO i=" + indice + " ext=" + ext);
        }
        catch (Exception ex)
        {
            ServidorAssets.Registrar("ZIP-INTERNO EXCEPCION: " + ex.Message);
        }
    }

    /// <summary>Regresa del archivo interno a la lista del comprimido.</summary>
    public void ZipVolver()
    {
        string ruta = _rutaActual;
        if (_ventana == null || ruta == "" || !File.Exists(ruta)) return;
        var (modo, _) = PreviewRouter.Clasificar(ruta);
        _servidor.EstablecerArchivo(ruta, modo, _config.tema, _config.autogiro);
        _ = _ventana.EnviarEstadoJson();
        ServidorAssets.Registrar("ZIP-VOLVER");
    }

    public void AlternarTema()
    {
        _config.tema = _config.tema == "claro" ? "oscuro" : "claro";
        _config.Guardar();
        if (Visible && _rutaActual != "")
        {
            _ = MostrarRuta(_rutaActual);
        }
    }

    private System.Threading.Tasks.Task _inicializacion = System.Threading.Tasks.Task.CompletedTask;

    private async System.Threading.Tasks.Task AsegurarVentana()
    {
        ServidorAssets.Registrar("VENTANA: AsegurarVentana entra");
        if (_ventana == null)
        {
            ServidorAssets.Registrar("VENTANA: construyendo");
            _ventana = new PreviewWindow(this, _servidor, _config);
            ServidorAssets.Registrar("VENTANA: construida");
            _inicializacion = _ventana.InicializarAsync();
            ServidorAssets.Registrar("VENTANA: inicializacion lanzada");
        }
        else if (_inicializacion.IsCompleted && !_ventana.NucleoListo)
        {
            // Un fallo de arranque no queda memorizado para siempre: la próxima
            // pulsación vuelve a intentar inicializar el mismo WebView2.
            ServidorAssets.Registrar("VENTANA: reintentando inicializacion");
            _inicializacion = _ventana.InicializarAsync();
        }
        await _inicializacion;
        ServidorAssets.Registrar("VENTANA: inicializacion esperada");
    }

    private async System.Threading.Tasks.Task AbrirEn(int idx)
    {
        try
        {
            if (idx < 0 || idx >= _lista.Count) return;
            await AsegurarVentana();
            await MostrarRuta(_lista[idx]);
        }
        catch (Exception ex) { ServidorAssets.Registrar("APERTURA EXCEPCION: " + ex); }
    }

    private async System.Threading.Tasks.Task MostrarRuta(string ruta)
    {
        int secuencia = ++_secuenciaRuta;
        _rutaActual = ruta;
        var (modo, icono) = PreviewRouter.Clasificar(ruta);
        if (modo == ModoVista.Tarjeta && !File.Exists(ruta) && !Directory.Exists(ruta))
        {
            // la lista se quedó vieja
            if (_navegable) { Navegar(_indice >= 0 ? 1 : 0); return; }
        }
        _servidor.EstablecerArchivo(ruta, modo, _config.tema, _config.autogiro);
        if (secuencia != _secuenciaRuta) return;
        _ventana!.MostrarArchivo(ruta, modo, icono, ruta, _indice, _lista.Count, _navegable);
        EstadoCambio?.Invoke();
    }

    public string RutaActual => _rutaActual;
}

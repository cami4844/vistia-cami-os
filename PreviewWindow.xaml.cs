using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Vistia.Core;

namespace Vistia;

public partial class PreviewWindow : Window
{
    private readonly VistaController _controlador;
    private readonly ServidorAssets _servidor;
    private readonly Config _config;
    private readonly DispatcherTimer _relojMeta = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private ModoVista _modo = ModoVista.Tarjeta;
    private int _cargaId;
    private double _escala = 1.0;
    private int _versionNavegacion;
    private bool _navegado;
    private bool _fijado;
    private bool _vistaAlterna;
    private bool _dividiendo;
    private string _rutaActual = "";
    private string _metaBase = "";
    private VentanaInfo? _info;
    private readonly bool _mantenerVisibleQa = Environment.GetEnvironmentVariable("VISTIA_QA_VISIBLE") == "1";
    private bool _vistaRealMostrada;
    private bool _pipActivo;

    public bool Fijado => _fijado;
    public bool VisibleReal => _vistaRealMostrada && IsVisible;
    public bool NucleoListo => Web.CoreWebView2 != null;

    public PreviewWindow(VistaController controlador, ServidorAssets servidor, Config config)
    {
        InitializeComponent();
        EntradaTeclado.VistaHandle = new WindowInteropHelper(this).Handle;
        _controlador = controlador;
        _servidor = servidor;
        _config = config;
        AplicarTema();
        _relojMeta.Tick += (_, _) => RefrescarMetaMedia();
        // QuickLook: la vista se cierra sola al hacer clic fuera (perder el foco);
        // la aplicación queda en la bandeja y el espacio vuelve a abrirla.
        Deactivated += (_, _) =>
        {
            // Copiar, consultar Info o pasar a otra app no debe cerrar una vista fijada.
            if (_vistaRealMostrada && IsLoaded && !_fijado && _info == null && !_pipActivo && !_mantenerVisibleQa) _controlador.Cerrar();
        };
    }

    /// <summary>Crea el entorno WebView2 una sola vez y conecta el servidor local.</summary>
    public async System.Threading.Tasks.Task InicializarAsync()
    {
        if (Web.CoreWebView2 != null) return;
        ServidorAssets.Registrar("INIT inicio");
        string carpetaUsuario = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vistia", "WebView2");
        var opciones = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required"
        };
        var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, carpetaUsuario, opciones);
        CoreWebView2EnvironmentWrapper.Actual = env;
        ServidorAssets.Registrar("INIT entorno creado, esperando navegador");

        // El control WebView2 necesita HWND: mostrar la ventana UNA vez fuera de
        // pantalla (sin robar foco) para que el navegador pueda engancharse.
        if (!IsLoaded)
        {
            Left = -32000; Top = 0;
            ShowActivated = false;
            Show();
            ServidorAssets.Registrar("INIT ventana pre-mostrada fuera de pantalla");
        }

        // Si otra instancia recién muerta aún suelta la carpeta de datos, reintenta
        for (int intento = 1; ; intento++)
        {
            try
            {
                await Web.EnsureCoreWebView2Async(env);
                break;
            }
            catch (Exception ex) when (intento < 5)
            {
                ServidorAssets.Registrar("INIT reintento " + intento + ": " + ex.Message);
                await System.Threading.Tasks.Task.Delay(1500);
            }
        }
        Hide();
        ServidorAssets.Registrar("INIT navegador listo");
        var nucleo = Web.CoreWebView2;
        _servidor.Conectar(nucleo);
        nucleo.Settings.AreDefaultContextMenusEnabled = false;
        nucleo.Settings.IsStatusBarEnabled = false;
        nucleo.Settings.IsZoomControlEnabled = false;
        nucleo.Settings.AreDevToolsEnabled = false;
        nucleo.NewWindowRequested += (_, e) => { e.Handled = true; }; // nunca abrir ventanas
        nucleo.WebMessageReceived += MensajeDelVisor;
        nucleo.NavigationStarting += (_, e) =>
            ServidorAssets.Registrar("NAV-INICIO " + e.Uri);
        nucleo.NavigationCompleted += (_, e) =>
        {
            ServidorAssets.Registrar("NAV-FIN ok=" + e.IsSuccess + " " + (e.IsSuccess ? "" : e.WebErrorStatus));
            if (e.IsSuccess) _navegado = true; // el visor queda cargado: los siguientes estados van por mensaje
        };
        Web.DefaultBackgroundColor = System.Drawing.Color.Transparent;
    }

    private void MensajeDelVisor(object? s, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(e.WebMessageAsJson);
            var raiz = doc.RootElement;
            string cmd = raiz.GetProperty("cmd").GetString() ?? "";
            switch (cmd)
            {
                case "listo":
                    Cargando.Visibility = Visibility.Hidden;
                    ServidorAssets.Registrar("LISTO");
                    break;
                case "estado-recibido":
                    ServidorAssets.Registrar("ESTADO-MSG recibido " + e.WebMessageAsJson);
                    break;
                case "jserror":
                    ServidorAssets.Registrar("JS " + e.WebMessageAsJson);
                    break;
                case "foco-interactivo":
                    EntradaTeclado.InteractivoEnVisor = raiz.TryGetProperty("valor", out var interactivo) && interactivo.GetBoolean();
                    EntradaTeclado.TextoEnVisor = raiz.TryGetProperty("texto", out var texto) ? texto.GetBoolean() : EntradaTeclado.InteractivoEnVisor;
                    break;
                case "abrir":
                    _controlador.AbrirConApp();
                    break;
                case "abrir-editor":
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("code", "\"" + _rutaActual + "\"") { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        ServidorAssets.Registrar("EDITOR " + ex.Message);
                        _controlador.AbrirConApp();
                    }
                    break;
                case "zip-abrir":
                    _controlador.ZipInterno(raiz.GetProperty("i").GetInt32(),
                        raiz.GetProperty("ext").GetString() ?? "",
                        raiz.GetProperty("nombre").GetString() ?? "");
                    break;
                case "zip-volver":
                    _controlador.ZipVolver();
                    break;
                case "pip-estado":
                    _pipActivo = raiz.TryGetProperty("valor", out var pip) && pip.GetBoolean();
                    break;
                case "pantalla-completa":
                    AlternarPantallaCompleta();
                    break;
            }
        }
        catch (Exception ex) { ServidorAssets.Registrar("MENSAJE " + ex.Message); }
    }

    /// <summary>Manda el estado DIRECTO ya publicado (zip interno) al visor sin esperar.</summary>
    public void EnviarEstadoAhora()
    {
        try
        {
            string json = _servidor.JsonActual;
            if (!string.IsNullOrEmpty(json))
                Web.CoreWebView2?.PostWebMessageAsJson("{\"cmd\":\"estado\",\"estado\":" + json + "}");
        }
        catch (Exception ex)
        {
            ServidorAssets.Registrar("ESTADO-MSG EXCEPCION: " + ex.Message);
        }
    }

    /// <summary>Manda el estado tras esperar la generación (regreso al comprimido).</summary>
    public async System.Threading.Tasks.Task EnviarEstadoJson()
    {
        try
        {
            await _servidor.EsperarJsonAsync();
            EnviarEstadoAhora();
        }
        catch { }
    }

    private void AplicarTema()
    {
        bool oscuro = _config.tema == "oscuro";
        var papel = new SolidColorBrush(oscuro ? Color.FromRgb(0x1F, 0x1A, 0x16) : Color.FromRgb(0xF7, 0xF1, 0xE7));
        var barra = new SolidColorBrush(oscuro ? Color.FromRgb(0x2A, 0x24, 0x20) : Color.FromRgb(0xED, 0xE3, 0xD5));
        var tinta = new SolidColorBrush(oscuro ? Color.FromRgb(0xE8, 0xDF, 0xD4) : Color.FromRgb(0x2E, 0x26, 0x20));
        var suave = new SolidColorBrush(oscuro ? Color.FromRgb(0xA8, 0x98, 0x86) : Color.FromRgb(0x7A, 0x6A, 0x5A));
        Marco.Background = papel;
        Background = papel; // sin transparencia: la ventana misma es el papel
        Marco.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x5A, 0x32));
        Barra.Background = barra;
        TNombre.Foreground = tinta;
        TMeta.Foreground = suave;
        TPos.Foreground = suave;
        Cargando.Foreground = suave;
        foreach (var b in new[] { BFijar, BInfo, BMinecraft, BCodigo, BDividir, BAbrir, BCerrar })
        {
            b.Background = barra;
            b.Foreground = tinta;
            b.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x5A, 0x32));
        }
    }

    private static Color oscurroHelper() =>
        Color.FromRgb(0x1F, 0x1A, 0x16); // reservado

    /// <summary>Muestra un archivo (ruta ya validada por el controlador).</summary>
    public void MostrarArchivo(string ruta, ModoVista modo, string icono, string rutaOriginal, int indice, int total, bool navegable)
    {
        EnviarVisor("stop"); // ningún medio queda sonando al cambiar de archivo o de modo
        _modo = modo;
        _rutaActual = ruta;
        _cargaId++;
        _escala = 1.0;
        _vistaAlterna = false;
        _dividiendo = false;
        BCodigo.Content = "Código";
        BDividir.Content = "Dividir";
        BCodigo.Visibility = modo is ModoVista.Html or ModoVista.Markdown ? Visibility.Visible : Visibility.Collapsed;
        BDividir.Visibility = modo == ModoVista.Html ? Visibility.Visible : Visibility.Collapsed;
        BMinecraft.Visibility = EsPngCompatibleMinecraft(ruta, modo) ? Visibility.Visible : Visibility.Collapsed;

        AplicarTema();
        Img.Source = null;
        Img.LayoutTransform = null;
        Cargando.Visibility = Visibility.Hidden;
        TPos.Text = navegable && total > 1 ? $"{indice + 1} de {total}" : "";

        var fi = new FileInfo(ruta);
        TNombre.Text = Path.GetFileName(ruta);
        string tipo = TipoBonito(ruta, modo);
        string tam = Directory.Exists(ruta) ? "Carpeta" : TamañoHumano(fi.Exists ? fi.Length : 0);
        _metaBase = $"{tipo}  |  {tam}  |  {(fi.Exists ? fi.LastWriteTime : DateTime.Now):yyyy-MM-dd HH:mm}";
        ActualizarMeta();

        AjustarTamaño(modo, fi);

        if (modo == ModoVista.ImagenNativa)
        {
            Web.Visibility = Visibility.Collapsed;
            Img.Visibility = Visibility.Visible;
            Cargando.Visibility = Visibility.Visible;
            CargarImagen(ruta, _cargaId);
        }
        else
        {
            Img.Visibility = Visibility.Collapsed;
            Web.Visibility = Visibility.Visible;
            if (_navegado && Web.CoreWebView2 != null)
            {
                // página ya cargada: el estado nuevo va por MENSAJE (sin recargar los
                // visores de Office) y el visor avisa con 'listo' cuando terminó
                Cargando.Visibility = Visibility.Visible;
                _ = EnviarEstadoAsync(_cargaId);
            }
            else
            {
                Cargando.Visibility = Visibility.Collapsed;
                _versionNavegacion++;
                Web.CoreWebView2?.Navigate(ServidorAssets.Origen + $"/viewer.html?p={_versionNavegacion}");
            }
        }

        // La pre-inicialización usa ShowActivated=false; la vista real se presenta
        // al frente aunque la aplicación estuviera en la bandeja.
        PresentarAlFrente();
        _vistaRealMostrada = true;
    }

    /// <summary>Oculta (el proceso y el WebView2 viven para la próxima).</summary>
    public void Dormir()
    {
        SalirPantallaCompleta();
        if (_info != null)
        {
            try { _info.Close(); } catch { }
            _info = null;
        }
        EnviarVisor("stop");
        _relojMeta.Stop();
        Hide();
    }

    /// <summary>Estado nuevo al visor ya cargado (sin re-navegar): Office instantáneo.</summary>
    private async System.Threading.Tasks.Task EnviarEstadoAsync(int id)
    {
        try
        {
            string json = await _servidor.EsperarJsonAsync();
            if (id != _cargaId) return;
            var nucleo = Web.CoreWebView2;
            try
            {
                using var estadoDoc = System.Text.Json.JsonDocument.Parse(json);
                string nombreEstado = estadoDoc.RootElement.TryGetProperty("nombre", out var n) ? n.GetString() ?? "" : "";
                string modoEstado = estadoDoc.RootElement.TryGetProperty("modo", out var m) ? m.GetString() ?? "" : "";
                ServidorAssets.Registrar("ESTADO archivo=" + nombreEstado + " modo=" + modoEstado + " id=" + id);
            }
            catch { }
            ServidorAssets.Registrar("ESTADO-MSG envio len=" + (json?.Length ?? 0) + " navegado=" + _navegado);
            if (nucleo != null && !string.IsNullOrEmpty(json))
                nucleo.PostWebMessageAsJson("{\"cmd\":\"estado\",\"estado\":" + json + "}");
        }
        catch (Exception ex)
        {
            ServidorAssets.Registrar("ESTADO-MSG EXCEPCION: " + ex.Message);
        }
    }

    // ---------- imagen nativa (camino rápido) ----------

    private async void CargarImagen(string ruta, int id)
    {
        try
        {
            var fuente = await System.Threading.Tasks.Task.Run(() =>
            {
                var fi = new FileInfo(ruta);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(ruta);
                if (fi.Length > 3_500_000) bi.DecodePixelWidth = 2600;
                bi.EndInit();
                bi.Freeze();

                ImageSource final = bi;
                int ori = LeerOrientacion(bi);
                if (ori >= 2 && ori <= 8)
                {
                    var tb = new TransformedBitmap { Source = bi };
                    switch (ori)
                    {
                        case 2: tb.Transform = Congelar(new ScaleTransform(-1, 1)); break;
                        case 3: tb.Transform = Congelar(new RotateTransform(180)); break;
                        case 4: tb.Transform = Congelar(new ScaleTransform(1, -1)); break;
                        case 5: tb.Transform = Congelar(Grupo(new RotateTransform(90), new ScaleTransform(-1, 1))); break;
                        case 6: tb.Transform = Congelar(new RotateTransform(90)); break;
                        case 7: tb.Transform = Congelar(Grupo(new RotateTransform(270), new ScaleTransform(-1, 1))); break;
                        case 8: tb.Transform = Congelar(new RotateTransform(270)); break;
                    }
                    final = tb;
                }
                return final;
            });

            if (id != _cargaId) return; // llegó otro archivo mientras tanto
            Img.Source = fuente;
            Cargando.Visibility = Visibility.Hidden;

            // tamaño natural de la imagen para ajustar la ventana
            if (fuente is BitmapSource bs && bs.PixelWidth > 0)
            {
                AjustarPorAspecto(bs.PixelWidth, bs.PixelHeight);
            }
        }
        catch
        {
            if (id == _cargaId)
            {
                Cargando.Visibility = Visibility.Hidden;
                TMeta.Text = "No se pudo mostrar la imagen  |  Enter para abrir con su app";
            }
        }
    }

    private static TransformGroup Grupo(params Transform[] transforms)
    {
        var g = new TransformGroup();
        foreach (var t in transforms) g.Children.Add(t);
        return g;
    }

    private static T Congelar<T>(T f) where T : System.Windows.Freezable
    {
        if (f.CanFreeze) f.Freeze();
        return f;
    }

    private static int LeerOrientacion(BitmapImage bi)
    {
        try
        {
            var frame = BitmapFrame.Create(bi);
            if (frame.Metadata is BitmapMetadata meta &&
                meta.ContainsQuery("/app1/ifd/exif/{ushort=274}"))
            {
                object? v = meta.GetQuery("/app1/ifd/exif/{ushort=274}");
                if (v is ushort u) return u;
                if (v is int i) return i;
            }
        }
        catch { }
        return 1;
    }

    // ---------- tamaño de ventana ----------

    private void AjustarTamaño(ModoVista modo, FileInfo fi)
    {
        var wa = SystemParameters.WorkArea;
        double pw = wa.Width * 0.86, ph = wa.Height * 0.86;
        (double w, double h) = modo switch
        {
            ModoVista.Pdf => (Math.Min(1000, pw), Math.Min(740, ph)),
            ModoVista.Html or ModoVista.Texto or ModoVista.Markdown => (Math.Min(1100, pw), Math.Min(720, ph)),
            ModoVista.Docx or ModoVista.Hoja => (Math.Min(1020, pw), Math.Min(700, ph)),
            ModoVista.Presentacion => (Math.Min(1040, pw), Math.Min(710, ph)),
            ModoVista.Modelo3d => (Math.Min(880, pw), Math.Min(650, ph)),
            ModoVista.Carpeta => (Math.Min(1000, pw), Math.Min(660, ph)),
            ModoVista.Zip => (Math.Min(920, pw), Math.Min(640, ph)),
            ModoVista.Fuente => (Math.Min(780, pw), Math.Min(540, ph)),
            ModoVista.Media => (Math.Min(960, pw), Math.Min(640, ph)),
            ModoVista.Tarjeta => (Math.Min(580, pw), Math.Min(360, ph)),
            _ => (Math.Min(960, pw), Math.Min(640, ph))
        };
        Width = w; Height = h;
        Centrar();
    }

    private void AjustarPorAspecto(int px, int py)
    {
        var wa = SystemParameters.WorkArea;
        double maxW = wa.Width * 0.86, maxH = wa.Height * 0.86;
        double k = Math.Min(maxW / px, maxH / py);
        // límites: no enana ni gigante
        k = Math.Clamp(k, 0.12, 1.6);
        Width = Math.Max(420, Math.Min(px * k + 44, maxW));
        Height = Math.Max(320, Math.Min(py * k + 100, maxH));
        Centrar();
    }

    private void Centrar()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 2;
    }

    // ---------- teclas y ratón ----------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (EntradaTeclado.InteractivoEnVisor) return;
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Key == Key.F)
            {
                e.Handled = true;
                Fijar_Click(this, new RoutedEventArgs());
                return;
            }
            if (e.Key == Key.I)
            {
                e.Handled = true;
                Info_Click(this, new RoutedEventArgs());
                return;
            }
        }
        switch (e.Key)
        {
            case Key.Space:
                e.Handled = true;
                if (_modo == ModoVista.Media) EnviarVisor("toggle");
                else if (!_fijado) _controlador.Cerrar(true);
                else AvisarQueEstaFijada();
                break;
            case Key.Escape:
                e.Handled = true;
                if (_pantallaCompleta) SalirPantallaCompleta();
                else if (!_fijado) _controlador.Cerrar(true);
                else AvisarQueEstaFijada();
                break;
            case Key.Left:
                e.Handled = true;
                _controlador.Navegar(-1);
                break;
            case Key.Right:
                e.Handled = true;
                _controlador.Navegar(1);
                break;
            case Key.Enter:
                e.Handled = true;
                // QuickLook: Enter abre el archivo con su app y cierra la vista
                _controlador.AbrirConApp();
                if (!_fijado) _controlador.Cerrar();
                break;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        double paso = e.Delta > 0 ? 1.15 : 1 / 1.15;
        if (_modo == ModoVista.ImagenNativa && Img.Source != null)
        {
            _escala = Math.Clamp(_escala * paso, 0.2, 8);
            var st = new ScaleTransform(_escala, _escala);
            Img.LayoutTransform = st;
            e.Handled = true;
        }
        else if (Web.Visibility == Visibility.Visible && Web.CoreWebView2 != null)
        {
            try { Web.ZoomFactor = Math.Clamp(Web.ZoomFactor * paso, 0.25, 5); } catch { }
            e.Handled = true;
        }
    }

    private void EnviarVisor(string cmd)
    {
        try { Web.CoreWebView2?.PostWebMessageAsJson("{\"cmd\":\"" + cmd + "\"}"); } catch { }
    }

    // El gancho global recibe estas teclas aunque Word/PDF/WebView2 tenga el foco.
    public void TeclaDesdeGancho(uint vk)
    {
        if (EntradaTeclado.InteractivoEnVisor) return;
        if (vk == 0x20)
        {
            if (_fijado) AvisarQueEstaFijada();
            else if (_modo == ModoVista.Media) EnviarVisor("toggle");
            else _controlador.Cerrar(true);
        }
        else if (vk == 0x1B)
        {
            if (_pantallaCompleta) SalirPantallaCompleta();
            else if (_fijado) AvisarQueEstaFijada();
            else _controlador.Cerrar(true);
        }
    }

    private void Barra_Arrastrar(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    private static bool EsPngCompatibleMinecraft(string ruta, ModoVista modo)
    {
        if (modo != ModoVista.ImagenNativa || !string.Equals(Path.GetExtension(ruta), ".png", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var frame = BitmapFrame.Create(new Uri(ruta), BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
            int w = frame.PixelWidth, h = frame.PixelHeight;
            return w >= 64 && w % 64 == 0 && (h == w || h == w / 2);
        }
        catch { return false; }
    }

    private void Minecraft_Click(object sender, RoutedEventArgs e)
    {
        if (_rutaActual == "" || !File.Exists(_rutaActual)) return;
        string json = "{\"modo\":\"skin\",\"nombre\":" + JsonEncoderEscape.Escape(Path.GetFileName(_rutaActual)) +
            ",\"ext\":\"png\",\"src\":\"/file/png\",\"tema\":\"" + _config.tema + "\",\"autogiro\":" + (_config.autogiro ? "true" : "false") + "}";
        _modo = ModoVista.ImagenWeb;
        Img.Visibility = Visibility.Collapsed;
        Web.Visibility = Visibility.Visible;
        _servidor.EstablecerEstadoDirecto(json);
        if (_navegado) EnviarEstadoAhora();
        else Web.CoreWebView2?.Navigate(ServidorAssets.Origen + "/viewer.html?p=" + (++_versionNavegacion));
    }

    public void AlternarFijado() => EstablecerFijado(!_fijado);

    public void EstablecerFijado(bool valor)
    {
        _fijado = valor;
        Topmost = _fijado;
        BFijar.Content = _fijado ? "Desfijar" : "Fijar";
        if (_info != null) _info.Topmost = _fijado;
        ServidorAssets.Registrar("FIJAR " + (_fijado ? "activado" : "desactivado") + " topmost=" + Topmost);
        ActualizarMeta();
        PresentarAlFrente();
    }

    private void Fijar_Click(object sender, RoutedEventArgs e) => AlternarFijado();

    private async void Info_Click(object sender, RoutedEventArgs e)
    {
        if (_rutaActual == "") return;
        if (_info != null)
        {
            try { _info.Close(); } catch { }
            _info = null;
            return;
        }
        var ventana = new VentanaInfo(_rutaActual, _config.tema == "oscuro", _fijado);
        _info = ventana;
        ventana.Closed += (_, _) => { if (ReferenceEquals(_info, ventana)) _info = null; };
        ventana.Show();
        try
        {
            var datos = await InformacionArchivo.ObtenerAsync(_rutaActual, _modo);
            if (ReferenceEquals(_info, ventana))
                ventana.Actualizar(datos.Select(d => new CampoInfo(d.Clave, d.Valor)));
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_info, ventana))
                ventana.Actualizar(new[] { new CampoInfo("Error", ex.Message) });
        }
    }

    private void Codigo_Click(object sender, RoutedEventArgs e)
    {
        _vistaAlterna = !_vistaAlterna;
        _dividiendo = false;
        BDividir.Content = "Dividir";
        EnviarVisor(_vistaAlterna ? "codigo" : "pagina");
        BCodigo.Content = _vistaAlterna ? (_modo == ModoVista.Html ? "Página" : "Markdown") : "Código";
    }

    private void Dividir_Click(object sender, RoutedEventArgs e)
    {
        _dividiendo = !_dividiendo;
        _vistaAlterna = false;
        BCodigo.Content = "Código";
        BDividir.Content = _dividiendo ? "Unir" : "Dividir";
        EnviarVisor(_dividiendo ? "dividir" : "pagina");
    }

    private void ActualizarMeta()
    {
        TMeta.Text = (_fijado ? "FIJADA · Desfija para cerrar  |  " : "") + _metaBase +
            (_fijado ? "" : "  |  Espacio/Esc cierra · ←/→ navega");
    }

    public void AvisarQueEstaFijada()
    {
        TMeta.Text = "Vista fijada: pulsa Desfijar y después podrás cerrarla";
    }

    private void Abrir_Click(object sender, RoutedEventArgs e) => _controlador.AbrirConApp();
    private void Cerrar_Click(object sender, RoutedEventArgs e)
    {
        if (_fijado) AvisarQueEstaFijada();
        else _controlador.Cerrar(true);
    }

    // ---------- helpers ----------

    private void RefrescarMetaMedia() { /* reservado */ }

    private static string TamañoHumano(long b)
    {
        if (b < 1024) return $"{b} B";
        if (b < 1024 * 1024) return $"{b / 1024.0:0.#} KB";
        if (b < 1024L * 1024 * 1024) return $"{b / 1048576.0:0.#} MB";
        return $"{b / 1073741824.0:0.##} GB";
    }

    private static string TipoBonito(string ruta, ModoVista modo)
    {
        string ext = Path.GetExtension(ruta).TrimStart('.').ToUpperInvariant();
        return modo switch
        {
            ModoVista.ImagenNativa or ModoVista.ImagenWeb or ModoVista.ImagenExtendida => $"Imagen {ext}",
            ModoVista.Media => PreviewRouter.EsAudio(Path.GetExtension(ruta)) ? $"Audio {ext}" : $"Video {ext}",
            ModoVista.Pdf => "Documento PDF",
            ModoVista.Html => "Página HTML",
            ModoVista.Texto => string.IsNullOrEmpty(ext) ? "Texto" : $"Texto {ext}",
            ModoVista.Markdown => "Markdown",
            ModoVista.Docx => "Word (docx)",
            ModoVista.Hoja => $"Hoja {ext}",
            ModoVista.Presentacion => "PowerPoint",
            ModoVista.Zip => $"Comprimido {ext}",
            ModoVista.Modelo3d => $"Modelo 3D {ext}",
            ModoVista.Fuente => $"Fuente {ext}",
            ModoVista.Carpeta => "Carpeta",
            _ => string.IsNullOrEmpty(ext) ? "Archivo" : $"Archivo {ext}"
        };
    }
}

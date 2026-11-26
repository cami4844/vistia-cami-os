using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Vistia.Core;

namespace Vistia;

public partial class App : Application
{
    private static Mutex? _unico;
    private KeyboardHook? _gancho;
    private Config _config = new();
    private VistaController? _controlador;
    private System.Windows.Forms.NotifyIcon? _bandeja;
    private bool _modoQa;
    private VistaInicio? _inicio;
    private EventWaitHandle? _despertar;
    private System.Windows.Forms.ToolStripMenuItem? _pausaItem;
    private System.Windows.Forms.ToolStripMenuItem? _fijarItem;
    private System.Windows.Forms.ToolStripMenuItem? _ultimaItem;
    private System.Windows.Forms.ToolStripMenuItem? _autoinicioItem;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // quedarse residente en la bandeja: cerrar la vista NO mata la app (solo Salir lo hace)
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // registro global de fallos para diagnóstico
        ServidorAssets.Registrar("ARRANQUE args=" + string.Join(" ", e.Args));
        DispatcherUnhandledException += (_, ex) =>
        {
            ServidorAssets.Registrar("CRASH-DISPATCHER: " + ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            ServidorAssets.Registrar("CRASH-DOMINIO: " + ex.ExceptionObject);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            ServidorAssets.Registrar("CRASH-TASK: " + ex.Exception);
            ex.SetObserved();
        };

        if (EsperarSalidaAnterior(e.Args))
        {
            Shutdown();
            return;
        }

        // modo QA/determinista: Vistia.exe --preview "ruta1" ["ruta2" ...]
        // varias rutas = secuencial en la MISMA instancia (prueba del canal de mensajes)
        if (e.Args.Length >= 2 && e.Args[0] == "--preview")
        {
            _modoQa = true;
            IniciarNucleo();
            var rutasQa = new List<string>();
            for (int i = 1; i < e.Args.Length; i++) rutasQa.Add(e.Args[i]);
            _ = SecuenciaQa(rutasQa);
            return;
        }

        // instancia única: una segunda ejecución DESPIERTA la ventana de la primera
        _unico = new Mutex(true, "Vistia.InstanciaUnica", out bool nueva);
        if (!nueva)
        {
            if (EventWaitHandle.TryOpenExisting("Vistia.Despertar", out var existente))
            {
                existente.Set();
                existente.Dispose();
            }
            Shutdown();
            return;
        }
        _despertar = new EventWaitHandle(false, EventResetMode.AutoReset, "Vistia.Despertar");
        var vigilante = new System.Threading.Thread(() =>
        {
            while (_despertar.WaitOne())
            {
                Dispatcher.BeginInvoke(() => MostrarInicio());
            }
        }) { IsBackground = true, Name = "Vistia-Vigilante" };
        vigilante.Start();

        IniciarNucleo();
        // Al iniciar Windows, solo bandeja y precalentamiento oculto.
        // Inicio se abre únicamente cuando Cami lo solicita.
    }

    private async System.Threading.Tasks.Task SecuenciaQa(List<string> rutas)
    {
        foreach (var ruta in rutas)
        {
            if (!File.Exists(ruta) && !Directory.Exists(ruta))
            {
                ServidorAssets.Registrar("QA-RUTA NOEXISTE " + ruta);
                continue; // sin MessageBox: el modo QA no debe bloquearse
            }
            ServidorAssets.Registrar("QA-RUTA " + ruta);
            _controlador!.AbrirRutaDirecta(ruta);
            await System.Threading.Tasks.Task.Delay(4200);
        }
    }

    private void IniciarNucleo()
    {
        ServidorAssets.Registrar("NUCLEO inicio");
        _config = Config.Cargar();
        string raizAssets = Path.Combine(AppContext.BaseDirectory, "Assets");
        var servidor = new ServidorAssets(raizAssets);
        _controlador = new VistaController(_config, servidor);
        ServidorAssets.Registrar("NUCLEO objetos listos");

        if (!_modoQa)
        {
            _gancho = new KeyboardHook(ServidorAssets.Registrar) { Activo = !_config.pausado };
            _gancho.SpacePressed += (hwnd, clase) => _ = _controlador.EspacioAsync(hwnd, clase);
            _gancho.SpaceVPressed += async (hwnd, clase) =>
            {
                ServidorAssets.Registrar("ESPACIO+V clase=" + clase + " visible=" + _controlador.Visible);
                if (_controlador.Visible) _controlador.TeclaEnVista(0x20);
                else if (!await _controlador.EspacioAsync(hwnd, clase))
                {
                    if (!string.IsNullOrEmpty(_controlador.RutaActual)) _controlador.AbrirRutaDirecta(_controlador.RutaActual);
                    else MostrarInicio();
                }
            };
            _gancho.PreviewKeyPressed += (vk, _, _) => _controlador.TeclaEnVista(vk);
            _gancho.CtrlSpacePressed += () =>
            {
                if (_controlador.Visible) _controlador.TeclaEnVista(0x20);
                else if (!string.IsNullOrEmpty(_controlador.RutaActual)) _controlador.AbrirRutaDirecta(_controlador.RutaActual);
                else MostrarInicio();
            };
            CrearBandeja();
            if (_config.autoinicio) AsegurarAutoinicio(true);
            ServidorAssets.Registrar("NUCLEO gancho y bandeja listos");
        }

        // precalentar WebView2 en segundo plano para que la primera vista sea instantánea
        _controlador.Precalentar();
        ServidorAssets.Registrar("NUCLEO fin");
    }

    // ---------- bandeja ----------

    private void CrearBandeja()
    {
        _bandeja = new System.Windows.Forms.NotifyIcon
        {
            Text = "Vistia · vista previa local",
            Visible = true
        };
        try
        {
            _bandeja.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
                            ?? System.Drawing.SystemIcons.Application;
        }
        catch { _bandeja.Icon = System.Drawing.SystemIcons.Application; }

        var menu = new MenuVistia();
        var titulo = new System.Windows.Forms.ToolStripMenuItem("Vistia 1.0.1 · Cami-OS") { Enabled = false };
        titulo.Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold);
        var abrir = new System.Windows.Forms.ToolStripMenuItem("Ventana de inicio");
        abrir.Click += (_, _) => MostrarInicio();
        var elegir = new System.Windows.Forms.ToolStripMenuItem("Abrir archivo…");
        elegir.Click += (_, _) => ElegirArchivo();
        _ultimaItem = new System.Windows.Forms.ToolStripMenuItem("Mostrar última vista");
        _ultimaItem.Click += (_, _) => MostrarUltimaVista();
        _fijarItem = new System.Windows.Forms.ToolStripMenuItem("Fijar vista");
        _fijarItem.Click += (_, _) => _controlador?.AlternarFijado();
        _pausaItem = new System.Windows.Forms.ToolStripMenuItem("Pausar activación") { CheckOnClick = true };
        _pausaItem.Click += (_, _) => EstablecerPausa(_pausaItem.Checked);
        var tema = new System.Windows.Forms.ToolStripMenuItem("Tema claro/oscuro");
        tema.Click += (_, _) => _controlador?.AlternarTema();
        _autoinicioItem = new System.Windows.Forms.ToolStripMenuItem("Iniciar con Windows") { CheckOnClick = true };
        _autoinicioItem.Click += (_, _) => EstablecerAutoinicio(_autoinicioItem.Checked);
        var registro = new System.Windows.Forms.ToolStripMenuItem("Abrir registro de diagnóstico");
        registro.Click += (_, _) => AbrirRegistro();
        var reiniciar = new System.Windows.Forms.ToolStripMenuItem("Reiniciar Vistia");
        reiniciar.Click += (_, _) => Reiniciar();
        var salir = new System.Windows.Forms.ToolStripMenuItem("Salir");
        salir.Click += (_, _) => Shutdown();

        menu.Items.Add(titulo);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(abrir);
        menu.Items.Add(elegir);
        menu.Items.Add(_ultimaItem);
        menu.Items.Add(_fijarItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_pausaItem);
        menu.Items.Add(tema);
        menu.Items.Add(_autoinicioItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(registro);
        menu.Items.Add(reiniciar);
        menu.Items.Add(salir);
        menu.Opening += (_, _) => RefrescarMenu();
        _bandeja.ContextMenuStrip = menu;
        _bandeja.DoubleClick += (_, _) => MostrarInicio();
        RefrescarMenu();
    }

    private void RefrescarMenu()
    {
        if (_pausaItem != null)
        {
            _pausaItem.Checked = _config.pausado;
            _pausaItem.Text = _config.pausado ? "Reanudar activación" : "Pausar activación";
        }
        if (_fijarItem != null)
        {
            _fijarItem.Enabled = _controlador?.Visible == true;
            _fijarItem.Text = _controlador?.FijadoActual == true ? "Desfijar vista" : "Fijar vista";
        }
        if (_ultimaItem != null) _ultimaItem.Enabled = !string.IsNullOrEmpty(_controlador?.RutaActual);
        if (_autoinicioItem != null) _autoinicioItem.Checked = _config.autoinicio;
    }

    /// <summary>Ventana de inicio: estado del servicio + probar un archivo sin el Explorador.</summary>
    private void MostrarInicio()
    {
        if (_modoQa) return;
        if (_inicio == null)
        {
            _inicio = new VistaInicio();
            _inicio.AlReiniciar = Reiniciar;
            _inicio.AlProbarRuta = ruta =>
            {
                if (_controlador != null && File.Exists(ruta)) _controlador.AbrirRutaDirecta(ruta);
            };
            _inicio.Conectar(
                () => _gancho is { Activo: true, Instalado: true },
                EstablecerPausa,
                () => _config.autoinicio,
                EstablecerAutoinicio,
                Shutdown);
        }
        _inicio.Closed += (_, _) => _inicio = null;
        _inicio.Show();
        _inicio.Activate();
        _inicio.Refrescar();
    }

    private void ElegirArchivo()
    {
        using var dlg = new System.Windows.Forms.OpenFileDialog
        {
            Title = "Abrir con Vistia",
            Filter = "Todos los archivos|*.*"
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            _controlador?.AbrirRutaDirecta(dlg.FileName);
    }

    private void MostrarUltimaVista()
    {
        string ruta = _controlador?.RutaActual ?? "";
        if (ruta.Length > 0 && (File.Exists(ruta) || Directory.Exists(ruta))) _controlador!.AbrirRutaDirecta(ruta);
        else MostrarInicio();
    }

    private void EstablecerPausa(bool pausado)
    {
        if (_gancho != null) _gancho.Activo = !pausado;
        _config.pausado = pausado;
        _config.Guardar();
        ServidorAssets.Registrar(pausado ? "ACTIVACION pausada" : "ACTIVACION reanudada");
        RefrescarMenu();
        _inicio?.Refrescar();
    }

    private void EstablecerAutoinicio(bool activar)
    {
        _config.autoinicio = activar;
        _config.Guardar();
        AsegurarAutoinicio(activar);
        RefrescarMenu();
    }

    private void Reiniciar()
    {
        try
        {
            string exe = Environment.ProcessPath ?? throw new InvalidOperationException("No se conoce el ejecutable actual");
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
            psi.ArgumentList.Add("--reiniciar");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(psi);
            ServidorAssets.Registrar("REINICIO solicitado");
            Shutdown();
        }
        catch (Exception ex)
        {
            ServidorAssets.Registrar("REINICIO EXCEPCION: " + ex.Message);
            MessageBox.Show("No se pudo reiniciar Vistia: " + ex.Message, "Vistia");
        }
    }

    private static bool EsperarSalidaAnterior(string[] args)
    {
        if (args.Length != 2 || args[0] != "--reiniciar" || !int.TryParse(args[1], out int pid)) return false;
        try
        {
            using var anterior = Process.GetProcessById(pid);
            if (!anterior.WaitForExit(15000) && !anterior.HasExited)
            {
                ServidorAssets.Registrar("REINICIO cancelado: el proceso anterior no terminó");
                return true;
            }
        }
        catch { }
        return false;
    }

    private void AbrirRegistro()
    {
        try
        {
            string ruta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Vistia", "registro.log");
            if (File.Exists(ruta)) Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
            else MostrarInicio();
        }
        catch { }
    }

    private void AsegurarAutoinicio(bool activar)
    {
        try
        {
            string carpeta = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string lnk = Path.Combine(carpeta, "Vistia.lnk");
            string? exe = Environment.ProcessPath;
            if (exe == null) return;

            if (activar)
            {
                dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"))!;
                dynamic atajo = sh.CreateShortcut(lnk);
                atajo.TargetPath = exe;
                atajo.WorkingDirectory = Path.GetDirectoryName(exe)!;
                atajo.IconLocation = exe + ",0";
                atajo.Description = "Vistia · vista previa universal · Cami-OS";
                atajo.Save();
            }
            else if (File.Exists(lnk))
            {
                File.Delete(lnk);
            }
        }
        catch (Exception ex) { ServidorAssets.Registrar("AUTOINICIO EXCEPCION: " + ex.Message); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _gancho?.Dispose(); } catch (Exception ex) { ServidorAssets.Registrar("SALIDA gancho: " + ex.Message); }
        if (_bandeja != null) { _bandeja.Visible = false; _bandeja.Dispose(); }
        _despertar?.Set();
        _despertar?.Dispose();
        _inicio?.Close();
        try { _unico?.ReleaseMutex(); } catch { }
        _unico?.Dispose();
        base.OnExit(e);
    }
}

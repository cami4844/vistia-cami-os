using System;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Vistia.Core;

namespace Vistia;

/// <summary>
/// Ventana de inicio: muestra el estado del servicio y permite probar una vista
/// sin tener que ir al Explorador. Cerrarla NO apaga la aplicación.
/// </summary>
public partial class VistaInicio : Window
{
    /// <summary>Pide abrir una ruta en la vista (la App la enlaza al controlador).</summary>
    public Action<string>? AlProbarRuta;
    public Action? AlReiniciar;

    private Func<bool> _estaActiva = () => true;
    private Action<bool> _alPausar = _ => { };
    private Func<bool> _autoinicioActual = () => false;
    private Action<bool> _alAutoinicio = _ => { };
    private Action _alSalir = () => { };
    private bool _silencioAutoinicio;

    public VistaInicio()
    {
        InitializeComponent();
        BProbar.Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x5A, 0x32));
    }

    public void Conectar(Func<bool> estaActiva, Action<bool> alPausar,
                         Func<bool> autoinicioActual, Action<bool> alAutoinicio, Action alSalir)
    {
        _estaActiva = estaActiva;
        _alPausar = alPausar;
        _autoinicioActual = autoinicioActual;
        _alAutoinicio = alAutoinicio;
        _alSalir = alSalir;
        _silencioAutoinicio = true;
        CAutoinicio.IsChecked = autoinicioActual();
        _silencioAutoinicio = false;
        Refrescar();
    }

    public void Refrescar()
    {
        bool activa = _estaActiva();
        Punto.Fill = new SolidColorBrush(activa ? Color.FromRgb(0x3E, 0x7C, 0x4F)
                                                : Color.FromRgb(0xC0, 0x5A, 0x32));
        TEstado.Text = activa ? "Servicio ACTIVO" : "Servicio PAUSADO";
        BPausar.Content = activa ? "Pausar" : "Reanudar";
    }

    private void ProbarClick(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.OpenFileDialog
        {
            Title = "Elegir un archivo para la vista previa",
            Filter = "Modelos 3D|*.stl;*.glb;*.gltf;*.obj;*.ply;*.fbx;*.3ds;*.dae;*.3mf" +
                     "|Imágenes|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.tiff" +
                     "|Documentos|*.pdf;*.docx;*.xlsx;*.pptx;*.csv" +
                     "|Texto y código|*.txt;*.md;*.py;*.js;*.html;*.css;*.json" +
                     "|Todos los archivos|*.*"
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            AlProbarRuta?.Invoke(dlg.FileName);
    }

    private void PausarClick(object sender, RoutedEventArgs e)
    {
        _alPausar(_estaActiva()); // si está activa -> pausar; si pausada -> reanudar
        Refrescar();
    }

    private void AutoinicioChange(object sender, RoutedEventArgs e)
    {
        if (_silencioAutoinicio) return;
        _alAutoinicio(CAutoinicio.IsChecked == true);
    }

    private void ReiniciarClick(object sender, RoutedEventArgs e) => AlReiniciar?.Invoke();

    private void SalirClick(object sender, RoutedEventArgs e) => _alSalir();
}

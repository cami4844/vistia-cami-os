using System.Windows;

namespace Vistia;

/// <summary>Estado visual de pantalla completa, separado del ciclo de vida del visor.</summary>
public partial class PreviewWindow
{
    private bool _pantallaCompleta;
    private WindowState _estadoAnterior;
    private double _izquierdaAnterior, _arribaAnterior, _anchoAnterior, _altoAnterior;
    private Visibility _barraAnterior;

    public bool EnPantallaCompleta => _pantallaCompleta;

    public void AlternarPantallaCompleta()
    {
        if (_pantallaCompleta) SalirPantallaCompleta();
        else EntrarPantallaCompleta();
    }

    public void EntrarPantallaCompleta()
    {
        if (_pantallaCompleta) return;
        _estadoAnterior = WindowState;
        _izquierdaAnterior = Left; _arribaAnterior = Top;
        _anchoAnterior = Width; _altoAnterior = Height;
        _barraAnterior = Barra.Visibility;
        _pantallaCompleta = true;
        Barra.Visibility = Visibility.Collapsed;
        Topmost = true;
        WindowState = WindowState.Maximized;
    }

    public void SalirPantallaCompleta()
    {
        if (!_pantallaCompleta) return;
        _pantallaCompleta = false;
        WindowState = _estadoAnterior;
        Left = _izquierdaAnterior; Top = _arribaAnterior;
        Width = _anchoAnterior; Height = _altoAnterior;
        // El estado actual de fijado manda, aunque se haya cambiado en fullscreen.
        Topmost = _fijado;
        Barra.Visibility = _barraAnterior;
    }
}

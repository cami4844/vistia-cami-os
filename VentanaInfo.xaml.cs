using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Clipboard = System.Windows.Clipboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Vistia.Core;

namespace Vistia;

public sealed record CampoInfo(string Clave, string Valor);

public partial class VentanaInfo : Window
{
    private readonly string _ruta;
    private readonly bool _oscuro;
    private List<CampoInfo> _campos = new();

    public VentanaInfo(string ruta, bool oscuro, bool siempreEncima)
    {
        InitializeComponent();
        _ruta = ruta;
        _oscuro = oscuro;
        Topmost = siempreEncima;
        AplicarTema();
        Campos.ItemsSource = new[] { new CampoInfo("Estado", "Leyendo información local…") };
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 28;
        Top = area.Top + 42;
    }

    public void Actualizar(IEnumerable<CampoInfo> campos)
    {
        _campos = campos.ToList();
        Campos.ItemsSource = _campos;
    }

    private void AplicarTema()
    {
        var papel = new SolidColorBrush(_oscuro ? Color.FromRgb(0x1F, 0x1A, 0x16) : Color.FromRgb(0xF7, 0xF1, 0xE7));
        var banda = new SolidColorBrush(_oscuro ? Color.FromRgb(0x2A, 0x24, 0x20) : Color.FromRgb(0xED, 0xE3, 0xD5));
        var tinta = new SolidColorBrush(_oscuro ? Color.FromRgb(0xE8, 0xDF, 0xD4) : Color.FromRgb(0x2E, 0x26, 0x20));
        Background = papel;
        Marco.Background = papel;
        Cabecera.Background = banda;
        Pie.Background = banda;
        Titulo.Foreground = tinta;
        BCerrarInfo.Foreground = tinta;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void Arrastrar(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();

    private void CopiarRuta_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_ruta); BCopiarRuta.Content = "Ruta copiada"; } catch { }
    }

    private void CopiarTodo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var c in _campos) sb.Append(c.Clave).Append(": ").AppendLine(c.Valor);
            Clipboard.SetText(sb.ToString());
            BCopiarTodo.Content = "Información copiada";
        }
        catch { }
    }

    private void AbrirCarpeta_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + _ruta + "\"") { UseShellExecute = true });
        }
        catch { }
    }
}

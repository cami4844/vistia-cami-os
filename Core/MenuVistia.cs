using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Vistia.Core;

/// <summary>Menú de bandeja con la identidad tinta/terracota y esquinas redondeadas.</summary>
public sealed class MenuVistia : ContextMenuStrip
{
    public MenuVistia()
    {
        BackColor = Color.FromArgb(0x25, 0x21, 0x1D);
        ForeColor = Color.FromArgb(0xF7, 0xF1, 0xE7);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        ShowImageMargin = false;
        ShowCheckMargin = true;
        Padding = new Padding(6);
        DropShadowEnabled = true;
        Renderer = new RenderizadorVistia();
        Redondear();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Redondear();
    }

    protected override void OnItemAdded(ToolStripItemEventArgs e)
    {
        base.OnItemAdded(e);
        e.Item.Padding = new Padding(8, 5, 8, 5);
    }

    private void Redondear()
    {
        if (Width <= 0 || Height <= 0) return;
        using var ruta = GeometriaMenu.Ruta(new Rectangle(0, 0, Width, Height), 14);
        Region?.Dispose();
        Region = new Region(ruta);
    }
}

internal static class GeometriaMenu
{
    public static GraphicsPath Ruta(Rectangle area, int radio)
    {
        int d = Math.Max(2, radio * 2);
        var ruta = new GraphicsPath();
        ruta.AddArc(area.X, area.Y, d, d, 180, 90);
        ruta.AddArc(area.Right - d, area.Y, d, d, 270, 90);
        ruta.AddArc(area.Right - d, area.Bottom - d, d, d, 0, 90);
        ruta.AddArc(area.X, area.Bottom - d, d, d, 90, 90);
        ruta.CloseFigure();
        return ruta;
    }
}

internal sealed class RenderizadorVistia : ToolStripProfessionalRenderer
{
    private static readonly Color Papel = Color.FromArgb(0xF7, 0xF1, 0xE7);
    private static readonly Color Suave = Color.FromArgb(0xA8, 0x98, 0x86);
    private static readonly Color Terracota = Color.FromArgb(0xC0, 0x5A, 0x32);

    public RenderizadorVistia() : base(new ColoresVistia()) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected)
        {
            base.OnRenderMenuItemBackground(e);
            return;
        }
        var area = e.Item.ContentRectangle;
        area.Inflate(-2, -1);
        if (area.Width <= 0 || area.Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var ruta = GeometriaMenu.Ruta(area, 7);
        using var brocha = new SolidBrush(Terracota);
        e.Graphics.FillPath(brocha, ruta);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? Suave : e.Item.Selected ? Color.White : Papel;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var area = e.Item.ContentRectangle;
        int y = area.Top + area.Height / 2;
        using var lapiz = new Pen(Color.FromArgb(0x56, 0x4A, 0x40));
        e.Graphics.DrawLine(lapiz, area.Left + 8, y, area.Right - 8, y);
    }
}

internal sealed class ColoresVistia : ProfessionalColorTable
{
    public override Color MenuBorder => Color.FromArgb(0x62, 0x50, 0x44);
    public override Color ToolStripDropDownBackground => Color.FromArgb(0x25, 0x21, 0x1D);
    public override Color MenuItemSelected => Color.FromArgb(0xC0, 0x5A, 0x32);
    public override Color MenuItemBorder => Color.Transparent;
    public override Color CheckBackground => Color.FromArgb(0x3A, 0x30, 0x28);
    public override Color CheckSelectedBackground => Color.FromArgb(0xC0, 0x5A, 0x32);
    public override Color CheckPressedBackground => Color.FromArgb(0xC0, 0x5A, 0x32);
    public override Color SeparatorDark => Color.FromArgb(0x56, 0x4A, 0x40);
    public override Color SeparatorLight => Color.FromArgb(0x56, 0x4A, 0x40);
}

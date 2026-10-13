using System.ComponentModel;
using DroneLive.Core;

namespace DroneLive.App;

/// <summary>Kachel mit abgerundeten Ecken und dünnem Rand, etwa für die Vorschau.</summary>
internal sealed class RoundedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Eckenradius { get; set; } = 12;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Padding = new Padding(1);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        // Die Region schneidet auch das Vorschaubild an den Ecken ab.
        using (var pfad = BrandGraphics.RundesRechteck(new RectangleF(0, 0, Width, Height), LogicalToDeviceUnits(Eckenradius)))
            Region = new Region(pfad);
        base.OnSizeChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pfad = BrandGraphics.RundesRechteck(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), LogicalToDeviceUnits(Eckenradius));
        using var stift = new Pen(AppTheme.Linie);
        e.Graphics.DrawPath(stift, pfad);
    }
}

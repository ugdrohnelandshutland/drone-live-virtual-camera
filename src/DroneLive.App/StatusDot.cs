using System.ComponentModel;

namespace DroneLive.App;

/// <summary>Farbiger Statuspunkt vor „Verbunden“, „Kein Signal“ oder „Fehler“.</summary>
internal sealed class StatusDot : Control
{
    public StatusDot()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Farbe { get; set { if (Equals(field, value)) return; field = value; Invalidate(); } } = AppTheme.KeinSignal;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int groesse = Math.Min(Width, Height) - 1;
        using var pinsel = new SolidBrush(Farbe);
        e.Graphics.FillEllipse(pinsel, (Width - groesse) / 2f, (Height - groesse) / 2f, groesse, groesse);
    }
}

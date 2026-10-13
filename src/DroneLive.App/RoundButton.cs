using System.ComponentModel;
using DroneLive.Core;

namespace DroneLive.App;

/// <summary>Schaltfläche mit abgerundeten Ecken wie auf der Hub-Testseite; die Farben setzt das Formular je Zustand.</summary>
internal sealed class RoundButton : Button
{
    private bool _ueber;

    public RoundButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Hintergrund { get; set { if (Equals(field, value)) return; field = value; Invalidate(); } } = AppTheme.Flaeche;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverHintergrund { get; set { if (Equals(field, value)) return; field = value; Invalidate(); } } = Color.Empty;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Rand { get; set { if (Equals(field, value)) return; field = value; Invalidate(); } } = AppTheme.LinieStark;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Schriftfarbe { get; set { if (Equals(field, value)) return; field = value; Invalidate(); } } = AppTheme.Text;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Untertitel { get; set { if (Equals(field, value)) return; field = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Eckenradius { get; set; } = 7;

    protected override void OnMouseEnter(EventArgs e) { _ueber = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _ueber = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var grafik = e.Graphics;
        // Ecken in der ersten echten Hintergrundfarbe darüber füllen; durchsichtige Container ergäben Schwarz.
        Color umgebung = AppTheme.Grund;
        for (Control? eltern = Parent; eltern != null; eltern = eltern.Parent)
            if (eltern.BackColor.A == 255) { umgebung = eltern.BackColor; break; }
        grafik.Clear(umgebung);
        grafik.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var flaeche = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color hintergrund = Enabled && _ueber && !HoverHintergrund.IsEmpty ? HoverHintergrund : Hintergrund;
        using (var pfad = BrandGraphics.RundesRechteck(flaeche, LogicalToDeviceUnits(Eckenradius)))
        using (var pinsel = new SolidBrush(hintergrund))
        using (var stift = new Pen(Rand))
        {
            grafik.FillPath(pinsel, pfad);
            grafik.DrawPath(stift, pfad);
        }
        if (Focused && ShowFocusCues)
        {
            int abstand = LogicalToDeviceUnits(3);
            using var fokus = BrandGraphics.RundesRechteck(new RectangleF(abstand, abstand, Width - 2 * abstand - 1, Height - 2 * abstand - 1), Math.Max(1, LogicalToDeviceUnits(Eckenradius) - abstand));
            using var fokusStift = new Pen(hintergrund == AppTheme.Flaeche || hintergrund == AppTheme.Gesperrt ? AppTheme.Akzent : Color.White, LogicalToDeviceUnits(2));
            grafik.DrawPath(fokusStift, fokus);
        }
        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        if (!string.IsNullOrEmpty(Untertitel))
        {
            var oben = TextRenderer.MeasureText(grafik, Text, Font, Size.Empty, flags);
            var unten = TextRenderer.MeasureText(grafik, Untertitel, AppTheme.Klein, Size.Empty, flags);
            int y = (Height - oben.Height - unten.Height) / 2;
            TextRenderer.DrawText(grafik, Text, Font, new Point((Width - oben.Width) / 2, y), Schriftfarbe, flags);
            TextRenderer.DrawText(grafik, Untertitel, AppTheme.Klein, new Point((Width - unten.Width) / 2, y + oben.Height), Schriftfarbe, flags);
            return;
        }
        var groesse = TextRenderer.MeasureText(grafik, Text, Font, Size.Empty, flags);
        int luecke = Image == null || Text.Length == 0 ? 0 : LogicalToDeviceUnits(10);
        int bildBreite = Image?.Width ?? 0;
        int x = (Width - bildBreite - luecke - groesse.Width) / 2;
        if (Image != null) grafik.DrawImage(Image, x, (Height - Image.Height) / 2, Image.Width, Image.Height);
        TextRenderer.DrawText(grafik, Text, Font, new Point(x + bildBreite + luecke, (Height - groesse.Height) / 2), Schriftfarbe, flags);
    }
}

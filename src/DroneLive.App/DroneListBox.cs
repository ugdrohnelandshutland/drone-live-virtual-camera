using DroneLive.Core;

namespace DroneLive.App;

/// <summary>Drohnenliste als Kacheln wie auf der Hub-Testseite; zeigt vier Einträge und scrollt darüber hinaus.</summary>
internal sealed class DroneListBox : ListBox
{
    public const int Zeilenhoehe = 58, Abstand = 6, SichtbareZeilen = 4;

    public DroneListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        BackColor = AppTheme.Grund;
        ItemHeight = LogicalToDeviceUnits(Zeilenhoehe);
    }

    /// <summary>Höhe für genau vier Einträge; die letzte Lücke entfällt.</summary>
    public int SichtbareHoehe => SichtbareZeilen * ItemHeight - LogicalToDeviceUnits(Abstand);

    protected override void OnHandleCreated(EventArgs e)
    {
        ItemHeight = LogicalToDeviceUnits(Zeilenhoehe);
        base.OnHandleCreated(e);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        ItemHeight = LogicalToDeviceUnits(Zeilenhoehe);
        base.OnDpiChangedAfterParent(e);
    }

    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var grafik = e.Graphics;
        using (var grund = new SolidBrush(AppTheme.Grund)) grafik.FillRectangle(grund, e.Bounds);
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not Drohne drohne) return;
        bool gewaehlt = (e.State & DrawItemState.Selected) != 0;
        // Gesperrt bleibt nur die gewählte Drohne hervorgehoben, alle anderen werden zurückgenommen.
        bool aktiv = Enabled || gewaehlt;
        grafik.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int luecke = LogicalToDeviceUnits(Abstand);
        var karte = new RectangleF(e.Bounds.X + 1, e.Bounds.Y + 1, e.Bounds.Width - LogicalToDeviceUnits(4), e.Bounds.Height - luecke - 2);
        float radius = LogicalToDeviceUnits(12);
        int nummerBreite = LogicalToDeviceUnits(46);
        Color slot = drohne.IstLive && aktiv ? AppTheme.Live : AppTheme.SlotLeer;
        Color nummerFarbe = drohne.IstLive && aktiv ? Color.White : AppTheme.Text;
        Color schrift = aktiv ? AppTheme.Text : AppTheme.Leise;
        using (var pfad = BrandGraphics.RundesRechteck(karte, radius))
        {
            var clip = grafik.Clip;
            grafik.SetClip(pfad, System.Drawing.Drawing2D.CombineMode.Intersect);
            using (var flaeche = new SolidBrush(aktiv ? AppTheme.Flaeche : AppTheme.Gesperrt)) grafik.FillRectangle(flaeche, karte);
            using (var nummer = new SolidBrush(slot)) grafik.FillRectangle(nummer, karte.X, karte.Y, nummerBreite, karte.Height);
            grafik.Clip = clip;
            using var rand = new Pen(gewaehlt ? AppTheme.Akzent : AppTheme.Linie, gewaehlt ? LogicalToDeviceUnits(2) : 1);
            grafik.DrawPath(rand, pfad);
        }
        var nummerFeld = new Rectangle((int)karte.X, (int)karte.Y, nummerBreite, (int)karte.Height);
        TextRenderer.DrawText(grafik, (e.Index + 1).ToString(), AppTheme.Nummer, nummerFeld, nummerFarbe,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        string status = drohne.IstLive ? "Live" : "— Offline";
        Color statusFarbe = drohne.IstLive && aktiv ? AppTheme.Live : AppTheme.Leise;
        const TextFormatFlags einzeilig = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        var statusGroesse = TextRenderer.MeasureText(grafik, status, AppTheme.Kennzeichnung, Size.Empty, einzeilig);
        int rechts = (int)karte.Right - LogicalToDeviceUnits(12);
        int mitte = (int)(karte.Y + karte.Height / 2);
        TextRenderer.DrawText(grafik, status, AppTheme.Kennzeichnung, new Point(rechts - statusGroesse.Width, mitte - statusGroesse.Height / 2), statusFarbe, einzeilig);
        if (drohne.IstLive)
        {
            int punkt = LogicalToDeviceUnits(9);
            using var punktPinsel = new SolidBrush(statusFarbe);
            grafik.FillEllipse(punktPinsel, rechts - statusGroesse.Width - LogicalToDeviceUnits(6) - punkt, mitte - punkt / 2f, punkt, punkt);
        }

        int textX = nummerFeld.Right + LogicalToDeviceUnits(12);
        int textBreite = rechts - statusGroesse.Width - LogicalToDeviceUnits(24) - textX;
        var nameGroesse = TextRenderer.MeasureText(grafik, drohne.Name, AppTheme.Name, Size.Empty, einzeilig);
        var formatGroesse = TextRenderer.MeasureText(grafik, drohne.Formatbeschreibung, AppTheme.Klein, Size.Empty, einzeilig);
        int textY = mitte - (nameGroesse.Height + formatGroesse.Height) / 2;
        TextRenderer.DrawText(grafik, drohne.Name, AppTheme.Name, new Rectangle(textX, textY, textBreite, nameGroesse.Height), schrift, einzeilig | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(grafik, drohne.Formatbeschreibung, AppTheme.Klein, new Rectangle(textX, textY + nameGroesse.Height, textBreite, formatGroesse.Height), AppTheme.Leise, einzeilig | TextFormatFlags.EndEllipsis);
    }
}

namespace DroneLive.App;

/// <summary>Schriftzug „Drohnen-Livestream“ wie auf der Testseite, mit farbigem zweiten Teil ohne Lücke.</summary>
internal sealed class WordmarkLabel : Control
{
    private const string Erster = "Drohnen-", Zweiter = "Livestream";
    private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    public WordmarkLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Font = AppTheme.Wortmarke;
        TabStop = false;
        AutoSize = true;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = Erster + Zweiter;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var erster = TextRenderer.MeasureText(Erster, Font, Size.Empty, Flags);
        var zweiter = TextRenderer.MeasureText(Zweiter, Font, Size.Empty, Flags);
        return new Size(erster.Width + zweiter.Width, Math.Max(erster.Height, zweiter.Height));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var erster = TextRenderer.MeasureText(e.Graphics, Erster, Font, Size.Empty, Flags);
        int y = (Height - erster.Height) / 2;
        TextRenderer.DrawText(e.Graphics, Erster, Font, new Point(0, y), AppTheme.Text, Flags);
        TextRenderer.DrawText(e.Graphics, Zweiter, Font, new Point(erster.Width, y), AppTheme.Akzent, Flags);
    }
}

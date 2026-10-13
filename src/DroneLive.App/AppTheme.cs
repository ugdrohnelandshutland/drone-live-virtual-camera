namespace DroneLive.App;

/// <summary>Farben und Schriften nach dem Design der Hub-Testseite (Tagmodus).</summary>
internal static class AppTheme
{
    public static readonly Color Grund = Color.FromArgb(0xE8, 0xEE, 0xF3);
    public static readonly Color Flaeche = Color.White;
    public static readonly Color Gesperrt = Color.FromArgb(0xF4, 0xF7, 0xF9);
    public static readonly Color Text = Color.FromArgb(0x0B, 0x1A, 0x28);
    public static readonly Color Leise = Color.FromArgb(0x3B, 0x50, 0x63);
    public static readonly Color Linie = Color.FromArgb(0xB9, 0xC8, 0xD4);
    public static readonly Color LinieStark = Color.FromArgb(0x6F, 0x85, 0x97);
    public static readonly Color SlotLeer = Color.FromArgb(0xC9, 0xD5, 0xDF);
    public static readonly Color Akzent = Color.FromArgb(0x0F, 0x6F, 0x68);
    public static readonly Color AkzentFlaeche = Color.FromArgb(0x1D, 0x6F, 0x68);
    public static readonly Color AkzentHover = Color.FromArgb(0x17, 0x84, 0x7B);
    public static readonly Color Live = Color.FromArgb(0x0B, 0x7A, 0x3E);
    public static readonly Color KeinSignal = Color.FromArgb(0xF2, 0xB0, 0x1E);
    public static readonly Color KeinSignalText = Color.FromArgb(0x8A, 0x5A, 0x00);
    public static readonly Color Fehler = Color.FromArgb(0xC4, 0x20, 0x2B);
    public static readonly Color Bildrand = Color.Black;

    private const string Familie = "Segoe UI";
    // Größen wie auf der Testseite in Pixeln bei 96 DPI; Windows skaliert Punktgrößen mit der Anzeige.
    public static readonly Font Wortmarke = Schrift(26, FontStyle.Bold);
    public static readonly Font Bedienung = new("Segoe UI Semibold", 17 * 0.75f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Name = Schrift(17, FontStyle.Bold);
    public static readonly Font Text15 = Schrift(15, FontStyle.Regular);
    public static readonly Font Klein = Schrift(13, FontStyle.Regular);
    public static readonly Font Kennzeichnung = Schrift(13, FontStyle.Bold);
    public static readonly Font Nummer = Schrift(21, FontStyle.Bold);
    public static readonly Font NummerGross = Schrift(26, FontStyle.Bold);

    private static Font Schrift(float pixel, FontStyle stil) => new(Familie, pixel * 0.75f, stil, GraphicsUnit.Point);
}

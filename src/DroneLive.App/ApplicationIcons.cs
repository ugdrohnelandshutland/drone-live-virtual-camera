using System.ComponentModel;
using System.Drawing.Drawing2D;
using DroneLive.Core;

namespace DroneLive.App;

internal enum Symbol { Start, StartHell, Stopp, Aktualisieren, Fenster, Beenden, Protokoll, Verbunden, KeinSignal, Fehler, Schloss }

/// <summary>Ein gemeinsames Anwendungssymbol und gut erkennbare Symbole für Bedienung und Status.</summary>
internal sealed class ApplicationIcons : Component
{
    private readonly Dictionary<Symbol, Bitmap> _bilder = [];
    private readonly Dictionary<string, Icon> _statusBilder = [];
    public Icon Anwendung { get; }
    public ApplicationIcons(IContainer container)
    {
        container.Add(this);
        using var quelle = typeof(ApplicationIcons).Assembly.GetManifestResourceStream("DroneLive.Application.ico")
            ?? throw new InvalidOperationException("Das Anwendungssymbol fehlt.");
        using var symbol = new Icon(quelle, 32, 32);
        Anwendung = (Icon)symbol.Clone();
    }

    /// <summary>Tray-Symbol mit Statuspunkt; ohne Punkt, solange keine Übertragung läuft.</summary>
    public Icon Tray(string zustand) => Statussymbol("Tray:" + zustand, zustand, SystemInformation.SmallIconSize);

    /// <summary>Fenster- und Taskleistensymbol mit demselben Statuspunkt wie im Tray; ohne Übertragung das Anwendungssymbol.</summary>
    public Icon Fenster(string zustand) => Punktfarbe(zustand) == null ? Anwendung : Statussymbol("Fenster:" + zustand, zustand, null);

    private Icon Statussymbol(string schluessel, string zustand, Size? groesse)
    {
        if (_statusBilder.TryGetValue(schluessel, out var vorhanden)) return vorhanden;
        // Alle Größen in einer Icon-Datei, damit Windows für Taskleiste, Titelleiste und Tray jeweils die passende wählt.
        using var datei = new MemoryStream(BrandGraphics.ErzeugeIco([16, 20, 24, 32, 40, 48, 64], Punktfarbe(zustand)));
        var icon = groesse is { } g ? new Icon(datei, g) : new Icon(datei);
        _statusBilder.Add(schluessel, icon);
        return icon;
    }

    // Kräftige Farben, damit der Punkt auf heller und dunkler Taskleiste erkennbar bleibt.
    private static Color? Punktfarbe(string zustand) => zustand switch
    {
        "Verbunden" => Color.FromArgb(0x2F, 0xC8, 0x73),
        "Kein Signal" => Color.FromArgb(0xFF, 0xC5, 0x3D),
        "Fehler" => Color.FromArgb(0xF0, 0x44, 0x4E),
        _ => null
    };
    public Bitmap this[Symbol symbol]
    {
        get
        {
            if (_bilder.TryGetValue(symbol, out var bild)) return bild;
            bild = new Bitmap(20, 20);
            using var grafik = Graphics.FromImage(bild);
            grafik.SmoothingMode = SmoothingMode.AntiAlias;
            using var stift = new Pen(Color.FromArgb(44, 80, 110), 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var gruen = new SolidBrush(Color.FromArgb(29, 143, 102));
            using var rot = new SolidBrush(Color.FromArgb(196, 62, 62));
            switch (symbol)
            {
                case Symbol.Start: grafik.FillPolygon(gruen, [new(5, 3), new(17, 10), new(5, 17)]); break;
                case Symbol.StartHell: grafik.FillPolygon(Brushes.White, [new(5, 3), new(17, 10), new(5, 17)]); break;
                case Symbol.Schloss:
                    using (var schloss = new SolidBrush(Color.FromArgb(59, 80, 99)))
                    using (var buegel = new Pen(Color.FromArgb(59, 80, 99), 2f))
                    {
                        grafik.DrawArc(buegel, 6, 3, 8, 9, 180, 180);
                        grafik.DrawLine(buegel, 6, 7.5f, 6, 10);
                        grafik.DrawLine(buegel, 14, 7.5f, 14, 10);
                        grafik.FillRectangle(schloss, 4, 9, 12, 9);
                    }
                    break;
                case Symbol.Stopp: grafik.FillRectangle(rot, 4, 4, 12, 12); break;
                case Symbol.Aktualisieren:
                    grafik.DrawArc(stift, 4, 4, 12, 12, 40, 285);
                    grafik.DrawLines(stift, [new(12, 3), new(17, 4), new(16, 9)]); break;
                case Symbol.Fenster:
                    grafik.DrawRectangle(stift, 3, 4, 14, 12); grafik.DrawLine(stift, 3, 8, 17, 8); break;
                case Symbol.Beenden:
                    grafik.DrawArc(stift, 4, 5, 12, 12, -50, 280); grafik.DrawLine(stift, 10, 2, 10, 10); break;
                case Symbol.Protokoll:
                    grafik.DrawRectangle(stift, 4, 2, 12, 16);
                    for (int y = 6; y <= 14; y += 4) grafik.DrawLine(stift, 7, y, 13, y); break;
                case Symbol.Verbunden:
                    grafik.FillEllipse(gruen, 2, 2, 16, 16);
                    using (var weiss = new Pen(Color.White, 2)) grafik.DrawLines(weiss, [new(6, 10), new(9, 13), new(14, 7)]);
                    break;
                case Symbol.KeinSignal:
                    using (var orange = new SolidBrush(Color.FromArgb(220, 150, 32))) grafik.FillEllipse(orange, 2, 2, 16, 16);
                    grafik.DrawLine(stift, 10, 5, 10, 10); grafik.DrawLine(stift, 10, 10, 13, 12); break;
                case Symbol.Fehler:
                    grafik.FillEllipse(rot, 2, 2, 16, 16);
                    using (var weiss = new Pen(Color.White, 2)) { grafik.DrawLine(weiss, 10, 5, 10, 11); grafik.DrawEllipse(weiss, 9.6f, 14, .8f, .8f); }
                    break;
            }
            _bilder.Add(symbol, bild);
            return bild;
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var bild in _bilder.Values) bild.Dispose();
            foreach (var icon in _statusBilder.Values) icon.Dispose();
            Anwendung.Dispose();
        }
        base.Dispose(disposing);
    }
}

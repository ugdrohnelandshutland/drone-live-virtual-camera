using System.Drawing.Imaging;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace DroneLive.Core;

/// <summary>Drei BGRA-Bildplätze trennen das veröffentlichte Bild vom laufenden Schreibvorgang.</summary>
public sealed unsafe class FrameExchange : IDisposable
{
    public const int Kopflaenge = 64;
    public const long Protokollversion = 2;
    public const int AnzahlBildplaetze = 3;
    public const int SignalfristMillisekunden = 2000;
    private readonly FileStream _datei;
    private readonly MemoryMappedFile _speicher;
    private readonly MemoryMappedViewAccessor _sicht;
    private readonly bool _schreiber;
    private long _sequenz;
    private long _veroeffentlichungsNummer;
    private int _naechsterPlatz;
    private byte* _anfang;
    private bool _beendet;
    private byte[]? _lesepuffer;
    private byte[]? _letztesBild;
    private long _letzteBildzeit;
    public int Breite { get; }
    public int Hoehe { get; }
    public int Bildlaenge => checked(Breite * Hoehe * 4);

    public FrameExchange(string pfad, int breite, int hoehe, bool schreiber)
    {
        if ((breite, hoehe) is not ((1280, 720) or (1920, 1080))) throw new ArgumentException("Ungültige Ausgabeauflösung.");
        Breite = breite;
        Hoehe = hoehe;
        _schreiber = schreiber;
        _datei = new FileStream(pfad, schreiber ? FileMode.CreateNew : FileMode.Open,
            schreiber ? FileAccess.ReadWrite : FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long dateilaenge = Kopflaenge + (long)AnzahlBildplaetze * Bildlaenge;
        if (schreiber) _datei.SetLength(dateilaenge);
        if (_datei.Length != dateilaenge) { _datei.Dispose(); throw new InvalidDataException("Die Bilddatei verwendet ein anderes Format. Bitte Anwendung und Kameraquelle gemeinsam aktualisieren."); }
        _speicher = MemoryMappedFile.CreateFromFile(_datei, null, 0,
            schreiber ? MemoryMappedFileAccess.ReadWrite : MemoryMappedFileAccess.Read, HandleInheritability.None, true);
        _sicht = _speicher.CreateViewAccessor(0, 0, schreiber ? MemoryMappedFileAccess.ReadWrite : MemoryMappedFileAccess.Read);
        _sicht.SafeMemoryMappedViewHandle.AcquirePointer(ref _anfang);
        _anfang += _sicht.PointerOffset;
        if (schreiber) SchreibeKopfwert(0, Protokollversion);
        else if (LeseKopfwert(0) != Protokollversion) { Dispose(); throw new InvalidDataException("Die Bildspeicher-Version passt nicht zur Kameraquelle. Bitte die Installation aktualisieren."); }
    }
    public void Schreibe(byte[] bgra)
    {
        if (!_schreiber || bgra.Length != Bildlaenge) throw new ArgumentException("Ungültiges BGRA-Bild.");
        ObjectDisposedException.ThrowIf(_beendet, this);
        int platz = _naechsterPlatz;
        _naechsterPlatz = (platz + 1) % AnzahlBildplaetze;
        int kopf = 16 + platz * 16;
        SchreibeKopfwert(kopf, ++_sequenz);
        Thread.MemoryBarrier();
        // Blockkopie statt mehrerer Millionen einzelner ReadArray-/WriteArray-Zugriffe.
        bgra.AsSpan().CopyTo(new Span<byte>(_anfang + Bildversatz(platz), Bildlaenge));
        SchreibeKopfwert(kopf + 8, Environment.TickCount64);
        Thread.MemoryBarrier();
        SchreibeKopfwert(kopf, ++_sequenz);
        // Erst das vollständig kopierte Bild freigeben; der nächste Schreibplatz ist ein anderer.
        SchreibeKopfwert(8, (++_veroeffentlichungsNummer << 2) | (uint)platz);
    }
    public void KeinSignal()
    {
        if (!_schreiber) return;
        ObjectDisposedException.ThrowIf(_beendet, this);
        SchreibeKopfwert(8, 0);
    }
    public bool Lese(byte[] bgra)
    {
        if (bgra.Length != Bildlaenge) throw new ArgumentException("Ungültiger Bildpuffer.");
        ObjectDisposedException.ThrowIf(_beendet, this);
        _lesepuffer ??= new byte[Bildlaenge];
        for (int versuch = 0; versuch < 3; versuch++)
        {
            long veroeffentlichung = LeseKopfwert(8);
            if (veroeffentlichung == 0) { _letzteBildzeit = 0; return false; }
            int platz = (int)(veroeffentlichung & 3);
            if (platz >= AnzahlBildplaetze) throw new InvalidDataException("Ungültiger veröffentlichter Bildplatz.");
            int kopf = 16 + platz * 16;
            long vorher = LeseKopfwert(kopf);
            if ((vorher & 1) != 0) continue;
            Thread.MemoryBarrier();
            long zeit = LeseKopfwert(kopf + 8);
            if (!IstFrisch(zeit))
            {
                Thread.MemoryBarrier();
                if (LeseKopfwert(kopf) != vorher || LeseKopfwert(8) != veroeffentlichung) continue;
                _letzteBildzeit = 0;
                return false;
            }
            // Erst nach erfolgreicher Sequenzprüfung veröffentlichen. Ein verworfener
            // Leseversuch darf das zuletzt gültige Bild nicht teilweise überschreiben.
            new ReadOnlySpan<byte>(_anfang + Bildversatz(platz), Bildlaenge).CopyTo(_lesepuffer);
            Thread.MemoryBarrier();
            if (LeseKopfwert(kopf) != vorher) continue;
            if (LeseKopfwert(8) == 0) { _letzteBildzeit = 0; return false; }
            byte[] vollstaendigesBild = _lesepuffer;
            _lesepuffer = _letztesBild ?? new byte[Bildlaenge];
            _letztesBild = vollstaendigesBild;
            _letzteBildzeit = zeit;
            vollstaendigesBild.CopyTo(bgra, 0);
            return true;
        }
        // Auch wenn der Sender den Leser dreimal überholt, keinen halben Frame ausgeben.
        if (LeseKopfwert(8) == 0) _letzteBildzeit = 0;
        if (_letztesBild == null || !IstFrisch(_letzteBildzeit)) return false;
        _letztesBild.CopyTo(bgra, 0);
        return true;
    }
    private long Bildversatz(int platz) => Kopflaenge + (long)platz * Bildlaenge;
    private long LeseKopfwert(int versatz) => Volatile.Read(ref *(long*)(_anfang + versatz));
    private void SchreibeKopfwert(int versatz, long wert) => Volatile.Write(ref *(long*)(_anfang + versatz), wert);
    private static bool IstFrisch(long zeit) => zeit != 0 && Environment.TickCount64 - zeit <= SignalfristMillisekunden;
    public void Dispose()
    {
        if (_beendet) return;
        _beendet = true;
        _sicht.SafeMemoryMappedViewHandle.ReleasePointer();
        _anfang = null;
        _sicht.Dispose(); _speicher.Dispose(); _datei.Dispose();
    }
}

public static class Bildwerkzeuge
{
    /// <summary>Ersatzbild im Stil der Hub-Testseite: schwarz mit Schrägstreifen, Hinweis in der Mitte und Wortmarke.</summary>
    public static byte[] KeinSignal(int breite, int hoehe)
    {
        using var bitmap = new Bitmap(breite, hoehe, PixelFormat.Format32bppArgb);
        using var grafik = Graphics.FromImage(bitmap);
        grafik.Clear(Color.Black);
        grafik.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        grafik.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        // Maße beziehen sich auf eine Breite von 960 Pixeln und werden auf die Ausgabegröße übertragen.
        float f = breite / 960f;
        using (var streifen = new Pen(Color.FromArgb(26, 255, 255, 255), 3 * f))
        {
            float abstand = 29 * f * MathF.Sqrt(2);
            for (float x = 0; x < breite + hoehe; x += abstand)
                grafik.DrawLine(streifen, x, 0, x - hoehe, hoehe);
        }
        using var titel = new Font("Segoe UI", 40 * f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var untertitel = new Font("Segoe UI", 19 * f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var marke = new Font("Segoe UI", 19 * f, FontStyle.Bold, GraphicsUnit.Pixel);
        const string titelText = "Kein Signal", untertitelText = "Das Drohnenbild erscheint automatisch.";
        var titelGroesse = grafik.MeasureString(titelText, titel);
        var untertitelGroesse = grafik.MeasureString(untertitelText, untertitel);
        float kastenBreite = Math.Max(titelGroesse.Width, untertitelGroesse.Width) + 80 * f;
        float kastenHoehe = titelGroesse.Height + untertitelGroesse.Height + 44 * f;
        var kasten = new RectangleF((breite - kastenBreite) / 2, (hoehe - kastenHoehe) / 2, kastenBreite, kastenHoehe);
        using (var pfad = BrandGraphics.RundesRechteck(kasten, 16 * f)) grafik.FillPath(Brushes.Black, pfad);
        using var hell = new SolidBrush(Color.FromArgb(0xF2, 0xF5, 0xF7));
        using var leise = new SolidBrush(Color.FromArgb(0xA9, 0xB6, 0xC1));
        grafik.DrawString(titelText, titel, hell, (breite - titelGroesse.Width) / 2, kasten.Y + 22 * f);
        grafik.DrawString(untertitelText, untertitel, leise, (breite - untertitelGroesse.Width) / 2, kasten.Y + 22 * f + titelGroesse.Height);
        float symbol = 36 * f;
        var symbolOrt = new RectangleF(28 * f, hoehe - 24 * f - symbol, symbol, symbol);
        BrandGraphics.ZeichneSymbol(grafik, symbolOrt);
        float textY = symbolOrt.Y + (symbol - grafik.MeasureString("D", marke).Height) / 2;
        float textX = symbolOrt.Right + 10 * f;
        grafik.DrawString("Drohnen-", marke, hell, textX, textY);
        using var akzent = new SolidBrush(BrandGraphics.Akzent);
        grafik.DrawString("Livestream", marke, akzent, textX + grafik.MeasureString("Drohnen-", marke, PointF.Empty, StringFormat.GenericTypographic).Width, textY);
        var daten = bitmap.LockBits(new Rectangle(0, 0, breite, hoehe), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[breite * hoehe * 4];
            for (int y = 0; y < hoehe; y++) Marshal.Copy(daten.Scan0 + y * daten.Stride, bytes, y * breite * 4, breite * 4);
            return bytes;
        }
        finally { bitmap.UnlockBits(daten); }
    }
    public static Bitmap AlsBitmap(byte[] bgra, int breite, int hoehe)
    {
        var bitmap = new Bitmap(breite, hoehe, PixelFormat.Format32bppArgb);
        var daten = bitmap.LockBits(new Rectangle(0, 0, breite, hoehe), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { for (int y = 0; y < hoehe; y++) Marshal.Copy(bgra, y * breite * 4, daten.Scan0 + y * daten.Stride, breite * 4); }
        finally { bitmap.UnlockBits(daten); }
        return bitmap;
    }
    public static byte[] NachNv12(byte[] bgra, int breite, int hoehe)
    {
        var nv12 = new byte[breite * hoehe * 3 / 2];
        for (int y = 0; y < hoehe; y++)
        for (int x = 0; x < breite; x++)
        {
            int p = (y * breite + x) * 4;
            int b = bgra[p], g = bgra[p + 1], r = bgra[p + 2];
            nv12[y * breite + x] = (byte)Math.Clamp(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16, 0, 255);
            if ((x & 1) != 0 || (y & 1) != 0) continue;
            int sumR = 0, sumG = 0, sumB = 0;
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                int q = ((y + dy) * breite + x + dx) * 4;
                sumB += bgra[q]; sumG += bgra[q + 1]; sumR += bgra[q + 2];
            }
            r = sumR / 4; g = sumG / 4; b = sumB / 4;
            int uv = breite * hoehe + (y / 2) * breite + x;
            nv12[uv] = (byte)Math.Clamp(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128, 0, 255);
            nv12[uv + 1] = (byte)Math.Clamp(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128, 0, 255);
        }
        return nv12;
    }
}

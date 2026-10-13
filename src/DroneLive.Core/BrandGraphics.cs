using System.Drawing.Drawing2D;

namespace DroneLive.Core;

/// <summary>Anwendungssymbol im Stil der Hub-Testseite: Schild mit Videokamera und Live-Punkt.</summary>
public static class BrandGraphics
{
    public static readonly Color Grund = Color.FromArgb(0x10, 0x23, 0x37);
    public static readonly Color Akzent = Color.FromArgb(0x58, 0xD5, 0xCC);
    public static readonly Color Hell = Color.FromArgb(0xE3, 0xED, 0xF7);

    /// <summary>Zeichnet das Symbol in ein Quadrat. Unter 28 Pixeln entfällt der Live-Punkt und die Kamera wird gefüllt.</summary>
    public static void ZeichneSymbol(Graphics grafik, RectangleF ziel)
    {
        var zustand = grafik.Save();
        try
        {
            grafik.SmoothingMode = SmoothingMode.AntiAlias;
            grafik.PixelOffsetMode = PixelOffsetMode.HighQuality;
            grafik.TranslateTransform(ziel.X, ziel.Y);
            grafik.ScaleTransform(ziel.Width / 256f, ziel.Height / 256f);
            float pixel = ziel.Width;
            bool klein = pixel < 28;
            using (var flaeche = RundesRechteck(new RectangleF(0, 0, 256, 256), klein ? 40 : 44))
            using (var grund = new SolidBrush(Grund))
                grafik.FillPath(grund, flaeche);
            using var hell = new SolidBrush(Hell);
            if (klein)
            {
                using var schild = Schild(new PointF(128, 30), 206, 58, 122, 228, 170, 202, 50);
                using var schildStift = new Pen(Akzent, 28) { LineJoin = LineJoin.Round };
                grafik.DrawPath(schildStift, schild);
                using (var koerper = RundesRechteck(new RectangleF(76, 94, 76, 60), 12)) grafik.FillPath(hell, koerper);
                grafik.FillPolygon(hell, [new PointF(156, 110), new PointF(186, 94), new PointF(186, 154), new PointF(156, 138)]);
                return;
            }
            // Kleinere Darstellungen erhalten kräftigere Linien, damit sie nicht verschwimmen.
            float schildBreite = pixel >= 96 ? 13 : pixel >= 40 ? 15 : 18;
            float kameraBreite = pixel >= 96 ? 10 : pixel >= 40 ? 12 : 15;
            float objektivBreite = pixel >= 96 ? 6 : pixel >= 40 ? 8 : 10;
            float punkt = pixel >= 96 ? 10 : pixel >= 40 ? 11 : 12;
            using (var schild = Schild(new PointF(128, 37), 199, 62, 122, 220, 166, 196, 57))
            using (var schildStift = new Pen(Akzent, schildBreite) { LineJoin = LineJoin.Round })
                grafik.DrawPath(schildStift, schild);
            using (var koerper = RundesRechteck(new RectangleF(80, 98, 70, 52), 11))
            using (var kameraStift = new Pen(Hell, kameraBreite))
                grafik.DrawPath(kameraStift, koerper);
            using (var objektiv = new GraphicsPath())
            using (var objektivStift = new Pen(Hell, objektivBreite) { LineJoin = LineJoin.Round })
            {
                objektiv.AddPolygon([new PointF(156, 114), new PointF(178, 102), new PointF(178, 146), new PointF(156, 134)]);
                grafik.FillPath(hell, objektiv);
                grafik.DrawPath(objektivStift, objektiv);
            }
            using var akzent = new SolidBrush(Akzent);
            grafik.FillEllipse(akzent, 105 - punkt, 124 - punkt, 2 * punkt, 2 * punkt);
        }
        finally { grafik.Restore(zustand); }
    }

    /// <summary>Statuspunkt unten rechts für das Tray-Symbol, mit Rand in der Farbe der Taskleiste.</summary>
    public static void ZeichneStatuspunkt(Graphics grafik, RectangleF ziel, Color farbe)
    {
        var zustand = grafik.Save();
        try
        {
            grafik.SmoothingMode = SmoothingMode.AntiAlias;
            float faktor = ziel.Width / 256f;
            float radius = 62 * faktor;
            using var pinsel = new SolidBrush(farbe);
            using var rand = new Pen(Grund, 14 * faktor);
            var mitte = new PointF(ziel.X + 198 * faktor, ziel.Y + 198 * faktor);
            grafik.FillEllipse(pinsel, mitte.X - radius, mitte.Y - radius, 2 * radius, 2 * radius);
            grafik.DrawEllipse(rand, mitte.X - radius, mitte.Y - radius, 2 * radius, 2 * radius);
        }
        finally { grafik.Restore(zustand); }
    }

    public static Bitmap ErzeugeSymbol(int groesse)
    {
        var bild = new Bitmap(groesse, groesse, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var grafik = Graphics.FromImage(bild);
        grafik.Clear(Color.Transparent);
        ZeichneSymbol(grafik, new RectangleF(0, 0, groesse, groesse));
        return bild;
    }

    /// <summary>Icon-Datei mit mehreren Größen (PNG-Einträge), optional mit Statuspunkt; Windows wählt je Verwendung die passende Größe.</summary>
    public static byte[] ErzeugeIco(IReadOnlyList<int> groessen, Color? statuspunkt = null)
    {
        var bilder = new List<byte[]>();
        foreach (int groesse in groessen)
        {
            using var bild = ErzeugeSymbol(groesse);
            if (statuspunkt is { } farbe)
                using (var grafik = Graphics.FromImage(bild)) ZeichneStatuspunkt(grafik, new RectangleF(0, 0, groesse, groesse), farbe);
            using var png = new MemoryStream();
            bild.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            bilder.Add(png.ToArray());
        }
        using var datei = new MemoryStream();
        using (var schreiber = new BinaryWriter(datei, System.Text.Encoding.UTF8, true))
        {
            schreiber.Write((ushort)0);
            schreiber.Write((ushort)1);
            schreiber.Write((ushort)bilder.Count);
            int versatz = 6 + 16 * bilder.Count;
            for (int i = 0; i < bilder.Count; i++)
            {
                byte kante = (byte)(groessen[i] >= 256 ? 0 : groessen[i]);
                schreiber.Write(kante);
                schreiber.Write(kante);
                schreiber.Write((byte)0);
                schreiber.Write((byte)0);
                schreiber.Write((ushort)1);
                schreiber.Write((ushort)32);
                schreiber.Write(bilder[i].Length);
                schreiber.Write(versatz);
                versatz += bilder[i].Length;
            }
            foreach (var bild in bilder) schreiber.Write(bild);
        }
        return datei.ToArray();
    }

    public static GraphicsPath RundesRechteck(RectangleF r, float radius)
    {
        float d = Math.Min(2 * radius, Math.Min(r.Width, r.Height));
        var pfad = new GraphicsPath();
        if (d <= 0) { pfad.AddRectangle(r); return pfad; }
        pfad.AddArc(r.X, r.Y, d, d, 180, 90);
        pfad.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        pfad.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        pfad.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        pfad.CloseFigure();
        return pfad;
    }

    private static GraphicsPath Schild(PointF spitze, float rechts, float schulter, float mitte, float unten, float bogenY, float bogenUnten, float links)
    {
        var pfad = new GraphicsPath();
        pfad.AddLine(spitze.X, spitze.Y, rechts, schulter);
        pfad.AddLine(rechts, schulter, rechts, mitte);
        pfad.AddBezier(rechts, mitte, rechts, bogenY, spitze.X + (rechts - spitze.X) * 0.58f, bogenUnten, spitze.X, unten);
        pfad.AddBezier(spitze.X, unten, spitze.X - (spitze.X - links) * 0.58f, bogenUnten, links, bogenY, links, mitte);
        pfad.AddLine(links, mitte, links, schulter);
        pfad.CloseFigure();
        return pfad;
    }
}

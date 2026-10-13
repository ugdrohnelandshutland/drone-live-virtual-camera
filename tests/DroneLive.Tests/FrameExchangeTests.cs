using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using DirectN;
using DroneLive.Core;
using VCamNetSampleSource;

internal static class FrameExchangeTests
{
    public static async Task PruefeAsync(Action<bool, string> pruefe)
    {
        string ordner = Path.GetFullPath("artifacts/test-results");
        Directory.CreateDirectory(ordner);
        foreach (var (breite, hoehe) in new[] { (1280, 720), (1920, 1080) })
        {
            string pfad = Path.Combine(ordner, Guid.NewGuid() + ".bgra");
            try
            {
                using var schreiber = new FrameExchange(pfad, breite, hoehe, true);
                using var generator = new FrameGenerator(pfad, breite, hoehe);
                using var sample = MFFunctions.MFCreateSample();
                using var kontrolldatei = new FileStream(pfad, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                using var kontrollspeicher = MemoryMappedFile.CreateFromFile(kontrolldatei, null, 0, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, true);
                using var kontrollsicht = kontrollspeicher.CreateViewAccessor();
                byte[] erstes = Farbbild(breite, hoehe, 16, 32, 128);
                byte[] zweites = Farbbild(breite, hoehe, 128, 64, 8);
                byte[] erstesNv12 = Bildwerkzeuge.NachNv12(erstes, breite, hoehe);
                byte[] zweitesNv12 = Bildwerkzeuge.NachNv12(zweites, breite, hoehe);
                byte[] keinSignal = Bildwerkzeuge.KeinSignal(breite, hoehe);
                byte[] keinSignalNv12 = Bildwerkzeuge.NachNv12(keinSignal, breite, hoehe);
                var rgb = new byte[erstes.Length];
                var nv12 = new byte[erstesNv12.Length];
                bool IstSample(Guid format, byte[] erwartet, byte[] ziel)
                {
                    generator.Generate(sample, format);
                    LeseSample(sample, ziel);
                    return ziel.AsSpan().SequenceEqual(erwartet);
                }
                schreiber.Schreibe(erstes);
                pruefe(IstSample(MFConstants.MFVideoFormat_RGB32, erstes, rgb), $"Kamera übernimmt vollständiges Ausgangsbild: {breite} × {hoehe}");

                // Einen angefangenen Schreibvorgang samt teilweise überschriebenem Bild erzwingen.
                // Der gleiche Zustand entsteht zwischen Sequenzbeginn und -ende des echten Senders.
                int platz = (int)(kontrollsicht.ReadInt64(8) & 3);
                int bildkopf = 16 + platz * 16;
                kontrollsicht.Write(bildkopf, kontrollsicht.ReadInt64(bildkopf) + 1);
                Thread.MemoryBarrier();
                kontrollsicht.WriteArray(FrameExchange.Kopflaenge + (long)platz * zweites.Length, zweites, 0, zweites.Length / 2);
                bool stabil = true;
                for (int i = 0; i < 6; i++)
                {
                    stabil &= IstSample(MFConstants.MFVideoFormat_RGB32, erstes, rgb);
                    stabil &= IstSample(MFConstants.MFVideoFormat_NV12, erstesNv12, nv12);
                }
                pruefe(stabil, $"Gleichzeitiges Schreiben liefert das letzte vollständige Bild, ohne Kein-Signal-Flackern oder Bildmischung: {breite} × {hoehe}");

                await Task.Delay(2100);
                pruefe(IstSample(MFConstants.MFVideoFormat_RGB32, keinSignal, rgb) && IstSample(MFConstants.MFVideoFormat_NV12, keinSignalNv12, nv12),
                    $"Auch ein beim Schreiben ausgefallener Sender liefert nach zwei Sekunden Kein Signal: {breite} × {hoehe}");
                schreiber.Schreibe(zweites);
                pruefe(IstSample(MFConstants.MFVideoFormat_RGB32, zweites, rgb) && IstSample(MFConstants.MFVideoFormat_NV12, zweitesNv12, nv12),
                    $"Neues vollständiges Bild ersetzt den Zwischenstand sofort: {breite} × {hoehe}");
                schreiber.KeinSignal();
                pruefe(IstSample(MFConstants.MFVideoFormat_RGB32, keinSignal, rgb) && IstSample(MFConstants.MFVideoFormat_NV12, keinSignalNv12, nv12),
                    $"Expliziter Signalverlust leert auch das zuletzt gültige Kamerabild sofort: {breite} × {hoehe}");

                schreiber.Schreibe(erstes);
                generator.Generate(sample, MFConstants.MFVideoFormat_RGB32);
                using var abbruch = new CancellationTokenSource();
                var schreibt = Task.Run(async () =>
                {
                    int nummer = 0;
                    try
                    {
                        while (!abbruch.IsCancellationRequested)
                        {
                            schreiber.Schreibe((nummer++ & 1) == 0 ? zweites : erstes);
                            await Task.Delay(8, abbruch.Token);
                        }
                    }
                    catch (OperationCanceledException) when (abbruch.IsCancellationRequested) { }
                });
                int samples = 0, falscheBilder = 0;
                try
                {
                    var uhr = Stopwatch.StartNew();
                    while (uhr.Elapsed < TimeSpan.FromSeconds(2))
                    {
                        bool istNv12 = (samples++ & 1) != 0;
                        generator.Generate(sample, istNv12 ? MFConstants.MFVideoFormat_NV12 : MFConstants.MFVideoFormat_RGB32);
                        byte[] ziel = istNv12 ? nv12 : rgb;
                        LeseSample(sample, ziel);
                        if (!ziel.AsSpan().SequenceEqual(istNv12 ? erstesNv12 : erstes) && !ziel.AsSpan().SequenceEqual(istNv12 ? zweitesNv12 : zweites)) falscheBilder++;
                        await Task.Delay(1);
                    }
                }
                finally { await abbruch.CancelAsync(); await schreibt; }
                pruefe(samples >= 20 && falscheBilder == 0,
                    $"Paralleler Sender und RGB32/NV12-Kamera: {samples} Samples, {falscheBilder} falsche Bilder bei {breite} × {hoehe}");
            }
            finally { File.Delete(pfad); }
        }
    }

    private static byte[] Farbbild(int breite, int hoehe, byte b, byte g, byte r)
    {
        var bild = new byte[breite * hoehe * 4];
        for (int p = 0; p < bild.Length; p += 4) { bild[p] = b; bild[p + 1] = g; bild[p + 2] = r; bild[p + 3] = 255; }
        return bild;
    }

    private static void LeseSample(IComObject<IMFSample> sample, byte[] ziel)
    {
        using var puffer = sample.GetBufferByIndex(0);
        if (puffer.GetCurrentLength() != ziel.Length) throw new InvalidDataException("Kamera-Sample hat eine falsche Bildlänge.");
        puffer.WithLock((zeiger, _, _) => Marshal.Copy(zeiger, ziel, 0, ziel.Length));
    }
}

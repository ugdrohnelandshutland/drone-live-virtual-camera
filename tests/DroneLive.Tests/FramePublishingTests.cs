using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using DirectN;
using DroneLive.Core;
using VCamNetSampleSource;

internal static class FramePublishingTests
{
    public static async Task PruefeAsync(Action<bool, string> pruefe)
    {
        string ordner = Path.GetFullPath("artifacts/test-results");
        Directory.CreateDirectory(ordner);
        foreach (var (breite, hoehe) in new[] { (1280, 720), (1920, 1080) })
            await PruefeUnfertigenNachfolgerAsync(ordner, breite, hoehe, pruefe);

        // Beide Auflösungen und beide Eingangsraten gemeinsam prüfen, damit die
        // Kopiervorgänge auch unter gleichzeitiger Dekodier-/Kamera-Last stattfinden.
        var aufgaben = from aufloesung in new[] { (1280, 720), (1920, 1080) }
                       from senderate in new[] { 30, 60 }
                       select PruefeDauerbetriebAsync(ordner, aufloesung.Item1, aufloesung.Item2, senderate);
        foreach (var ergebnisse in await Task.WhenAll(aufgaben))
            foreach (var (erfolg, beschreibung) in ergebnisse) pruefe(erfolg, beschreibung);
    }

    private static async Task PruefeUnfertigenNachfolgerAsync(string ordner, int breite, int hoehe, Action<bool, string> pruefe)
    {
        string pfad = Path.Combine(ordner, Guid.NewGuid() + ".bgra");
        try
        {
            using var schreiber = new FrameExchange(pfad, breite, hoehe, true);
            using var leser = new FrameExchange(pfad, breite, hoehe, false);
            using var generator = new FrameGenerator(pfad, breite, hoehe);
            using var sample = MFFunctions.MFCreateSample();
            using var datei = new FileStream(pfad, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            using var speicher = MemoryMappedFile.CreateFromFile(datei, null, 0, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, true);
            using var sicht = speicher.CreateViewAccessor();
            int bildlaenge = breite * hoehe * 4;
            pruefe(sicht.ReadInt64(0) == FrameExchange.Protokollversion && datei.Length == FrameExchange.Kopflaenge + (long)FrameExchange.AnzahlBildplaetze * bildlaenge,
                $"Bildprotokoll besitzt drei getrennte Bildplätze: {breite} × {hoehe}");
            byte[] erstes = Farbbild(breite, hoehe, 16, 32, 128);
            byte[] zweites = Farbbild(breite, hoehe, 128, 64, 8);
            byte[] erstesNv12 = Bildwerkzeuge.NachNv12(erstes, breite, hoehe);
            byte[] zweitesNv12 = Bildwerkzeuge.NachNv12(zweites, breite, hoehe);
            var rgb = new byte[bildlaenge];
            var nv12 = new byte[erstesNv12.Length];
            schreiber.Schreibe(erstes);
            bool bestehenderLeserStabil = leser.Lese(rgb) && rgb.AsSpan().SequenceEqual(erstes);
            bool neuerLeserStabil = true;
            bool kameraStabil = true;
            long unfertigeMetadaten = -1;
            long urspruenglicheSequenz = 0;
            int anzahl = 0;
            var uhr = Stopwatch.StartNew();
            while (uhr.Elapsed < TimeSpan.FromSeconds(3.2))
            {
                // Die künstliche Unterbrechung der vorigen Runde vor dem nächsten
                // echten Schreibvorgang aufheben. Danach beginnt wieder ein neuer
                // unvollständiger Nachfolger, der niemals veröffentlicht wird.
                if (unfertigeMetadaten >= 0) sicht.Write(unfertigeMetadaten, urspruenglicheSequenz);
                byte[] erwartet = (anzahl & 1) == 0 ? zweites : erstes;
                byte[] erwartetNv12 = (anzahl & 1) == 0 ? zweitesNv12 : erstesNv12;
                byte[] nachfolger = (anzahl & 1) == 0 ? erstes : zweites;
                schreiber.Schreibe(erwartet);
                long veroeffentlichung = sicht.ReadInt64(8);
                int nachfolgerPlatz = ((int)(veroeffentlichung & 3) + 1) % FrameExchange.AnzahlBildplaetze;
                unfertigeMetadaten = 16 + nachfolgerPlatz * 16;
                urspruenglicheSequenz = sicht.ReadInt64(unfertigeMetadaten);
                sicht.Write(unfertigeMetadaten, (urspruenglicheSequenz & ~1L) + 1);
                Thread.MemoryBarrier();
                sicht.WriteArray(FrameExchange.Kopflaenge + (long)nachfolgerPlatz * bildlaenge, nachfolger, 0, bildlaenge / 2);
                Thread.MemoryBarrier();

                bestehenderLeserStabil &= leser.Lese(rgb) && rgb.AsSpan().SequenceEqual(erwartet);
                if (anzahl % 5 == 0)
                {
                    // Die Kamera kann gerade während eines Schreibvorgangs neu
                    // aktiviert werden und hat dann noch keinen lokalen Bildcache.
                    using var neuerLeser = new FrameExchange(pfad, breite, hoehe, false);
                    neuerLeserStabil &= neuerLeser.Lese(rgb) && rgb.AsSpan().SequenceEqual(erwartet);
                }
                generator.Generate(sample, MFConstants.MFVideoFormat_RGB32);
                LeseSample(sample, rgb);
                kameraStabil &= rgb.AsSpan().SequenceEqual(erwartet);
                generator.Generate(sample, MFConstants.MFVideoFormat_NV12);
                LeseSample(sample, nv12);
                kameraStabil &= nv12.AsSpan().SequenceEqual(erwartetNv12);
                anzahl++;
                await Task.Delay(15);
            }
            pruefe(anzahl >= 10 && bestehenderLeserStabil,
                $"Fortlaufende Publikationen über mehr als zwei Sekunden bleiben trotz unfertiger Nachfolger frisch: {breite} × {hoehe}, {anzahl} Publikationen");
            pruefe(neuerLeserStabil,
                $"Neu gestarteter Leser übernimmt sofort das veröffentlichte Bild während der Nachfolger geschrieben wird: {breite} × {hoehe}");
            pruefe(kameraStabil,
                $"RGB32/NV12-Kamera zeigt während unfertiger Nachfolger ausschließlich vollständige aktuelle Bilder: {breite} × {hoehe}");

            // Jetzt tatsächlich alle Publikationen beenden, während der angefangene
            // Nachfolger liegen bleibt. Das alte Bild darf nicht ewig frisch bleiben.
            await Task.Delay(2100);
            byte[] keinSignal = Bildwerkzeuge.KeinSignal(breite, hoehe);
            byte[] keinSignalNv12 = Bildwerkzeuge.NachNv12(keinSignal, breite, hoehe);
            bool signalVerloren = !leser.Lese(rgb);
            using (var neuerLeser = new FrameExchange(pfad, breite, hoehe, false)) signalVerloren &= !neuerLeser.Lese(rgb);
            generator.Generate(sample, MFConstants.MFVideoFormat_RGB32);
            LeseSample(sample, rgb);
            signalVerloren &= rgb.AsSpan().SequenceEqual(keinSignal);
            generator.Generate(sample, MFConstants.MFVideoFormat_NV12);
            LeseSample(sample, nv12);
            signalVerloren &= nv12.AsSpan().SequenceEqual(keinSignalNv12);
            pruefe(signalVerloren,
                $"Ausgefallener Sender mit unfertigem Nachfolger liefert nach zwei Sekunden zuverlässig Kein Signal: {breite} × {hoehe}");
        }
        finally { File.Delete(pfad); }
    }

    private static async Task<List<(bool Erfolg, string Beschreibung)>> PruefeDauerbetriebAsync(string ordner, int breite, int hoehe, int senderate)
    {
        string pfad = Path.Combine(ordner, Guid.NewGuid() + ".bgra");
        try
        {
            using var schreiber = new FrameExchange(pfad, breite, hoehe, true);
            byte[] erstes = Farbbild(breite, hoehe, 24, 48, 160);
            byte[] zweites = Farbbild(breite, hoehe, 160, 96, 16);
            byte[] erstesNv12 = Bildwerkzeuge.NachNv12(erstes, breite, hoehe);
            byte[] zweitesNv12 = Bildwerkzeuge.NachNv12(zweites, breite, hoehe);
            byte[] keinSignal = Bildwerkzeuge.KeinSignal(breite, hoehe);
            byte[] keinSignalNv12 = Bildwerkzeuge.NachNv12(keinSignal, breite, hoehe);
            using var abbruch = new CancellationTokenSource();
            var erstesVeroeffentlicht = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int geschrieben = 0;
            var schreibt = Task.Run(async () =>
            {
                try
                {
                    do
                    {
                        // Mehrere gleiche Bilder verhindern eine reine 30/60-Hz-
                        // Phasenaliasierung beim Prüfen tatsächlicher Bildwechsel.
                        schreiber.Schreibe(((geschrieben / 7) & 1) == 0 ? erstes : zweites);
                        Interlocked.Increment(ref geschrieben);
                        erstesVeroeffentlicht.TrySetResult();
                        await Task.Delay(senderate == 60 ? 16 : 33, abbruch.Token);
                    } while (!abbruch.IsCancellationRequested);
                }
                catch (OperationCanceledException) when (abbruch.IsCancellationRequested) { }
                catch (Exception ex) { erstesVeroeffentlicht.TrySetException(ex); throw; }
            });
            int samples = 0, ohneSignal = 0, gemischteBilder = 0, bildwechsel = 0;
            int letzterBildtyp = -1;
            try
            {
                await erstesVeroeffentlicht.Task;
                // Den Leser erst aktivieren, nachdem der Sender bereits läuft.
                await Task.Delay(23);
                using var generator = new FrameGenerator(pfad, breite, hoehe);
                using var sample = MFFunctions.MFCreateSample();
                var rgb = new byte[erstes.Length];
                var nv12 = new byte[erstesNv12.Length];
                var uhr = Stopwatch.StartNew();
                do
                {
                    bool istNv12 = (samples & 1) != 0;
                    generator.Generate(sample, istNv12 ? MFConstants.MFVideoFormat_NV12 : MFConstants.MFVideoFormat_RGB32);
                    byte[] ziel = istNv12 ? nv12 : rgb;
                    LeseSample(sample, ziel);
                    int bildtyp = ziel.AsSpan().SequenceEqual(istNv12 ? erstesNv12 : erstes) ? 0
                        : ziel.AsSpan().SequenceEqual(istNv12 ? zweitesNv12 : zweites) ? 1 : -1;
                    if (bildtyp < 0)
                    {
                        if (ziel.AsSpan().SequenceEqual(istNv12 ? keinSignalNv12 : keinSignal)) ohneSignal++;
                        else gemischteBilder++;
                    }
                    else
                    {
                        if (letzterBildtyp >= 0 && letzterBildtyp != bildtyp) bildwechsel++;
                        letzterBildtyp = bildtyp;
                    }
                    samples++;
                    await Task.Delay(33);
                } while (uhr.Elapsed < TimeSpan.FromSeconds(10));
            }
            finally { await abbruch.CancelAsync(); await schreibt; }
            string beschreibung = $"{breite} × {hoehe}, Sender {senderate} Hz, Kamera 30 Hz, {geschrieben} Publikationen/{samples} Samples";
            return
            [
                (samples >= 60 && geschrieben >= 100 && ohneSignal == 0, $"Zehn Sekunden laufender Stream ohne falsches Kein Signal: {beschreibung}, {ohneSignal} Kein-Signal-Samples"),
                (gemischteBilder == 0, $"Keine teilweise überschriebenen RGB32/NV12-Bilder im Dauerbetrieb: {beschreibung}, {gemischteBilder} Mischbilder"),
                (bildwechsel >= 5, $"Dauerbetrieb liefert fortlaufend neue Bilder statt eines dauerhaften alten Caches: {beschreibung}, {bildwechsel} Bildwechsel")
            ];
        }
        finally { File.Delete(pfad); }
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

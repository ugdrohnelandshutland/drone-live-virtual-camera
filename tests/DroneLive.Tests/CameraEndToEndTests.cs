using System.Diagnostics;
using System.Runtime.InteropServices;
using DirectN;
using DroneLive.Core;
using DroneLive.App;

internal static class CameraEndToEndTests
{
    public static async Task PruefeAsync(Action<bool, string> pruefe)
    {
        using var zeitlimit = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await PruefeKameraAsync(pruefe, zeitlimit.Token);
    }

    private static async Task PruefeKameraAsync(Action<bool, string> pruefe, CancellationToken abbrechen)
    {
        const int breite = 1280, hoehe = 720;
        byte[] erstes = Farbbild(breite, hoehe, 16, 32, 128);
        byte[] zweites = Farbbild(breite, hoehe, 128, 64, 8);
        using var kamera = new VirtualCameraOutput(breite, hoehe);
        kamera.Schreibe(erstes);
        using var senderAbbruch = CancellationTokenSource.CreateLinkedTokenSource(abbrechen);
        var sender = Task.Run(async () =>
        {
            int nummer = 0;
            try
            {
                while (!senderAbbruch.IsCancellationRequested)
                {
                    kamera.Schreibe((nummer++ & 1) == 0 ? zweites : erstes);
                    await Task.Delay(33, senderAbbruch.Token);
                }
            }
            catch (OperationCanceledException) when (senderAbbruch.IsCancellationRequested) { }
        });

        IComObject<IMFActivate>? aktivierung = null;
        IComObject<IMFMediaSource>? quelle = null;
        IComObject<IMFSourceReader>? leser = null;
        Task? sampleAufgabe = null;
        CancellationTokenSource? wechselAbbruch = null;
        Task? wechselSender = null;
        try
        {
            Console.WriteLine("Ende-zu-Ende-Kameratest: registriertes Kameragerät suchen.");
            var wartezeit = Stopwatch.StartNew();
            while (aktivierung == null)
            {
                abbrechen.ThrowIfCancellationRequested();
                aktivierung = FindeKamera();
                if (aktivierung == null)
                {
                    if (wartezeit.Elapsed > TimeSpan.FromSeconds(10))
                        throw new TimeoutException("Die registrierte Drone-Live-Kamera erschien nicht in der Windows-Geräteliste.");
                    await Task.Delay(100, abbrechen);
                }
            }
            pruefe(true, "Registrierte Drone-Live-Kamera erscheint in MFEnumDeviceSources");

            aktivierung.Object.ActivateObject(typeof(IMFMediaSource).GUID, out var aktiviert).ThrowOnError();
            quelle = new ComObject<IMFMediaSource>((IMFMediaSource)aktiviert);
            using var leseAttribute = MFFunctions.MFCreateAttributes(1);
            leseAttribute.Object.SetUINT32(MFConstants.MF_SOURCE_READER_DISCONNECT_MEDIASOURCE_ON_SHUTDOWN, 1).ThrowOnError();
            Functions.MFCreateSourceReaderFromMediaSource(quelle.Object, leseAttribute.Object, out var sourceReader).ThrowOnError();
            leser = new ComObject<IMFSourceReader>(sourceReader);
            WaehleRohformat(sourceReader);
            sourceReader.GetCurrentMediaType(0, out var medientyp).ThrowOnError();
            Guid format;
            using (var typ = new ComObject<IMFMediaType>(medientyp))
            {
                medientyp.GetGUID(MFConstants.MF_MT_SUBTYPE, out format).ThrowOnError();
                medientyp.GetUINT64(MFConstants.MF_MT_FRAME_SIZE, out ulong groesse).ThrowOnError();
                pruefe(groesse == ((ulong)breite << 32 | hoehe), "Windows Frame Server meldet die feste Kameraauflösung 1280 × 720");
            }
            pruefe(format == MFConstants.MFVideoFormat_NV12 || format == MFConstants.MFVideoFormat_RGB32,
                "Windows Frame Server liefert unkomprimiertes NV12 oder RGB32");

            byte[] erwartetErstes = format == MFConstants.MFVideoFormat_NV12 ? Bildwerkzeuge.NachNv12(erstes, breite, hoehe) : erstes;
            byte[] erwartetZweites = format == MFConstants.MFVideoFormat_NV12 ? Bildwerkzeuge.NachNv12(zweites, breite, hoehe) : zweites;
            byte[] keinSignal = Bildwerkzeuge.KeinSignal(breite, hoehe);
            if (format == MFConstants.MFVideoFormat_NV12) keinSignal = Bildwerkzeuge.NachNv12(keinSignal, breite, hoehe);
            Console.WriteLine("Ende-zu-Ende-Kameratest: zehn Sekunden echte Frame-Server-Samples prüfen.");
            sampleAufgabe = Task.Run(() =>
            {
                int samples = 0, falscheBilder = 0;
                var uhr = Stopwatch.StartNew();
                bool signalAngekommen = false;
                while (!signalAngekommen && uhr.Elapsed < TimeSpan.FromSeconds(5))
                {
                    abbrechen.ThrowIfCancellationRequested();
                    byte[]? bild = LeseSample(sourceReader);
                    signalAngekommen = bild != null && (GleichesBild(bild, erwartetErstes, format) || GleichesBild(bild, erwartetZweites, format));
                }
                pruefe(signalAngekommen, "Registrierte COM-Kamera übernimmt ein tatsächlich eingespeistes Rohbild");

                uhr.Restart();
                while (uhr.Elapsed < TimeSpan.FromSeconds(10))
                {
                    abbrechen.ThrowIfCancellationRequested();
                    byte[]? bild = LeseSample(sourceReader);
                    if (bild == null) continue;
                    samples++;
                    if (!GleichesBild(bild, erwartetErstes, format) && !GleichesBild(bild, erwartetZweites, format)) falscheBilder++;
                }
                pruefe(samples >= 100 && falscheBilder == 0,
                    $"Registrierte Kamera und Windows Frame Server: {samples} Samples, {falscheBilder} unerwartete/Kein-Signal-Bilder in zehn Sekunden");
            }, abbrechen);
            await sampleAufgabe.WaitAsync(abbrechen);
            await senderAbbruch.CancelAsync();
            await sender;
            kamera.KeinSignal();

            Console.WriteLine("Ende-zu-Ende-Kameratest: Signalverlust im echten Frame Server prüfen.");
            sampleAufgabe = Task.Run(() =>
            {
                bool erkannt = false;
                var uhr = Stopwatch.StartNew();
                while (!erkannt && uhr.Elapsed < TimeSpan.FromSeconds(5))
                {
                    abbrechen.ThrowIfCancellationRequested();
                    byte[]? bild = LeseSample(sourceReader);
                    erkannt = bild != null && GleichesBild(bild, keinSignal, format);
                }
                pruefe(erkannt, "Registrierte Kamera liefert bei echtem Signalverlust das Kein-Signal-Bild");
            }, abbrechen);
            await sampleAufgabe.WaitAsync(abbrechen);

            // Drohnenwechsel: Die Kamera bleibt registriert; der geöffnete Leser (wie Teams) erhält das neue Bild ohne Neuauswahl.
            Console.WriteLine("Ende-zu-Ende-Kameratest: neues Signal nach Kein Signal in derselben Kamera prüfen.");
            byte[] drittes = Farbbild(breite, hoehe, 40, 200, 90);
            byte[] erwartetDrittes = format == MFConstants.MFVideoFormat_NV12 ? Bildwerkzeuge.NachNv12(drittes, breite, hoehe) : drittes;
            wechselAbbruch = CancellationTokenSource.CreateLinkedTokenSource(abbrechen);
            var wechselToken = wechselAbbruch.Token;
            wechselSender = Task.Run(async () =>
            {
                try
                {
                    while (!wechselToken.IsCancellationRequested)
                    {
                        kamera.Schreibe(drittes);
                        await Task.Delay(33, wechselToken);
                    }
                }
                catch (OperationCanceledException) when (wechselToken.IsCancellationRequested) { }
            });
            sampleAufgabe = Task.Run(() =>
            {
                bool erkannt = false;
                var uhr = Stopwatch.StartNew();
                while (!erkannt && uhr.Elapsed < TimeSpan.FromSeconds(5))
                {
                    abbrechen.ThrowIfCancellationRequested();
                    byte[]? bild = LeseSample(sourceReader);
                    erkannt = bild != null && GleichesBild(bild, erwartetDrittes, format);
                }
                pruefe(erkannt, "Nach Kein Signal liefert dieselbe geöffnete Kamera das Bild der nächsten Übertragung");
            }, abbrechen);
            await sampleAufgabe.WaitAsync(abbrechen);
        }
        finally
        {
            if (wechselAbbruch != null)
            {
                await wechselAbbruch.CancelAsync();
                await wechselSender!;
                wechselAbbruch.Dispose();
            }
            await senderAbbruch.CancelAsync();
            await sender;
            // Shutdown löst auch eine eventuell noch blockierte synchrone ReadSample-Anforderung.
            quelle?.Object.Shutdown();
            if (sampleAufgabe != null && !sampleAufgabe.IsCompleted)
            {
                try { await sampleAufgabe.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (Exception) { }
            }
            leser?.Dispose();
            quelle?.Dispose();
            aktivierung?.Object.ShutdownObject();
            aktivierung?.Dispose();
        }
    }

    private static IComObject<IMFActivate>? FindeKamera()
    {
        using var attribute = MFFunctions.MFCreateAttributes(1);
        attribute.Object.SetGUID(MFConstants.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
            MFConstants.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID).ThrowOnError();
        Functions.MFEnumDeviceSources(attribute.Object, out IntPtr zeiger, out uint anzahl).ThrowOnError();
        var geraete = new List<IComObject<IMFActivate>>();
        try
        {
            try
            {
                for (uint i = 0; i < anzahl; i++)
                    geraete.Add(ComObject.From<IMFActivate>(Marshal.ReadIntPtr(zeiger, checked((int)i * IntPtr.Size))));
            }
            finally
            {
                // MFEnumDeviceSources übergibt pro Zeiger eine Referenz sowie einen CoTaskMem-Array.
                for (uint i = 0; i < anzahl; i++) Marshal.Release(Marshal.ReadIntPtr(zeiger, checked((int)i * IntPtr.Size)));
                Marshal.FreeCoTaskMem(zeiger);
            }
            var kamera = geraete.FirstOrDefault(geraet =>
                geraet.Object.GetString(MFConstants.MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME, "")
                    .Contains("Drone Live Kamera", StringComparison.OrdinalIgnoreCase));
            if (kamera != null) geraete.Remove(kamera);
            return kamera;
        }
        finally { foreach (var geraet in geraete) geraet.Dispose(); }
    }

    private static void WaehleRohformat(IMFSourceReader leser)
    {
        IComObject<IMFMediaType>? rgb = null;
        try
        {
            for (uint index = 0; ; index++)
            {
                if (leser.GetNativeMediaType(0, index, out var typ).IsError) break;
                var medientyp = new ComObject<IMFMediaType>(typ);
                typ.GetGUID(MFConstants.MF_MT_SUBTYPE, out Guid format).ThrowOnError();
                if (format == MFConstants.MFVideoFormat_NV12)
                {
                    using (medientyp) leser.SetCurrentMediaType(0, IntPtr.Zero, typ).ThrowOnError();
                    return;
                }
                if (format == MFConstants.MFVideoFormat_RGB32 && rgb == null) rgb = medientyp;
                else medientyp.Dispose();
            }
            if (rgb == null) throw new NotSupportedException("Die registrierte Kamera bietet weder NV12 noch RGB32 an.");
            leser.SetCurrentMediaType(0, IntPtr.Zero, rgb.Object).ThrowOnError();
        }
        finally { rgb?.Dispose(); }
    }

    private static byte[]? LeseSample(IMFSourceReader leser)
    {
        IntPtr ausgaben = Marshal.AllocHGlobal(16);
        try
        {
            leser.ReadSample(0, 0, ausgaben, ausgaben + 4, ausgaben + 8, out var sample).ThrowOnError();
            if (sample == null) return null;
            using var sampleCom = new ComObject<IMFSample>(sample);
            sample.ConvertToContiguousBuffer(out var puffer).ThrowOnError();
            using var pufferCom = new ComObject<IMFMediaBuffer>(puffer);
            byte[] daten = new byte[pufferCom.GetCurrentLength()];
            pufferCom.WithLock((zeiger, _, _) => Marshal.Copy(zeiger, daten, 0, daten.Length));
            return daten;
        }
        finally { Marshal.FreeHGlobal(ausgaben); }
    }

    private static bool GleichesBild(byte[] bild, byte[] erwartet, Guid format)
    {
        if (bild.Length != erwartet.Length) return false;
        if (format == MFConstants.MFVideoFormat_NV12) return bild.AsSpan().SequenceEqual(erwartet);
        // RGB32 hat ein unbenutztes viertes Byte, das Windows verändern darf.
        for (int p = 0; p < bild.Length; p += 4)
            if (bild[p] != erwartet[p] || bild[p + 1] != erwartet[p + 1] || bild[p + 2] != erwartet[p + 2]) return false;
        return true;
    }

    private static byte[] Farbbild(int breite, int hoehe, byte b, byte g, byte r)
    {
        var bild = new byte[breite * hoehe * 4];
        for (int p = 0; p < bild.Length; p += 4)
        {
            bild[p] = b; bild[p + 1] = g; bild[p + 2] = r; bild[p + 3] = 255;
        }
        return bild;
    }
}

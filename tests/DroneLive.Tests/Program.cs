using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Drawing.Imaging;
using DirectN;
using DroneLive.Core;
using VCamNetSampleSource;
using DroneLive.App;
using Anwendungskonfiguration = DroneLive.App.AppConfiguration;

int bestanden = 0;
void Pruefe(bool bedingung, string text)
{
    if (!bedingung) throw new Exception("Prüfung fehlgeschlagen: " + text);
    bestanden++;
    Console.WriteLine("OK: " + text);
}
void ErwarteterFehler<T>(Action aktion, string text) where T : Exception
{
    try { aktion(); } catch (T) { Pruefe(true, text); return; }
    Pruefe(false, text);
}
if (args.Contains("--bildaustausch"))
{
    MFFunctions.MFStartup();
    try { await FrameExchangeTests.PruefeAsync(Pruefe); await FramePublishingTests.PruefeAsync(Pruefe); }
    finally { MFFunctions.MFShutdown(); }
    Console.WriteLine($"{bestanden} Prüfungen bestanden.");
    return;
}
if (args.Contains("--installation"))
{
    CameraRegistrationValidator.Pruefe();
    Console.WriteLine("Registrierte Kameraquelle und Anwendung verwenden dieselben DLLs.");
    return;
}
if (args.Contains("--kamera"))
{
    await CameraEndToEndTests.PruefeAsync(Pruefe);
    Console.WriteLine($"{bestanden} Prüfungen der registrierten virtuellen Kamera bestanden.");
    return;
}
byte[] Tag(byte[] daten, byte typ = 9, uint dts = 0)
{
    var bytes = new byte[15 + daten.Length];
    bytes[0] = typ;
    bytes[1] = (byte)(daten.Length >> 16); bytes[2] = (byte)(daten.Length >> 8); bytes[3] = (byte)daten.Length;
    bytes[4] = (byte)(dts >> 16); bytes[5] = (byte)(dts >> 8); bytes[6] = (byte)dts; bytes[7] = (byte)(dts >> 24);
    daten.CopyTo(bytes, 11);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(11 + daten.Length), (uint)(11 + daten.Length));
    return bytes;
}
byte[] Konfiguration(int laengenBytes = 4, byte profil = 66) => Tag([0x17, 0, 0, 0, 0, 1, profil, 0, 30, (byte)(0xfc | (laengenBytes - 1)), 0xe1, 0, 2, 0x67, profil, 1, 0, 2, 0x68, 0xce]);
byte[] Frame(int laengenBytes = 4, bool key = true, int komposition = 0, uint dts = 0)
{
    var daten = new List<byte> { (byte)(key ? 0x17 : 0x27), 1, (byte)(komposition >> 16), (byte)(komposition >> 8), (byte)komposition };
    for (int i = laengenBytes - 1; i >= 0; i--) daten.Add((byte)(2 >> (8 * i)));
    daten.AddRange([0x65, 0x88]);
    return Tag(daten.ToArray(), dts: dts);
}
var drohnen = HubClient.LeseDrohnen(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "drones.json")));
Pruefe(drohnen.Count == 3 && drohnen[0].Zustand == "live" && drohnen[2].Codec == null, "Geprüftes Server-JSON: drones/name/state/video");
Pruefe(drohnen[0].Formatbeschreibung == "640 × 512 · H.264" && drohnen[2].Formatbeschreibung == "noch kein Signal" &&
    new Drohne("x", "X", "live", "hvc1", 1920, 1080).Formatbeschreibung == "1920 × 1080 · H.265 · nicht unterstützt", "Drohnenliste zeigt Bildgröße und Format aus video.width/height/codec");
using (var hub = new HubClient("https://beispiel.test:8443")) Pruefe(hub.StreamAdresse("a/b").AbsoluteUri == "wss://beispiel.test:8443/ws/live/a%2Fb", "HTTPS → WSS, Port und URL-kodierte Drohnen-ID");
ErwarteterFehler<ArgumentException>(() => new HubClient("rtmp://beispiel.test"), "RTMP-Adresse abgelehnt");
foreach (int laengenBytes in new[] { 1, 2, 4 })
{
    var parser = new FlvParser();
    Pruefe(parser.Lese(Konfiguration(laengenBytes)).Count == 0, "Sequence-Header ohne Videoframe");
    Pruefe(parser.Lese(Frame(laengenBytes, false)).Count == 0, "Interframe vor Keyframe verworfen");
    var paket = parser.Lese(Frame(laengenBytes, komposition: -2, dts: 0xf1234567)).Single();
    Pruefe(paket.Dts == 0xf1234567L && paket.Pts == 0xf1234565L, "Unsigned DTS und negative 24-Bit-CompositionTime");
    Pruefe(paket.AnnexB.AsSpan().SequenceEqual(new byte[] { 0, 0, 0, 1, 0x67, 66, 0, 0, 0, 1, 0x68, 0xce, 0, 0, 0, 1, 0x65, 0x88 }), $"Annex B und SPS/PPS, {laengenBytes}-Byte-NAL-Längen");
    parser.Lese(Konfiguration(laengenBytes));
    Pruefe(parser.Lese(Frame(laengenBytes, false)).Count == 1, "Identische Konfiguration unterbricht den Decoder nicht");
    parser.Lese(Konfiguration(laengenBytes, 77));
    Pruefe(parser.Lese(Frame(laengenBytes, false)).Count == 0, "Neue Konfiguration wartet auf Keyframe");
}
byte[] Paket(params byte[] nals) => Tag([0x17, 1, 0, 0, 0, .. nals]);
var nalParser = new FlvParser();
var nalWarnungen = new List<string>();
int nalHinweise = 0;
nalParser.Warnung += nalWarnungen.Add;
nalParser.Hinweis += _ => nalHinweise++;
nalParser.Lese(Konfiguration());
Pruefe(nalHinweise == 0, "Erste Konfiguration einer Verbindung ist kein Konfigurationswechsel");
Pruefe(nalParser.Lese(Paket(0, 0, 0, 2, 0x65, 0x88, 0, 0, 0, 0)).Count == 1 && nalWarnungen.Count == 1, "NAL mit angehängten Nullbytes: Paket wird ausgegeben, Hinweis gemeldet");
Pruefe(nalParser.Lese(Paket(0, 0, 0, 9, 0x65, 0x88)).Count == 0 && nalParser.Lese(Frame(key: false)).Count == 0, "Zu lange NAL-Länge: Paket verworfen, Warten auf Keyframe");
Pruefe(nalParser.Lese(Paket(0, 0, 0, 0, 0x65, 0x88)).Count == 0, "NAL-Länge 0: Paket verworfen");
Pruefe(nalParser.Lese(Frame()).Count == 1 && nalParser.Lese(Frame(key: false)).Count == 1, "Nächster Keyframe wird nach ungültiger NAL-Länge normal dekodiert");
var zweiNals = nalParser.Lese(Paket(0, 0, 0, 2, 0x06, 0x01, 0, 0, 0, 2, 0x65, 0x88)).Single();
Pruefe(zweiNals.AnnexB.AsSpan().SequenceEqual(new byte[] { 0, 0, 0, 1, 0x67, 66, 0, 0, 0, 1, 0x68, 0xce, 0, 0, 0, 1, 0x06, 0x01, 0, 0, 0, 1, 0x65, 0x88 }), "Zwei NALs (SEI + IDR) erscheinen beide in Annex B");
Pruefe(nalWarnungen.Count == 1 && nalHinweise == 0, "NAL-Hinweis nur einmal je Parser, ohne Konfigurationswechsel");
ErwarteterFehler<InvalidDataException>(() => new FlvParser().Lese(Konfiguration(3)), "Reservierte NAL-Längenfeldgröße abgelehnt");
ErwarteterFehler<NotSupportedException>(() => new FlvParser().Lese(Tag([0x90, 0x68, 0x76, 0x63, 0x31])), "Enhanced RTMP/H.265 meldet verständlichen Fehler");
ErwarteterFehler<NotSupportedException>(() => new FlvParser().Lese(Tag([0x12, 0, 0, 0, 0])), "Anderer FLV-Videocodec abgelehnt");
var defekt = Konfiguration(); defekt[^1]++;
ErwarteterFehler<InvalidDataException>(() => new FlvParser().Lese(defekt), "PreviousTagSize geprüft");
ErwarteterFehler<InvalidDataException>(() => new FlvParser().Lese(Konfiguration()[..^1]), "Abgeschnittener FLV-Tag abgelehnt");
Pruefe(new FlvParser().Lese(Tag([1, 2, 3], 18)).Count == 0, "Metadaten werden ignoriert");
Pruefe(Enumerable.Range(0, 6).Select(i => StreamReceiver.Wiederholungswartezeit(i).TotalSeconds).SequenceEqual(new double[] { 1, 2, 4, 8, 10, 10 }), "Backoff 1/2/4/8/10/10 Sekunden");

string ausgabeordner = Path.GetFullPath("artifacts/test-results");
Directory.CreateDirectory(ausgabeordner);
CameraRegistrationTests.Pruefe(Pruefe);
string konfigPfad = Path.Combine(ausgabeordner, "configuration-test.json");
File.WriteAllText(konfigPfad, """{"Serveradresse":"https://beispiel.test:8443/","FFmpegPfad":"videoprogramm.exe","Hardwaredekodierung":false}""");
var appKonfiguration = Anwendungskonfiguration.Laden(konfigPfad);
Pruefe(appKonfiguration.Serveradresse == "https://beispiel.test:8443/" && !appKonfiguration.Hardwaredekodierung && appKonfiguration.VirtuelleKamera, "Administrator-Konfiguration mit optionalen Standardwerten geladen");
foreach (string json in new[] { "{", "{}", "null", """{"Serveradresse":"rtmp://ungueltig"}""", """{"Serveradresse":"https://beispiel.test","FFmepgPfad":"Tippfehler"}""" })
{
    File.WriteAllText(konfigPfad, json);
    ErwarteterFehler<InvalidDataException>(() => Anwendungskonfiguration.Laden(konfigPfad), "Defekte oder unvollständige Konfiguration mit verständlichem Fehler abgelehnt");
}
ErwarteterFehler<InvalidDataException>(() => Anwendungskonfiguration.Laden(konfigPfad + ".fehlt"), "Fehlende Konfigurationsdatei abgelehnt");
ErwarteterFehler<FileNotFoundException>(() => new Anwendungskonfiguration("https://beispiel.test", "garantiert-nicht-vorhandenes-ffmpeg.exe").FfmpegProgramm(), "Fehlender Decoder vor dem Streamstart erkannt");
string benutzerPfad = Path.Combine(ausgabeordner, "settings-test.json");
File.WriteAllText(benutzerPfad, """{"Serveradresse":"https://alte-adresse.test","FFmpegPfad":"alter-pfad.exe","DrohnenId":"test","Breite":1920,"Hoehe":1080}""");
var auswahl = UserSettings.Laden(benutzerPfad);
auswahl.Speichern(benutzerPfad);
Pruefe(auswahl.DrohnenId == "test" && auswahl.Breite == 1920 && !File.ReadAllText(benutzerPfad).Contains("Serveradresse") && !File.ReadAllText(benutzerPfad).Contains("FFmpeg"), "Alte Benutzerauswahl übernommen; Server und Decoder bleiben ausschließlich in der Administrator-Konfiguration");
Pruefe(MainForm.Startfehlertext(new InvalidDataException("Kameraquelle veraltet")) == "Kameraquelle veraltet" && MainForm.Startfehlertext(new FileNotFoundException("FFmpeg fehlt")) == "FFmpeg fehlt" &&
    MainForm.Startfehlertext(new InvalidOperationException("intern")) != "intern", "Installations- und Dateifehler beim Start werden mit ihrem Hinweis angezeigt, andere Fehler allgemein");
string aufraeumOrdner = Path.Combine(ausgabeordner, "frames-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(aufraeumOrdner);
FrameExchange? laufenderSender = null, laufenderLeser = null;
try
{
    string verwaist = Path.Combine(aufraeumOrdner, Guid.NewGuid().ToString("N") + ".bgra");
    string laufend = Path.Combine(aufraeumOrdner, Guid.NewGuid().ToString("N") + ".bgra");
    string fremd = Path.Combine(aufraeumOrdner, "fremd.txt");
    File.WriteAllBytes(verwaist, [1, 2, 3]);
    File.WriteAllText(fremd, "bleibt");
    laufenderSender = new FrameExchange(laufend, 1280, 720, true);
    laufenderLeser = new FrameExchange(laufend, 1280, 720, false);
    Pruefe(VirtualCameraOutput.EntferneVerwaisteBilddateien(aufraeumOrdner) == 1 && !File.Exists(verwaist) && File.Exists(laufend) && File.Exists(fremd),
        "Verwaiste Bilddatei entfernt; laufende Sitzung und fremde Dateien bleiben erhalten");
    laufenderSender.Dispose();
    Pruefe(VirtualCameraOutput.EntferneVerwaisteBilddateien(aufraeumOrdner) == 0 && File.Exists(laufend), "Vom Frame Server noch gelesene Bilddatei bleibt nach Ende der Anwendung erhalten");
    laufenderLeser.Dispose();
    Pruefe(VirtualCameraOutput.EntferneVerwaisteBilddateien(aufraeumOrdner) == 1 && !File.Exists(laufend), "Nicht mehr verwendete Bilddatei wird beim nächsten Kamerastart entfernt");
}
finally { laufenderLeser?.Dispose(); laufenderSender?.Dispose(); Directory.Delete(aufraeumOrdner, true); }
string bildpfad = Path.Combine(ausgabeordner, Guid.NewGuid() + ".bgra");
MFFunctions.MFStartup();
try
{
    await FrameExchangeTests.PruefeAsync(Pruefe);
    await FramePublishingTests.PruefeAsync(Pruefe);
    using var schreiber = new FrameExchange(bildpfad, 1280, 720, true);
    using var leser = new FrameExchange(bildpfad, 1280, 720, false);
    var bytes = new byte[1280 * 720 * 4];
    Pruefe(!leser.Lese(bytes), "Leerer Bildspeicher liefert Kein Signal");
    var signalbild = Bildwerkzeuge.KeinSignal(1280, 720);
    schreiber.Schreibe(signalbild);
    Pruefe(leser.Lese(bytes) && bytes.AsSpan().SequenceEqual(signalbild), "Prozessübergreifender BGRA-Bildaustausch");
    using (var bild = Bildwerkzeuge.AlsBitmap(signalbild, 1280, 720)) bild.Save(Path.Combine(ausgabeordner, "no-signal.png"), ImageFormat.Png);
    using var generator = new FrameGenerator(bildpfad, 1280, 720);
    using var sample = MFFunctions.MFCreateSample();
    generator.Generate(sample, MFConstants.MFVideoFormat_NV12);
    using (var puffer = sample.GetBufferByIndex(0)) Pruefe(puffer.GetCurrentLength() == 1280 * 720 * 3 / 2, "Media-Foundation-Sample enthält unkomprimiertes NV12");
    schreiber.KeinSignal();
    Pruefe(!leser.Lese(bytes), "Verbindungsausfall invalidiert das Bild sofort");
    generator.Generate(sample, MFConstants.MFVideoFormat_RGB32);
    using (var puffer = sample.GetBufferByIndex(0)) Pruefe(puffer.GetCurrentLength() == signalbild.Length, "Kein-Signal-Sample enthält RGB32");
    schreiber.Schreibe(signalbild);
    await Task.Delay(2100);
    Pruefe(!leser.Lese(bytes), "Ohne neuen Senderframe nach zwei Sekunden Kein Signal");
    using var quelle = new MediaSource(1280, 720, bildpfad);
    quelle.CreatePresentationDescriptor(out var descriptor).ThrowOnError();
    using var pd = new ComObject<IMFPresentationDescriptor>(descriptor);
    descriptor.GetStreamDescriptorByIndex(0, out _, out var sd).ThrowOnError();
    using var streamDescriptor = new ComObject<IMFStreamDescriptor>(sd);
    sd.GetMediaTypeHandler(out var handler).ThrowOnError();
    handler.GetCurrentMediaType(out var typ).ThrowOnError();
    typ.GetUINT64(MFConstants.MF_MT_FRAME_SIZE, out var groesse).ThrowOnError();
    Pruefe(groesse == ((ulong)1280 << 32 | 720), "Media-Source meldet stabiles 1280 × 720");
    MarshalFreigabe(handler);
    using (var stream = new MediaStream(quelle, 0, 1280, 720, bildpfad))
    {
        Console.WriteLine("Kameratest: Allocator erstellen.");
        Functions.MFCreateVideoSampleAllocatorEx(typeof(IMFVideoSampleAllocatorEx).GUID, out var streamAllocator).ThrowOnError();
        using var streamAllocatorCom = ComObject.From<IMFVideoSampleAllocatorEx>(streamAllocator);
        System.Runtime.InteropServices.Marshal.Release(streamAllocator);
        stream.SetAllocator(streamAllocatorCom.Object).ThrowOnError();
        Console.WriteLine("Kameratest: Stream starten.");
        stream.Start(typ).ThrowOnError();
        Console.WriteLine("Kameratest: Startereignis lesen.");
        stream.GetEvent(0, out var streamStart).ThrowOnError();
        Console.WriteLine("Kameratest: Sample anfordern.");
        MarshalFreigabe(streamStart);
        var uhr = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
        {
            stream.RequestSample(null!).ThrowOnError();
            uint sampleEreignis = await Task.Run(() =>
            {
                stream.GetEvent(0, out var ereignis).ThrowOnError();
                using var ereignisCom = new ComObject<IMFMediaEvent>(ereignis);
                ereignis.GetType(out var ereignistyp).ThrowOnError();
                return ereignistyp;
            }).WaitAsync(TimeSpan.FromSeconds(5));
            Pruefe(sampleEreignis == (uint)__MIDL___MIDL_itf_mfobjects_0000_0013_0001.MEMediaSample, "Asynchron angefordertes Kamera-Sample " + (i + 1));
        }
        Pruefe(uhr.ElapsedMilliseconds >= 100, "Kamera-Samples werden auf etwa 30 Bilder/s begrenzt");
        stream.Stop().ThrowOnError();
        Pruefe(stream.RequestSample(null!).IsError, "Gestoppter Kamerastream lehnt neue Sample-Anforderungen ab");
    }
    MarshalFreigabe(typ);
    Functions.MFCreateVideoSampleAllocatorEx(typeof(IMFVideoSampleAllocatorEx).GUID, out var allocator).ThrowOnError();
    using var allocatorCom = ComObject.From<IMFVideoSampleAllocatorEx>(allocator);
    System.Runtime.InteropServices.Marshal.Release(allocator);
    quelle.SetDefaultAllocator(0, allocatorCom.Object).ThrowOnError();
    descriptor.SelectStream(0).ThrowOnError();
    using var startposition = new PropVariant(0L);
    quelle.Start(descriptor, IntPtr.Zero, startposition.Detached).ThrowOnError();
    quelle.GetEvent(0, out var neu).ThrowOnError();
    using var neuesEreignis = new ComObject<IMFMediaEvent>(neu);
    neu.GetType(out var ereignistyp).ThrowOnError();
    Pruefe(ereignistyp == (uint)__MIDL___MIDL_itf_mfobjects_0000_0013_0001.MENewStream, "Media-Source veröffentlicht den Videostream");
    quelle.GetEvent(0, out var gestartet).ThrowOnError();
    using var startEreignis = new ComObject<IMFMediaEvent>(gestartet);
    gestartet.GetType(out ereignistyp).ThrowOnError();
    Pruefe(ereignistyp == (uint)__MIDL___MIDL_itf_mfobjects_0000_0013_0001.MESourceStarted, "Media-Source sendet MESourceStarted");
    quelle.Shutdown().ThrowOnError();
    Pruefe(quelle.GetSourceAttributes(out _).IsError && quelle.CreatePresentationDescriptor(out _).IsError, "Media-Source beendet sich geordnet");
    using var aktivierung = new VCamNetSampleSource.Activator();
    aktivierung.SetUINT32(Shared.Bildbreite, 1280).ThrowOnError();
    aktivierung.SetUINT32(Shared.Bildhoehe, 720).ThrowOnError();
    aktivierung.SetString(Shared.Bildpfad, bildpfad).ThrowOnError();
    aktivierung.ActivateObject(typeof(IMFMediaSource).GUID, out var aktiviert).ThrowOnError();
    Pruefe(aktiviert != IntPtr.Zero, "COM-Aktivierung übernimmt Bildpfad und feste Auflösung");
    System.Runtime.InteropServices.Marshal.Release(aktiviert);
    aktivierung.ShutdownObject().ThrowOnError();
}
finally { MFFunctions.MFShutdown(); File.Delete(bildpfad); }

string ffmpeg = Argument("--ffmpeg") ?? "ffmpeg.exe";
Pruefe(new Anwendungskonfiguration("https://beispiel.test", ffmpeg).FfmpegProgramm() == Path.GetFullPath(ffmpeg), "Konfigurierter FFmpeg-Pfad aufgelöst");
{
    // Install.ps1 trägt ein mitgeliefertes FFmpeg relativ zur EXE ein.
    string unterordner = "ffmpeg-relativ-" + Guid.NewGuid().ToString("N");
    string relativesProgramm = Path.Combine(AppContext.BaseDirectory, unterordner, "ffmpeg.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(relativesProgramm)!);
    File.WriteAllBytes(relativesProgramm, []);
    try { Pruefe(new Anwendungskonfiguration("https://beispiel.test", unterordner + "\\ffmpeg.exe").FfmpegProgramm() == relativesProgramm, "Mitgeliefertes FFmpeg relativ zur EXE aufgelöst"); }
    finally { Directory.Delete(Path.GetDirectoryName(relativesProgramm)!, true); }
}
var flvDateien = new List<byte[]>();
foreach (var groesse in new[] { "640x512", "800x600" })
{
    string datei = Path.Combine(ausgabeordner, "test-" + groesse + ".flv");
    var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
    foreach (string argument in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=" + groesse + ":rate=30", "-t", "3", "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-g", "30", "-bf", "0", "-f", "flv", "-y", datei }) start.ArgumentList.Add(argument);
    using var prozess = Process.Start(start)!;
    string fehler = await prozess.StandardError.ReadToEndAsync();
    await prozess.WaitForExitAsync();
    Pruefe(prozess.ExitCode == 0, "H.264-Testsignal erzeugt: " + groesse + " " + fehler);
    flvDateien.Add(File.ReadAllBytes(datei));
}
var ersterParser = new FlvParser();
Pruefe(ersterParser.Lese(flvDateien[0]).Count > 30, "Vollständige reale FFmpeg-FLV-Datei geparst");
long gesendeteTags = 0;
using (var server = new TestServer(async (socket, nummer, token) =>
{
    await socket.SendAsync(flvDateien[0].AsMemory(0, 13), WebSocketMessageType.Binary, true, token);
    while (!token.IsCancellationRequested)
        foreach (var tag in Tags(flvDateien[0]))
        {
            await socket.SendAsync(tag, WebSocketMessageType.Binary, true, token);
            Interlocked.Increment(ref gesendeteTags);
            await Task.Delay(33, token);
        }
}))
{
    var basis = new UriBuilder(server.Adresse) { Scheme = "http", Path = "/" }.Uri.AbsoluteUri;
    foreach (string pruefung in await MainFormTests.PruefeAsync(basis, ffmpeg, ausgabeordner, () => Interlocked.Read(ref gesendeteTags))) Pruefe(true, pruefung);
}
int bildanzahl = 0;
var fertigBild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
await using (var decoder = new FfmpegDecoder(ffmpeg, 1280, 720, false, bytes => { Interlocked.Increment(ref bildanzahl); fertigBild.TrySetResult(); }, Console.WriteLine))
{
    foreach (var paket in new FlvParser().Lese(flvDateien[0])) await decoder.DekodiereAsync(paket, CancellationToken.None);
    await fertigBild.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Pruefe(bildanzahl > 0, "Annex-B-Dekodierung liefert echte BGRA-Bilder");
}
foreach (int code in new[] { 4000, 4001, 4004, 4008 })
{
    using var server = new TestServer(async (socket, nummer, token) =>
    {
        await socket.CloseOutputAsync((WebSocketCloseStatus)code, "Test", token);
    });
    using var abbruch = new CancellationTokenSource(TimeSpan.FromSeconds(code == 4000 ? 4 : 2));
    var fehler = new ConcurrentQueue<string>();
    var client = new StreamReceiver(server.Adresse, ffmpeg, 1280, 720, false, _ => { }, (zustand, _) => fehler.Enqueue(zustand), _ => { });
    await client.StarteAsync(abbruch.Token);
    Pruefe(code == 4004 ? server.Anzahl == 1 && fehler.Contains("Fehler") : server.Anzahl >= 2 && fehler.Contains("Kein Signal"), "WebSocket-Schlusscode " + code);
    if (code == 4001) Pruefe(server.ErsterAbstand < TimeSpan.FromMilliseconds(500), "4001 verbindet sofort neu");
    if (code == 4000) Pruefe(server.ErsterAbstand >= TimeSpan.FromMilliseconds(900), "4000 respektiert Backoff");
}
{
    // Zwei Fehlversuche erhöhen den Backoff auf 4 Sekunden; die dritte Verbindung liefert etwa 1,5 Sekunden Bilder
    // und endet dann mit 4000. Danach muss wieder 1 statt 4 Sekunden gewartet werden.
    using var abbruch = new CancellationTokenSource(TimeSpan.FromSeconds(12));
    using var server = new TestServer(async (socket, nummer, token) =>
    {
        if (nummer >= 4) { abbruch.Cancel(); return; }
        if (nummer == 3)
        {
            await socket.SendAsync(flvDateien[0].AsMemory(0, 13), WebSocketMessageType.Binary, true, token);
            foreach (var tag in Tags(flvDateien[0]).Take(45))
            {
                await socket.SendAsync(tag, WebSocketMessageType.Binary, true, token);
                await Task.Delay(33, token);
            }
        }
        await socket.CloseOutputAsync((WebSocketCloseStatus)4000, "Test", token);
    });
    int bilder = 0;
    var client = new StreamReceiver(server.Adresse, ffmpeg, 1280, 720, false, _ => Interlocked.Increment(ref bilder), (_, _) => { }, _ => { });
    await client.StarteAsync(abbruch.Token);
    Pruefe(bilder > 0 && server.Anzahl >= 4 && server.Abstand(2) >= TimeSpan.FromMilliseconds(1900) && server.Abstand(3) < TimeSpan.FromSeconds(4.5),
        $"Dekodierte Bilder setzen den Backoff vor dem nächsten Verbindungsversuch zurück ({server.Abstand(3).TotalSeconds:0.0} s nach Bildempfang)");
}
using (var server = new TestServer((_, _, _) => Task.CompletedTask))
using (var hub = new HubClient(new UriBuilder(server.Adresse) { Scheme = "http", Path = "/" }.Uri.AbsoluteUri))
using (var abbruch = new CancellationTokenSource(TimeSpan.FromMilliseconds(2500)))
{
    int aktualisierungen = 0;
    await hub.BeobachteAsync(() => { Interlocked.Increment(ref aktualisierungen); return Task.CompletedTask; }, _ => { }, abbruch.Token);
    Pruefe(aktualisierungen >= 2, "SSE drone-updated erkannt und nach geschlossenem Kanal erneut verbunden");
}
int neueKonfigurationen = 0;
bildanzahl = 0;
using (var server = new TestServer(async (socket, nummer, token) =>
{
    await socket.SendAsync(Tag(new byte[130_000], 18).AsMemory(0, 65_000), WebSocketMessageType.Binary, false, token);
    await socket.SendAsync(Tag(new byte[130_000], 18).AsMemory(65_000), WebSocketMessageType.Binary, true, token);
    foreach (var datei in flvDateien)
    {
        await socket.SendAsync(datei.AsMemory(0, 13), WebSocketMessageType.Binary, true, token);
        foreach (var tag in Tags(datei))
        {
            await socket.SendAsync(tag, WebSocketMessageType.Binary, true, token);
            await Task.Delay(33, token);
        }
    }
    await Task.Delay(Timeout.Infinite, token);
}))
{
    using var abbruch = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var client = new StreamReceiver(server.Adresse, ffmpeg, 1280, 720, false, bytes =>
    {
        if (Interlocked.Increment(ref bildanzahl) == 100)
        {
            using var bild = Bildwerkzeuge.AlsBitmap(bytes, 1280, 720);
            bild.Save(Path.Combine(ausgabeordner, "decoded.png"), ImageFormat.Png);
        }
    }, (_, _) => { }, zeile => { if (zeile.Contains("Neue H.264")) Interlocked.Increment(ref neueKonfigurationen); });
    await client.StarteAsync(abbruch.Token);
    Pruefe(bildanzahl > 100 && neueKonfigurationen == 1 && server.Anzahl == 1, "Fragmentierte 130-KiB-Nachricht und Auflösungswechsel ohne Verbindungsabbruch");
}

if (Argument("--server") is { } serveradresse)
{
    using var hub = new HubClient(serveradresse);
    using var abbruch = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var liste = await hub.LadeDrohnenAsync(abbruch.Token);
    Pruefe(liste.Count > 0, "Live-Server: healthz und api/drones");
    var drohne = liste.First(d => d.Zustand == "live" && d.Codec == "avc");
    int frames = 0;
    var client = new StreamReceiver(hub.StreamAdresse(drohne.Id), ffmpeg, 1280, 720, true, bytes =>
    {
        if (Interlocked.Increment(ref frames) == 30)
        {
            using var bild = Bildwerkzeuge.AlsBitmap(bytes, 1280, 720);
            bild.Save(Path.Combine(ausgabeordner, "live-server.png"), ImageFormat.Png);
        }
    }, (zustand, text) => Console.WriteLine(zustand + ": " + text), Console.WriteLine);
    await client.StarteAsync(abbruch.Token);
    Pruefe(frames >= 30, $"Live-Server: {frames} H.264-Frames über WSS empfangen, dekodiert und auf 1280 × 720 skaliert");
}
Console.WriteLine($"{bestanden} Prüfungen bestanden.");

string? Argument(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
static void MarshalFreigabe(object com) => System.Runtime.InteropServices.Marshal.ReleaseComObject(com);
static IEnumerable<byte[]> Tags(byte[] datei)
{
    int p = 13;
    while (p < datei.Length)
    {
        int laenge = 15 + (datei[p + 1] << 16 | datei[p + 2] << 8 | datei[p + 3]);
        yield return datei.AsSpan(p, laenge).ToArray();
        p += laenge;
    }
}

sealed class TestServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _abbruch = new();
    private readonly Task _aufgabe;
    private readonly ConcurrentBag<Task> _verbindungen = [];
    private readonly ConcurrentQueue<DateTime> _zeiten = new();
    public Uri Adresse { get; }
    public int Anzahl => _zeiten.Count;
    public TimeSpan ErsterAbstand => Abstand(1);
    /// <summary>Zeit zwischen dem Verbindungsaufbau Nummer <paramref name="nummer"/> und dem nächsten.</summary>
    public TimeSpan Abstand(int nummer) { var zeiten = _zeiten.ToArray(); return zeiten.Length > nummer ? zeiten[nummer] - zeiten[nummer - 1] : TimeSpan.MaxValue; }
    public TestServer(Func<WebSocket, int, CancellationToken, Task> antwort)
    {
        var portFinder = new TcpListener(IPAddress.Loopback, 0);
        portFinder.Start(); int port = ((IPEndPoint)portFinder.LocalEndpoint).Port; portFinder.Stop();
        Adresse = new Uri($"ws://127.0.0.1:{port}/ws/live/test");
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _aufgabe = Task.Run(async () =>
        {
            try
            {
                while (!_abbruch.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();
                    if (!context.Request.IsWebSocketRequest)
                    {
                        string? pfad = context.Request.Url?.AbsolutePath;
                        context.Response.ContentType = pfad == "/api/events" ? "text/event-stream" : "application/json";
                        using var writer = new StreamWriter(context.Response.OutputStream);
                        await writer.WriteAsync(pfad switch
                        {
                            "/healthz" => "{\"status\":\"ok\"}",
                            "/api/drones" => """{"drones":[{"id":"test","name":"Testdrohne","state":"live","video":{"codec":"avc"}},{"id":"offline","name":"Offline-Drohne","state":"offline"},{"id":"h265","name":"Andere Kamera","state":"live","video":{"codec":"hvc1"}}]}""",
                            _ => ": Kommentar\nevent: drone-updated\ndata: {}\n\n"
                        });
                        await writer.FlushAsync();
                        context.Response.Close();
                        continue;
                    }
                    var verbindung = await context.AcceptWebSocketAsync(null);
                    _zeiten.Enqueue(DateTime.UtcNow);
                    int nummer = Anzahl;
                    _verbindungen.Add(Task.Run(async () =>
                    {
                        using var socket = verbindung.WebSocket;
                        try { await antwort(socket, nummer, _abbruch.Token); }
                        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
                    }));
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
        });
    }
    public void Dispose()
    {
        _abbruch.Cancel(); _listener.Close();
        _aufgabe.GetAwaiter().GetResult();
        Task.WhenAll(_verbindungen).GetAwaiter().GetResult();
        _abbruch.Dispose();
    }
}

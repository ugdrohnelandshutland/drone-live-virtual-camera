using System.Net.WebSockets;
using System.Threading.Channels;

namespace DroneLive.Core;

public sealed class StreamReceiver(Uri adresse, string ffmpeg, int breite, int hoehe, bool hardware,
    Action<byte[]> bild, Action<string, string> status, Action<string> protokoll)
{
    private sealed class RueckstauException : IOException { }
    private sealed record Empfangsnachricht(byte[] Daten, long Empfangszeit);
    public static TimeSpan Wiederholungswartezeit(int versuch) => TimeSpan.FromSeconds(Math.Min(10, Math.Pow(2, Math.Clamp(versuch, 0, 4))));

    public async Task StarteAsync(CancellationToken abbrechen)
    {
        int versuch = 0;
        while (!abbrechen.IsCancellationRequested)
        {
            TimeSpan? warten = null;
            try
            {
                int? code = await VerbindungAsync(() => versuch = 0, abbrechen);
                protokoll($"Stream geschlossen: {code?.ToString() ?? "ohne Code"}.");
                if (code == 4004) { status("Fehler", "Unbekannte Drohne. Bitte die Drohnenliste aktualisieren."); return; }
                if (code == 4001) { warten = TimeSpan.Zero; protokoll("Kamerawechsel: Decoder wird neu erstellt."); }
                if (code == 4008) { warten = TimeSpan.FromMilliseconds(200); protokoll("Viewer zu langsam: Verbindung und Decoder werden erneuert."); }
                status("Kein Signal", code == 4000 ? "Die Drohne ist offline." : "Stream unterbrochen; erneute Verbindung.");
            }
            catch (OperationCanceledException) when (abbrechen.IsCancellationRequested) { return; }
            catch (NotSupportedException ex) { protokoll(ex.Message); status("Fehler", ex.Message); return; }
            catch (RueckstauException)
            {
                warten = TimeSpan.FromMilliseconds(200);
                protokoll("Empfangspuffer voll: Rückstand verworfen, neuer Einstieg ab Keyframe.");
                status("Kein Signal", "Stream wird nach Rückstau neu verbunden.");
            }
            catch (Exception ex) { protokoll("Stream: " + ex.Message); status("Kein Signal", "Verbindung unterbrochen: " + ex.Message); }
            // Erst nach dem Verbindungsende berechnen: Dekodierte Bilder dieser Verbindung haben den Zähler zurückgesetzt.
            warten ??= Wiederholungswartezeit(versuch++);
            try { await Task.Delay(warten.Value, abbrechen); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task<int?> VerbindungAsync(Action stabil, CancellationToken abbrechen)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(10);
        using var lauf = CancellationTokenSource.CreateLinkedTokenSource(abbrechen);
        using (var zeitlimit = CancellationTokenSource.CreateLinkedTokenSource(lauf.Token))
        {
            zeitlimit.CancelAfter(TimeSpan.FromSeconds(10));
            await socket.ConnectAsync(adresse, zeitlimit.Token);
        }
        status("Kein Signal", "Verbindung hergestellt; warte auf ein dekodiertes Bild.");
        // Der kurze Keyframe-Rückblick kommt beim Verbinden als Burst. Seine Bytes und sein Alter bleiben begrenzt.
        long wartendeBytes = 0;
        var kanal = Channel.CreateBounded<Empfangsnachricht>(new BoundedChannelOptions(64) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        var empfang = Task.Run(async () =>
        {
            byte[] puffer = new byte[64 * 1024];
            while (!lauf.IsCancellationRequested)
            {
                int laenge = 0;
                ValueWebSocketReceiveResult antwort;
                do
                {
                    if (laenge == puffer.Length)
                    {
                        if (puffer.Length >= 16 * 1024 * 1024) throw new InvalidDataException("WebSocket-Nachricht ist größer als 16 MiB.");
                        Array.Resize(ref puffer, puffer.Length * 2);
                    }
                    antwort = await socket.ReceiveAsync(puffer.AsMemory(laenge), lauf.Token);
                    if (antwort.MessageType == WebSocketMessageType.Close) return (int?)socket.CloseStatus;
                    if (antwort.MessageType != WebSocketMessageType.Binary) throw new InvalidDataException("Binäre FLV-Nachricht erwartet.");
                    laenge += antwort.Count;
                } while (!antwort.EndOfMessage);
                if (laenge > 0)
                {
                    if (Interlocked.Add(ref wartendeBytes, laenge) > 16 * 1024 * 1024) throw new RueckstauException();
                    var nachricht = new Empfangsnachricht(puffer.AsSpan(0, laenge).ToArray(), Environment.TickCount64);
                    if (!kanal.Writer.TryWrite(nachricht)) throw new RueckstauException();
                }
            }
            return (int?)null;
        }, lauf.Token);
        var verarbeitung = Task.Run(async () =>
        {
            var leser = new FlvParser();
            int generation = 0;
            leser.Hinweis += meldung => { Interlocked.Increment(ref generation); status("Kein Signal", meldung); protokoll(meldung); };
            byte[]? konfiguration = null;
            FfmpegDecoder? decoder = null;
            try
            {
                await foreach (var nachricht in kanal.Reader.ReadAllAsync(lauf.Token))
                {
                    Interlocked.Add(ref wartendeBytes, -nachricht.Daten.Length);
                    if (Environment.TickCount64 - nachricht.Empfangszeit > 750) throw new RueckstauException();
                    foreach (var paket in leser.Lese(nachricht.Daten))
                    {
                        if (konfiguration == null || !konfiguration.AsSpan().SequenceEqual(paket.Konfiguration))
                        {
                            if (decoder != null) await decoder.DisposeAsync();
                            konfiguration = paket.Konfiguration;
                            int decoderGeneration = Volatile.Read(ref generation);
                            decoder = new FfmpegDecoder(ffmpeg, breite, hoehe, hardware, bytes =>
                            {
                                if (!lauf.IsCancellationRequested && decoderGeneration == Volatile.Read(ref generation)) { stabil(); bild(bytes); }
                            }, protokoll);
                        }
                        await decoder!.DekodiereAsync(paket, lauf.Token);
                    }
                }
            }
            finally { if (decoder != null) await decoder.DisposeAsync(); }
        }, lauf.Token);
        var fertig = await Task.WhenAny(empfang, verarbeitung);
        await lauf.CancelAsync();
        socket.Abort();
        kanal.Writer.TryComplete();
        try
        {
            await fertig;
            return empfang.IsCompletedSuccessfully ? empfang.Result : null;
        }
        finally
        {
            try { await Task.WhenAll(empfang, verarbeitung); } catch { /* Der ursprüngliche Fehler wird oben weitergegeben. */ }
        }
    }
}

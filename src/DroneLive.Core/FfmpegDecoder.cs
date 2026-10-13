using System.Diagnostics;

namespace DroneLive.Core;

/// <summary>Dekodierung in einem getrennten Prozess mit unabhängig gelesener BGRA-Ausgabe.</summary>
public sealed class FfmpegDecoder : IAsyncDisposable
{
    private readonly Process _prozess;
    private readonly CancellationTokenSource _abbruch = new();
    private readonly Task _ausgabe;
    private readonly Task _fehler;
    public FfmpegDecoder(string programm, int breite, int hoehe, bool hardware,
        Action<byte[]> bild, Action<string> protokoll)
    {
        var start = new ProcessStartInfo(programm)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        string[] argumente = ["-hide_banner", "-loglevel", "warning", "-probesize", "32", "-analyzeduration", "0",
            "-fflags", "+discardcorrupt", "-flags", "low_delay", "-threads", "2"];
        foreach (var argument in argumente) start.ArgumentList.Add(argument);
        if (hardware) { start.ArgumentList.Add("-hwaccel"); start.ArgumentList.Add("auto"); }
        foreach (var argument in new[] { "-f", "h264", "-framerate", "30", "-i", "pipe:0", "-an", "-vf",
            $"scale={breite}:{hoehe}:force_original_aspect_ratio=decrease,pad={breite}:{hoehe}:(ow-iw)/2:(oh-ih)/2,setsar=1",
            "-pix_fmt", "bgra", "-fps_mode", "passthrough", "-f", "rawvideo", "pipe:1" }) start.ArgumentList.Add(argument);
        _prozess = Process.Start(start) ?? throw new IOException("FFmpeg konnte nicht gestartet werden.");
        _ausgabe = Task.Run(async () =>
        {
            while (!_abbruch.IsCancellationRequested)
            {
                byte[] bytes = new byte[checked(breite * hoehe * 4)];
                try { await _prozess.StandardOutput.BaseStream.ReadExactlyAsync(bytes, _abbruch.Token); }
                catch (EndOfStreamException) { break; }
                bild(bytes);
            }
        });
        _fehler = Task.Run(async () =>
        {
            while (await _prozess.StandardError.ReadLineAsync(_abbruch.Token) is { } zeile)
                protokoll("FFmpeg: " + zeile);
        });
    }
    public async Task DekodiereAsync(Videopaket paket, CancellationToken abbrechen)
    {
        if (_ausgabe.IsCompleted || _prozess.HasExited) throw new IOException("FFmpeg hat die Dekodierung beendet. Bitte das Protokoll prüfen.");
        using var zeitlimit = CancellationTokenSource.CreateLinkedTokenSource(abbrechen);
        zeitlimit.CancelAfter(TimeSpan.FromSeconds(1));
        await _prozess.StandardInput.BaseStream.WriteAsync(paket.AnnexB, zeitlimit.Token);
        await _prozess.StandardInput.BaseStream.FlushAsync(zeitlimit.Token);
    }
    public async ValueTask DisposeAsync()
    {
        await _abbruch.CancelAsync();
        try { if (!_prozess.HasExited) _prozess.Kill(true); } catch (InvalidOperationException) { }
        try { await Task.WhenAll(_ausgabe, _fehler); } catch (OperationCanceledException) { }
        _prozess.Dispose();
        _abbruch.Dispose();
    }
}

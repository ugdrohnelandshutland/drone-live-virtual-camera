using System.Net.Http.Headers;
using System.Text.Json;

namespace DroneLive.Core;

public sealed record Drohne(string Id, string Name, string Zustand, string? Codec, int? Breite = null, int? Hoehe = null)
{
    public bool IstLive => Zustand == "live";
    public bool IstUnterstuetzt => Codec is null or "avc" or "h264";
    /// <summary>Kurzbeschreibung des Videoformats für die Drohnenliste.</summary>
    public string Formatbeschreibung
    {
        get
        {
            if (!IstLive) return "noch kein Signal";
            string codec = Codec switch { null or "avc" or "h264" => "H.264", "hvc1" or "hev1" or "hevc" or "h265" => "H.265", _ => Codec.ToUpperInvariant() };
            string format = Breite is > 0 && Hoehe is > 0 ? $"{Breite} × {Hoehe} · {codec}" : codec;
            return IstUnterstuetzt ? format : format + " · nicht unterstützt";
        }
    }
    public override string ToString() => $"{Name} ({(IstLive ? "Live" : "Offline")})";
}

public sealed class HubClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    public Uri Basis { get; }
    public HubClient(string adresse)
    {
        if (!Uri.TryCreate(adresse.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Bitte eine Serveradresse mit http:// oder https:// und optionalem Port eingeben, ohne Pfad.");
        Basis = uri;
    }
    public Uri Endpunkt(string pfad) => new(Basis, pfad.TrimStart('/'));
    public Uri StreamAdresse(string id)
    {
        var uri = new UriBuilder(Endpunkt("ws/live/" + Uri.EscapeDataString(id)))
        { Scheme = Basis.Scheme == "https" ? "wss" : "ws", Port = Basis.Port };
        return uri.Uri;
    }
    public async Task<IReadOnlyList<Drohne>> LadeDrohnenAsync(CancellationToken abbrechen)
    {
        using var zeitlimit = CancellationTokenSource.CreateLinkedTokenSource(abbrechen);
        zeitlimit.CancelAfter(TimeSpan.FromSeconds(10));
        using var gesundheit = await _http.GetAsync(Endpunkt("healthz"), zeitlimit.Token);
        gesundheit.EnsureSuccessStatusCode();
        using var antwort = await _http.GetAsync(Endpunkt("api/drones"), zeitlimit.Token);
        antwort.EnsureSuccessStatusCode();
        var json = await antwort.Content.ReadAsStringAsync(zeitlimit.Token);
        return LeseDrohnen(json);
    }
    public static IReadOnlyList<Drohne> LeseDrohnen(string json)
    {
        using var dokument = JsonDocument.Parse(json);
        if (!dokument.RootElement.TryGetProperty("drones", out var liste) || liste.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Die API-Antwort enthält keine Liste ‚drones‘.");
        return liste.EnumerateArray().Select(d =>
        {
            bool mitVideo = d.TryGetProperty("video", out var video) && video.ValueKind == JsonValueKind.Object;
            return new Drohne(
                d.GetProperty("id").GetString() ?? throw new InvalidDataException("Drohnen-ID fehlt."),
                d.GetProperty("name").GetString() ?? "Unbenannte Drohne",
                d.GetProperty("state").GetString() ?? "offline",
                mitVideo && video.TryGetProperty("codec", out var codec) ? codec.GetString() : null,
                mitVideo ? Ganzzahl(video, "width") : null,
                mitVideo ? Ganzzahl(video, "height") : null);
        }).ToArray();
    }
    private static int? Ganzzahl(JsonElement objekt, string name) =>
        objekt.TryGetProperty(name, out var wert) && wert.ValueKind == JsonValueKind.Number && wert.TryGetInt32(out int zahl) ? zahl : null;
    public async Task BeobachteAsync(Func<Task> aktualisieren, Action<string> protokoll, CancellationToken abbrechen)
    {
        while (!abbrechen.IsCancellationRequested)
        {
            try
            {
                using var anfrage = new HttpRequestMessage(HttpMethod.Get, Endpunkt("api/events"));
                anfrage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                using var antwort = await _http.SendAsync(anfrage, HttpCompletionOption.ResponseHeadersRead, abbrechen);
                antwort.EnsureSuccessStatusCode();
                using var leser = new StreamReader(await antwort.Content.ReadAsStreamAsync(abbrechen));
                string ereignis = "";
                while (await leser.ReadLineAsync(abbrechen) is { } zeile)
                {
                    if (zeile.StartsWith("event:")) ereignis = zeile[6..].Trim();
                    if (zeile.Length == 0)
                    {
                        if (ereignis == "drone-updated") await aktualisieren();
                        ereignis = "";
                    }
                }
                protokoll("Statuskanal geschlossen; erneute Verbindung in 2 Sekunden.");
            }
            catch (OperationCanceledException) when (abbrechen.IsCancellationRequested) { break; }
            catch (Exception ex) { protokoll("Statuskanal: " + ex.Message); }
            try { await Task.Delay(2000, abbrechen); } catch (OperationCanceledException) { break; }
        }
    }
    public void Dispose() => _http.Dispose();
}

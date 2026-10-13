using System.Text.Json;
using System.Text.Json.Serialization;
using DroneLive.Core;

namespace DroneLive.App;

/// <summary>Vom Administrator gepflegte Werte; die Oberfläche schreibt diese Datei niemals.</summary>
internal sealed record AppConfiguration(string Serveradresse = "", string FFmpegPfad = "ffmpeg.exe",
    bool Hardwaredekodierung = true, bool VirtuelleKamera = true)
{
    public static string Dateipfad => Path.Combine(AppContext.BaseDirectory, "configuration.json");

    public static AppConfiguration Laden(string? dateipfad = null)
    {
        string pfad = dateipfad ?? Dateipfad;
        try
        {
            var konfiguration = JsonSerializer.Deserialize<AppConfiguration>(File.ReadAllText(pfad),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new InvalidDataException("Die Konfiguration ist leer.");
            konfiguration.Pruefen();
            return konfiguration;
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Die Konfiguration konnte nicht geladen werden.\nBitte die Datei prüfen lassen:\n{pfad}\n\n{ex.Message}", ex);
        }
    }

    public void Pruefen()
    {
        if (string.IsNullOrWhiteSpace(Serveradresse)) throw new ArgumentException("Die Serveradresse fehlt.");
        using var hub = new HubClient(Serveradresse);
        if (string.IsNullOrWhiteSpace(FFmpegPfad)) throw new ArgumentException("Der FFmpeg-Pfad fehlt.");
    }

    public string FfmpegProgramm()
    {
        string pfad = Environment.ExpandEnvironmentVariables(FFmpegPfad.Trim());
        string lokalerPfad = Path.GetFullPath(pfad, AppContext.BaseDirectory);
        if (File.Exists(lokalerPfad)) return lokalerPfad;
        if (Path.GetFileName(pfad) == pfad)
        {
            foreach (string ordner in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string kandidat = Path.Combine(ordner.Trim().Trim('"'), pfad);
                if (File.Exists(kandidat)) return Path.GetFullPath(kandidat);
            }
        }
        throw new FileNotFoundException("Das Videoprogramm FFmpeg wurde nicht gefunden. Bitte den FFmpeg-Pfad in configuration.json prüfen lassen.");
    }
}

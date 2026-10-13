using System.Text.Json;

namespace DroneLive.App;

internal sealed record UserSettings(string DrohnenId = "", int Breite = 1280, int Hoehe = 720)
{
    internal static string Dateipfad => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DroneLive2VirtualCamera", "settings.json");
    public static UserSettings Laden(string? dateipfad = null)
    {
        try
        {
            string pfad = dateipfad ?? Dateipfad;
            // Vorhandene Benutzereinstellungen bei der ersten Ausführung nach der Umbenennung übernehmen.
            if (dateipfad == null && !File.Exists(pfad))
                pfad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DroneLiveKamera", "einstellungen.json");
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(pfad)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Speichern(string? dateipfad = null)
    {
        string pfad = dateipfad ?? Dateipfad;
        Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
        var zwischenablage = pfad + ".tmp";
        File.WriteAllText(zwischenablage, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(zwischenablage, pfad, true);
    }
}

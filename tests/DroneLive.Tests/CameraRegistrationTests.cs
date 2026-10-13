using DroneLive.App;

internal static class CameraRegistrationTests
{
    public static void Pruefe(Action<bool, string> pruefe)
    {
        string testordner = Path.GetFullPath(Path.Combine("artifacts/test-results", "registration-" + Guid.NewGuid().ToString("N")));
        string anwendung = Path.Combine(testordner, "Anwendung");
        string installation = Path.Combine(testordner, "Installation");
        string[] dateien = ["DroneLive.Core.dll", "VCamNetSampleSource.dll", "VCamNetSampleSource.comhost.dll"];
        Directory.CreateDirectory(anwendung);
        Directory.CreateDirectory(installation);
        try
        {
            foreach (string datei in dateien)
            {
                byte[] daten = System.Text.Encoding.UTF8.GetBytes("Aktueller Programmstand für " + datei);
                File.WriteAllBytes(Path.Combine(anwendung, datei), daten);
                File.WriteAllBytes(Path.Combine(installation, datei), daten);
            }

            CameraRegistrationValidator.PruefeOrdner(anwendung, installation);
            pruefe(true, "Identische Anwendung und installierte Kameraquelle werden akzeptiert");
            CameraRegistrationValidator.PruefeOrdner(anwendung, anwendung);
            pruefe(true, "COM-Registrierung direkt auf den aktuellen Anwendungsordner wird akzeptiert");

            foreach (string datei in dateien)
            {
                string pfad = Path.Combine(installation, datei);
                byte[] richtig = File.ReadAllBytes(pfad);
                byte[] abweichend = (byte[])richtig.Clone();
                abweichend[^1] ^= 1;
                File.WriteAllBytes(pfad, abweichend);
                bool abgelehnt = false;
                try { CameraRegistrationValidator.PruefeOrdner(anwendung, installation); }
                catch (InvalidDataException ex)
                {
                    abgelehnt = ex.Message.Contains(datei) && ex.Message.Contains(anwendung) && ex.Message.Contains(installation);
                }
                pruefe(abgelehnt, $"Veraltete Kameradatei trotz gleicher Dateigröße wird mit beiden Ordnerpfaden abgelehnt: {datei}");
                File.WriteAllBytes(pfad, richtig);

                File.Delete(pfad);
                abgelehnt = false;
                try { CameraRegistrationValidator.PruefeOrdner(anwendung, installation); }
                catch (IOException ex)
                {
                    abgelehnt = ex.Message.Contains(datei) && ex.Message.Contains(anwendung) && ex.Message.Contains(installation);
                }
                pruefe(abgelehnt, $"Fehlende installierte Kameradatei wird verständlich abgelehnt: {datei}");
                File.WriteAllBytes(pfad, richtig);
            }

            File.Delete(Path.Combine(anwendung, dateien[0]));
            bool unvollstaendig = false;
            try { CameraRegistrationValidator.PruefeOrdner(anwendung, installation); }
            catch (IOException ex) { unvollstaendig = ex.Message.Contains("DroneLive.Core.dll") && ex.Message.Contains(anwendung); }
            pruefe(unvollstaendig, "Ein unvollständiger Anwendungs-Build wird vor Kameraaktivierung erkannt");
        }
        finally { Directory.Delete(testordner, true); }
    }
}

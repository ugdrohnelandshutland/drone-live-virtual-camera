using System.Security.Cryptography;
using Microsoft.Win32;
using VCamNetSampleSource;

namespace DroneLive.App;

/// <summary>Verhindert, dass Vorschau und registrierte COM-Kamera verschiedene Programmstände verwenden.</summary>
internal static class CameraRegistrationValidator
{
    private const string ComHostDatei = "VCamNetSampleSource.comhost.dll";
    private static readonly string[] Kameradateien = ["DroneLive.Core.dll", "VCamNetSampleSource.dll", ComHostDatei];

    public static void Pruefe()
    {
        string anwendungsOrdner = Path.GetFullPath(AppContext.BaseDirectory);
        string installationsOrdner;
        try
        {
            using var klassen = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);
            using var registrierung = klassen.OpenSubKey($"CLSID\\{{{Shared.CLSID_VCamNet}}}\\InprocServer32");
            string? pfad = registrierung?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(pfad))
                throw new IOException($"Die virtuelle Kamera ist nicht registriert. Bitte scripts/RegisterCamera.ps1 aus dieser Anwendung als Administrator ausführen. Anwendungsordner: {anwendungsOrdner}");

            pfad = Path.GetFullPath(Environment.ExpandEnvironmentVariables(pfad.Trim().Trim('"')));
            if (!string.Equals(Path.GetFileName(pfad), ComHostDatei, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Die virtuelle Kamera ist mit einem unerwarteten COM-Host registriert: {pfad}. Bitte scripts/RegisterCamera.ps1 aus dieser Anwendung als Administrator ausführen. Anwendungsordner: {anwendungsOrdner}");
            installationsOrdner = Path.GetDirectoryName(pfad)!;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            throw new IOException($"Die Registrierung der virtuellen Kamera konnte nicht geprüft werden. Bitte Kamera-Installation und Zugriffsrechte prüfen lassen. Anwendungsordner: {anwendungsOrdner}", ex);
        }

        PruefeOrdner(anwendungsOrdner, installationsOrdner);
    }

    /// <summary>Prüft Dateien ohne COM-Aktivierung und ohne installierte Assemblies in den Prozess zu laden.</summary>
    internal static void PruefeOrdner(string anwendungsOrdner, string installationsOrdner)
    {
        anwendungsOrdner = Path.GetFullPath(anwendungsOrdner);
        installationsOrdner = Path.GetFullPath(installationsOrdner);
        foreach (string dateiname in Kameradateien)
        {
            string anwendungsDatei = Path.Combine(anwendungsOrdner, dateiname);
            string installierteDatei = Path.Combine(installationsOrdner, dateiname);
            try
            {
                if (!File.Exists(anwendungsDatei))
                    throw new FileNotFoundException("Die Kameradatei fehlt im Anwendungsordner.", anwendungsDatei);
                if (!File.Exists(installierteDatei))
                    throw new FileNotFoundException("Die Kameradatei fehlt im registrierten Installationsordner.", installierteDatei);
                byte[] erwartet = Pruefsumme(anwendungsDatei);
                byte[] installiert = Pruefsumme(installierteDatei);
                if (!erwartet.AsSpan().SequenceEqual(installiert))
                    throw new InvalidDataException($"Die installierte Kameraquelle passt nicht zu dieser Anwendung ({dateiname}). Bitte Anwendung und Kamera-Verbraucher schließen und scripts/RegisterCamera.ps1 aus diesem Build als Administrator ausführen. Anwendungsordner: {anwendungsOrdner}. Registrierter Kameraordner: {installationsOrdner}.");
            }
            catch (Exception ex) when (ex is IOException && ex is not InvalidDataException || ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                throw new IOException($"Die Kamera-Installation ist unvollständig oder nicht lesbar ({dateiname}). Bitte scripts/RegisterCamera.ps1 aus diesem Build als Administrator ausführen. Anwendungsordner: {anwendungsOrdner}. Registrierter Kameraordner: {installationsOrdner}.", ex);
            }
        }
    }

    private static byte[] Pruefsumme(string pfad)
    {
        using var datei = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(datei);
    }
}

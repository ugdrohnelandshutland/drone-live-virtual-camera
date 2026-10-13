using DirectN;
using DroneLive.Core;
using VCamNetSampleSource;

namespace DroneLive.App;

/// <summary>Kameraausgabe mit fester Bildgröße; bleibt über Drohnenwechsel hinweg bestehen.</summary>
internal interface ICameraOutput : IDisposable
{
    int Breite { get; }
    int Hoehe { get; }
    int EntfernteBilddateien { get; }
    void Schreibe(byte[] bild);
    void KeinSignal();
}

internal sealed class VirtualCameraOutput : ICameraOutput
{
    private readonly object _sperre = new();
    private readonly string _bildpfad;
    private readonly FrameExchange _austausch;
    private IComObject<IMFVirtualCamera>? _kamera;
    private bool _gestartet;
    private bool _beendet;
    public int Breite { get; }
    public int Hoehe { get; }
    public int EntfernteBilddateien { get; }

    public VirtualCameraOutput(int breite, int hoehe)
    {
        Breite = breite;
        Hoehe = hoehe;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            throw new PlatformNotSupportedException("Die virtuelle Kamera benötigt Windows 11 ab Version 22H2.");
        CameraRegistrationValidator.Pruefe();
        string ordner = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DroneLive2VirtualCamera", "Frames");
        if (!Directory.Exists(ordner)) throw new IOException("Die Kamera ist noch nicht eingerichtet. Bitte scripts/RegisterCamera.ps1 als Administrator ausführen.");
        EntfernteBilddateien = EntferneVerwaisteBilddateien(ordner);
        _bildpfad = Path.Combine(ordner, Guid.NewGuid().ToString("N") + ".bgra");
        _austausch = new FrameExchange(_bildpfad, breite, hoehe, true);
        try
        {
            MFFunctions.MFStartup();
            _gestartet = true;
            Functions.MFCreateVirtualCamera(
                __MIDL___MIDL_itf_mfvirtualcamera_0000_0000_0001.MFVirtualCameraType_SoftwareCameraSource,
                __MIDL___MIDL_itf_mfvirtualcamera_0000_0000_0002.MFVirtualCameraLifetime_Session,
                __MIDL___MIDL_itf_mfvirtualcamera_0000_0000_0003.MFVirtualCameraAccess_CurrentUser,
                "Drone Live Kamera", "{" + Shared.CLSID_VCamNet + "}", null, 0, out var kamera).ThrowOnError();
            _kamera = new ComObject<IMFVirtualCamera>(kamera);
            kamera.SetString(Shared.Bildpfad, _bildpfad).ThrowOnError();
            kamera.SetUINT32(Shared.Bildbreite, (uint)breite).ThrowOnError();
            kamera.SetUINT32(Shared.Bildhoehe, (uint)hoehe).ThrowOnError();
            kamera.Start(null).ThrowOnError();
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Löscht Bilddateien abgebrochener Sitzungen, die weder eine Anwendung noch der Frame Server geöffnet hat.</summary>
    internal static int EntferneVerwaisteBilddateien(string ordner)
    {
        int entfernt = 0;
        foreach (string datei in Directory.GetFiles(ordner, "*.bgra"))
        {
            try
            {
                // Ein einfaches Löschen gelänge wegen FileShare.Delete auch bei laufenden Sitzungen anderer Benutzer.
                // Ohne Freigabe öffnet Windows die Datei dagegen nur, wenn sie kein anderer Prozess verwendet.
                using (new FileStream(datei, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                entfernt++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return entfernt;
    }
    public void Schreibe(byte[] bild) { lock (_sperre) { if (!_beendet) _austausch.Schreibe(bild); } }
    public void KeinSignal() { lock (_sperre) { if (!_beendet) _austausch.KeinSignal(); } }
    public void Dispose()
    {
        lock (_sperre)
        {
            if (_beendet) return;
            _beendet = true;
            _austausch.KeinSignal();
            _kamera?.Object.Stop();
            _kamera?.Object.Remove();
            _kamera?.Object.Shutdown();
            _kamera?.Dispose();
            _austausch.Dispose();
            if (_gestartet) MFFunctions.MFShutdown();
            try { File.Delete(_bildpfad); } catch (IOException) { }
        }
    }
}

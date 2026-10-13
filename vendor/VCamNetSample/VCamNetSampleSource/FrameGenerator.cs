using System;
using System.Runtime.InteropServices;
using DirectN;
using DroneLive.Core;

namespace VCamNetSampleSource;

/// <summary>Übergibt das jüngste Rohbild oder ein zeitunabhängiges Kein-Signal-Bild an Media Foundation.</summary>
public sealed class FrameGenerator : IDisposable
{
    private readonly FrameExchange _austausch;
    private readonly byte[] _bild;
    private readonly byte[] _keinSignal;
    private readonly int _breite;
    private readonly int _hoehe;
    public ulong FrameCount { get; private set; }
    public FrameGenerator(string pfad, int breite, int hoehe)
    {
        _breite = breite;
        _hoehe = hoehe;
        _austausch = new FrameExchange(pfad, breite, hoehe, false);
        _bild = new byte[breite * hoehe * 4];
        _keinSignal = Bildwerkzeuge.KeinSignal(breite, hoehe);
    }
    public HRESULT EnsureRenderTarget(uint breite, uint hoehe) => HRESULTS.S_OK;
    public IComObject<IMFSample> Generate(IComObject<IMFSample> sample, Guid format)
    {
        var daten = _austausch.Lese(_bild) ? _bild : _keinSignal;
        if (format == MFConstants.MFVideoFormat_NV12) daten = Bildwerkzeuge.NachNv12(daten, _breite, _hoehe);
        sample.RemoveAllBuffers();
        using var buffer = MFFunctions.MFCreateMemoryBuffer((uint)daten.Length);
        buffer.WithLock((zeiger, _, _) => Marshal.Copy(daten, 0, zeiger, daten.Length));
        buffer.SetCurrentLength((uint)daten.Length);
        sample.AddBuffer(buffer);
        FrameCount++;
        return sample;
    }
    public void Dispose() => _austausch.Dispose();
}

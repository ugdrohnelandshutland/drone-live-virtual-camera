using System.Buffers.Binary;

namespace DroneLive.Core;

public sealed record Videopaket(byte[] AnnexB, long Dts, long Pts, bool Schluesselbild, byte[] Konfiguration);

/// <summary>Liest vollständige FLV-Tags und wandelt AVC-Längenpräfixe in Annex B um.</summary>
public sealed class FlvParser
{
    private byte[]? _konfiguration;
    private byte[] _parameter = [];
    private int _laengenBytes;
    private bool _warteAufSchluesselbild = true;
    public event Action<string>? Hinweis;
    public void VerwerfeBisSchluesselbild() => _warteAufSchluesselbild = true;

    public IReadOnlyList<Videopaket> Lese(ReadOnlySpan<byte> nachricht)
    {
        var pakete = new List<Videopaket>();
        if (nachricht.StartsWith("FLV"u8))
        {
            if (nachricht.Length < 13 || nachricht[3] != 1) throw new InvalidDataException("Ungültiger FLV-Dateikopf.");
            var kopf = BinaryPrimitives.ReadUInt32BigEndian(nachricht[5..]);
            if (kopf < 9 || kopf > nachricht.Length - 4 || BinaryPrimitives.ReadUInt32BigEndian(nachricht[(int)kopf..]) != 0)
                throw new InvalidDataException("Ungültige FLV-Kopflänge oder PreviousTagSize0.");
            nachricht = nachricht[((int)kopf + 4)..];
        }
        while (!nachricht.IsEmpty)
        {
            if (nachricht.Length < 15) throw new InvalidDataException("Unvollständiger FLV-Tag.");
            int groesse = Zahl24(nachricht[1..]);
            if (groesse > nachricht.Length - 15) throw new InvalidDataException("FLV-Tag überschreitet die Nachricht.");
            if (Zahl24(nachricht[8..]) != 0 || BinaryPrimitives.ReadUInt32BigEndian(nachricht[(11 + groesse)..]) != 11 + groesse)
                throw new InvalidDataException("Ungültige FLV-Tagstruktur.");
            long dts = (uint)(Zahl24(nachricht[4..]) | nachricht[7] << 24);
            if ((nachricht[0] & 31) == 9) LeseVideo(nachricht.Slice(11, groesse), dts, pakete);
            nachricht = nachricht[(15 + groesse)..];
        }
        return pakete;
    }

    private void LeseVideo(ReadOnlySpan<byte> daten, long dts, List<Videopaket> pakete)
    {
        if (daten.IsEmpty) throw new InvalidDataException("Leerer Video-Tag.");
        if ((daten[0] & 0x80) != 0 || (daten[0] & 15) != 7)
            throw new NotSupportedException("Dieses Videoformat wird nicht unterstützt. Bitte H.264/AVC an der Drohne einstellen (kein H.265/Enhanced RTMP).");
        if (daten.Length < 5) throw new InvalidDataException("Unvollständiger AVC-Tag.");
        int komposition = Zahl24(daten[2..]);
        if ((komposition & 0x800000) != 0) komposition |= unchecked((int)0xff000000);
        var nutzdaten = daten[5..];
        if (daten[1] == 0)
        {
            if (_konfiguration != null && nutzdaten.SequenceEqual(_konfiguration)) return;
            var (laengenBytes, parameter) = LeseKonfiguration(nutzdaten);
            _konfiguration = nutzdaten.ToArray();
            _laengenBytes = laengenBytes;
            _parameter = parameter;
            _warteAufSchluesselbild = true;
            Hinweis?.Invoke("Neue H.264-Decoderkonfiguration empfangen.");
            return;
        }
        if (daten[1] == 2) { _warteAufSchluesselbild = true; return; }
        if (daten[1] != 1) throw new InvalidDataException("Unbekannter AVC-Pakettyp.");
        if (_konfiguration == null) return;
        bool schluesselbild = (daten[0] >> 4) == 1;
        if (_warteAufSchluesselbild && !schluesselbild) return;
        using var ausgabe = new MemoryStream();
        if (schluesselbild) ausgabe.Write(_parameter);
        while (!nutzdaten.IsEmpty)
        {
            if (nutzdaten.Length < _laengenBytes) throw new InvalidDataException("Unvollständige NAL-Länge.");
            uint laenge = 0;
            for (int i = 0; i < _laengenBytes; i++) laenge = (laenge << 8) | nutzdaten[i];
            nutzdaten = nutzdaten[_laengenBytes..];
            if (laenge == 0 || laenge > nutzdaten.Length) throw new InvalidDataException("Ungültige NAL-Länge.");
            ausgabe.Write([0, 0, 0, 1]);
            ausgabe.Write(nutzdaten[..(int)laenge]);
            nutzdaten = nutzdaten[(int)laenge..];
        }
        _warteAufSchluesselbild = false;
        pakete.Add(new(ausgabe.ToArray(), dts, dts + komposition, schluesselbild, _konfiguration));
    }

    private static (int, byte[]) LeseKonfiguration(ReadOnlySpan<byte> daten)
    {
        if (daten.Length < 7 || daten[0] != 1) throw new InvalidDataException("Ungültiger AVC-Konfigurationsdatensatz.");
        int laengenBytes = (daten[4] & 3) + 1;
        if (laengenBytes == 3) throw new InvalidDataException("Reservierte AVC-NAL-Längenfeldgröße.");
        int anzahl = daten[5] & 31;
        if (anzahl == 0) throw new InvalidDataException("SPS fehlt.");
        int position = 6;
        using var parameter = new MemoryStream();
        for (int gruppe = 0; gruppe < 2; gruppe++)
        {
            for (int i = 0; i < anzahl; i++)
            {
                if (daten.Length - position < 2) throw new InvalidDataException("Abgeschnittene SPS/PPS-Länge.");
                int laenge = BinaryPrimitives.ReadUInt16BigEndian(daten[position..]);
                position += 2;
                if (laenge == 0 || laenge > daten.Length - position) throw new InvalidDataException("Abgeschnittene SPS/PPS-Daten.");
                parameter.Write([0, 0, 0, 1]);
                parameter.Write(daten.Slice(position, laenge));
                position += laenge;
            }
            if (gruppe == 0)
            {
                if (position == daten.Length || daten[position] == 0) throw new InvalidDataException("PPS fehlt.");
                anzahl = daten[position++];
            }
        }
        return (laengenBytes, parameter.ToArray());
    }
    private static int Zahl24(ReadOnlySpan<byte> bytes) => bytes[0] << 16 | bytes[1] << 8 | bytes[2];
}

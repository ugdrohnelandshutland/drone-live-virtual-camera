# Architektur

Gewählte Kamera- und Decoderlösung sowie Fehler- und Latenzverhalten. Die Begründungen stehen in
den Architekturentscheidungen unter `docs/decisions/`.

## Gewählte Kamera- und Decoderlösung

Eine eigene Implementierung von COM-Aktivierung, Media-Source, Stream-Deskriptoren und
Frame-Server-Verhalten würde den Schwerpunkt dieser Version wesentlich erweitern.
Deshalb wird die vom Auftrag zugelassene MIT-Komponente
[VCamNetSample](https://github.com/smourier/VCamNetSample) als Quellcode verwendet,
mit eigener CLSID und ersetztem Bildgenerator. Herkunft, festgelegter Commit und Änderungen
stehen in `vendor/VCamNetSample/PROVENANCE.md`; die Original-Lizenz bleibt enthalten.

[MFCreateVirtualCamera](https://learn.microsoft.com/en-us/windows/win32/api/mfvirtualcamera/nf-mfvirtualcamera-mfcreatevirtualcamera)
registriert die Kamera für den aktuellen Benutzer mit Sitzungslebensdauer. Die Anwendung erzeugt sie
beim Programmstart und entfernt sie erst beim Beenden; Start und Stopp einer Übertragung schreiben nur
Bilder beziehungsweise „Kein Signal“ in den bestehenden Bildspeicher. Die COM-Quelle wird
einmalig systemweit registriert. Benutzerdefinierte Kameraattribute übermitteln Bilddateipfad
und Ausgabegröße an die Aktivierung. Die Media-Source meldet fest 1280 × 720 oder 1920 × 1080,
30 Bilder/s sowie RGB32 und NV12. Sie bedient Sample-Anforderungen auf einer eigenen Aufgabe.

FFmpeg wird als getrenntes Programm betrieben; native FFmpeg-Bindings werden nicht benötigt.
`-hwaccel auto` bevorzugt verfügbare Hardwaredecoder und lässt FFmpeg auf Software zurückfallen.
Für problematische Treiber lässt sich `Hardwaredekodierung` in der Konfiguration ausschalten. Die Installation auf dem
Test-PC meldete D3D11VA im FFmpeg-Build; ein tatsächlicher Hardwarepfad wurde nicht nachgewiesen.
Skalierung, BGRA-Übergabe und RGB32/NV12-Kameraausgabe arbeiten im Hauptspeicher. Diese Version
hat damit keine durchgehend auf der GPU verbleibende Pipeline.

```text
ClientWebSocket → begrenzter Empfangskanal → FLV-Parser → AVCC/Annex B
               → FFmpeg/H.264 → BGRA mit Skalierung → veröffentlichter Bildplatz aus drei Plätzen
               → COM-Media-Source → RGB32/NV12 → Teams
```

Das Seitenverhältnis bleibt erhalten; bei abweichenden Formaten entstehen schwarze Ränder.
Der Parser ermittelt DTS und PTS mit vorzeichenbehafteter CompositionTime. Der H.264-Decoder
ordnet Bilder anhand des Bitstreams; der Annex-B-Prozesskanal überträgt die ursprünglichen
FLV-Zeitstempel nicht an FFmpeg. Die virtuelle Kamera besitzt eine eigene 30-Hz-Ausgabeuhr und
zeigt jeweils das jüngste Bild. Dies ist eine Live-Vorschau ohne Audio-Synchronisierung oder
Aufzeichnung mit originalen Zeitstempeln.

## Fehler- und Latenzverhalten

- 4000 und Netzwerkfehler: sofort „Kein Signal“, Wiederholung nach 1, 2, 4, 8, maximal 10 Sekunden.
  Erfolgreich dekodierte Bilder setzen den Backoff zurück; nach einer Verbindung mit Bildern
  wird daher wieder ab 1 Sekunde gewartet.
- 4001: Decoder verwerfen und unmittelbar neu verbinden.
- 4004: Fehler anzeigen und Wiederverbindung beenden.
- 4008: Rückstand verwerfen, Decoder erneuern, nach 200 ms neu verbinden.
- Geänderter Sequence-Header: alte Decoderausgaben verwerfen und ab dem nächsten Keyframe einen
  neuen Decoder erstellen. Identische Header lösen keinen Neustart aus; der erste Header einer
  Verbindung gilt nicht als Änderung.
- Ungültige oder zu lange NAL-Längen (z. B. Nullbytes am Paketende) beenden die Verbindung nicht:
  gültige NALs davor werden ausgegeben, sonst wird das Paket verworfen und auf das nächste Keyframe
  gewartet. Der erste Vorfall je Verbindung wird protokolliert; FLV-Tagfehler bleiben harte Fehler.
- H.265/Enhanced RTMP und andere Codecs: verständlicher Fehler und Protokollmeldung.
- Maximal 16 MiB je WebSocket-Nachricht; der Empfangskanal hält maximal 64 Nachrichten und
  insgesamt 16 MiB. Ein Rückstand über 2,5 s löst einen neuen Einstieg ab Keyframe aus.
  Diese kurze Reserve nimmt insbesondere den zwischengespeicherten Keyframe-Rückblick auf.
- Rohbilder werden nicht aufgereiht: drei feste Bildplätze trennen laufendes Schreiben und
  veröffentlichte Bilder. Ein atomarer Zeiger gibt das vollständig geschriebene Bild frei;
  neue Leser können es sofort übernehmen, auch während der nächste Platz beschrieben wird.
  Sequenzzähler pro Platz schützen vor einem Sender, der den Leser beim Kopieren überholt.
  Die Pixel werden als Speicherblock kopiert. Jeder Leser behält zusätzlich das letzte
  konsistente Bild als kurze Reserve. Die Zeitprüfung nutzt die monotone Windows-Betriebszeit
  statt der Systemuhr; Lesen verlängert die Frischefrist nicht. Teilbilder werden nie ausgegeben.
  Eine ausdrücklich veröffentlichte Kein-Signal-Meldung invalidiert auch das gespeicherte Bild.
  Nach zwei Sekunden ohne aktualisierten Bildplatz liefert die Kameraquelle „Kein Signal“,
  auch wenn die Oberfläche hängt oder der Senderprozess ausfällt.

Die H.264-Empfangsschleife führt keine Dekodierung, UI-Aufrufe oder blockierende Kanal-Schreibvorgänge aus.
FLV-Tags und Größenfelder werden geprüft; Skripttags und nicht benötigte Tagtypen werden ignoriert.
Für die CPU-Konvertierung nach NV12 wird BT.601 mit begrenztem Wertebereich verwendet.

Der frühere Einzelplatz verursachte falsche „Kein Signal“-Einblendungen bei Kopierkonflikten.
Nur einen Leser-Cache zu ergänzen reichte bei anhaltenden Überschneidungen nicht: Nach zwei Sekunden
konnte er trotz weiterer Senderframes auslaufen. Veröffentlichte, getrennte Bildplätze beheben auch
diesen Fall. Zusätzlich wurde auf dem Test-PC eine veraltete `DroneLive.Core.dll` im früheren
separaten Installationsordner nachgewiesen, während die Vorschau bereits den neuen Build verwendete.
Die Kameraquelle wird jetzt direkt aus dem Release-Ordner registriert; die frühere Program-Files-Kopie
wurde bei der Umstellung entfernt. Die separate Kamera-Strecke wurde über den Windows Frame Server geprüft.
Das Bildspeicherprotokoll hat Version 2; Anwendung und COM-Quelle müssen gemeinsam aktualisiert werden.

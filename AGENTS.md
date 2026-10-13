# Virtuelle Kamera für den Drone Live Stream Hub

Entwickle eine .NET-Windows-Forms-Anwendung, die den Videostream einer ausgewählten Drohne vom
Drone Live Stream Hub empfängt, dekodiert und als virtuelle Webcam für beispielsweise Teams ausgibt.
Eigener Code, Kommentare, UI-Texte und Commits werden auf Deutsch verfasst. Öffentliche Windows-/
.NET-Schnittstellen und unveränderte externe Komponenten behalten ihre API-Bezeichner.
Datei-, Ordner- und Projektnamen sowie die zugehörigen Typnamen werden auf Englisch geführt.
Dokumentation, Installationsanleitung, Kommentare und Oberfläche bleiben deutsch.

## Projektstruktur

- `src/DroneLive.App`: Windows-Forms-Anwendung, Ausgabe weiterhin `DroneLive2VirtualCamera.exe`.
- `src/DroneLive.Core`: Streamverarbeitung und Bildaustausch.
- `tests/DroneLive.Tests` und `tests/fixtures`: Prüfprogramm und geprüfte API-Testdaten.
- `scripts`: Registrierung, Paketbau und Hilfswerkzeuge; `scripts/package` enthält die Paketvorlagen.
- `vendor`: externe Kameraquelle; `docs`: Dokumentation, Entscheidungen und Designentwurf.
- Nicht versioniert: `packages` (fertige ZIPs) und `artifacts/test-results` (Prüfausgaben).

## Server und API

- Host und Port sind konfigurierbar, niemals fest im Anwendungscode. HTTP/WebSocket verwendet
  typischerweise Port 8080; RTMP 1935 ist ausschließlich für den Eingang des Servers relevant.
- Zunächst keine Authentifizierung; ausschließlich Video, kein Audio und keine Server-Transkodierung.
- `GET /api/drones`: Drohnenliste. Exakte JSON-Feldnamen vor Änderungen gegen einen laufenden Hub prüfen.
  Die geprüften Antworten vom 10.10.2026 unter `tests/fixtures/` verwenden `drones`, `id`, `name`, `state`, `video.codec`.
- `GET /api/events`: SSE, Ereignis `drone-updated` für Änderungen der Drohnenzustände.
- `GET /healthz`: Erreichbarkeit. `GET /ws/live/<droneId>`: binärer WebSocket-Stream.
- HTTP-Adressen ergeben WS; HTTPS-Adressen ergeben WSS. Benutzer wählt die Drohne aus der API-Liste.

## Streamverarbeitung

- `ClientWebSocket`-Empfang in eigener Aufgabe, niemals durch Dekodierung oder UI blockieren.
- WebSocket-Fragmente bis `EndOfMessage` sammeln und den Empfangspuffer bei Bedarf vergrößern.
  Nachrichten enthalten vollständige FLV-Tags; der initiale FLV-Kopf samt PreviousTagSize0 kann separat eintreffen.
- FLV: 11-Byte-Tagkopf, Big-Endian-24-Bit-DataSize, 32-Bit-DTS mit TimestampExtended,
  anschließend Body und 4-Byte-PreviousTagSize. Video-Typ 9 verarbeiten, andere ignorieren.
- Zunächst H.264/AVC, CodecID 7: Sequence-Header (Pakettyp 0) mit SPS/PPS und NAL-Längenfeldgröße;
  NAL-Pakete (Typ 1), Ende (Typ 2). CompositionTime ist vorzeichenbehaftet, PTS = DTS + CompositionTime.
- AVCC nach Annex B konvertieren, SPS/PPS vor Keyframes einfügen. Bei neuer, abweichender Konfiguration
  den Decoder erneuern und auf ein Keyframe warten, auch ohne Verbindungsabbruch.
- Enhanced RTMP (höchstes Bit im ersten Videobyte), H.265 und andere Formate: Protokoll und verständlicher UI-Fehler.
- Schlusscodes: 4000 und Netzwerkfehler mit Backoff 1 bis maximal 10 Sekunden; 4001 Decoder verwerfen
  und sofort erneut verbinden; 4004 beenden und Fehler anzeigen; 4008 Rückstand verwerfen und erneut verbinden.
- Rückstand begrenzen; Rohbilder ersetzen, statt eine wachsende Warteschlange aufzubauen.

## Ausgabe

- Dekodierung ist zwingend: virtuelle Kameras benötigen unkomprimiertes NV12/BGRA/RGB32.
  Hardwaredekodierung bevorzugen, Software-Rückfall unterstützen.
- Windows 11 bietet MFCreateVirtualCamera mit COM-Media-Source. Alternativen prüfen und die gewählte
  Lösung dokumentieren; die Komponente https://github.com/smourier/VCamNetSample ist zugelassen.
- Ausgabegröße bleibt während der Kamerasitzung stabil (1280 × 720 oder 1920 × 1080).
  Eingangsauflösungen bei Kamerawechsel entsprechend skalieren.
- Ohne aktuelle Frames fortlaufend ein Bild mit „Kein Signal“ ausgeben, auch auf Kameraquellenseite.
- Die virtuelle Kamera wird beim Programmstart bereitgestellt und erst beim Beenden entfernt, damit
  Teams sie beim Drohnenwechsel behält. Stopp und Start einer Übertragung zeigen dazwischen nur
  „Kein Signal“; allein eine geänderte Ausgabegröße erfordert eine neue Kamerasitzung.
- Deutsche Oberfläche mit Serveradresse, Drohnenauswahl, Ausgabeauflösung und Status
  „Verbunden“ / „Kein Signal“ / „Fehler“. Die Anwendung läuft auf dem Teams-PC.
- Gestaltung nach der Hub-Testseite (Tagmodus): Schriftzug „Drohnen-Livestream“ mit Zusatz
  „für Videokonferenzen“, Symbol aus Schild und Videokamera (`BrandGraphics` in `DroneLive.Core`),
  Drohnenliste als Kacheln mit Platz für vier Einträge und Scrollen darüber hinaus.
- Fenstername „DroneLive2VirtualCamera“, kompakte feste Fenstergröße und Anwendungssymbol
  auch in EXE und Windows-Tray. Die Bedienung soll für Laien verständlich sein.
- Die Lösung heißt `DroneLive2VirtualCamera.slnx`, die Projekte heißen `DroneLive.App`,
  `DroneLive.Core` und `DroneLive.Tests`; die EXE bleibt `DroneLive2VirtualCamera.exe`. Die COM-Kameraquelle wird
  direkt aus `src/DroneLive.App/bin/Release/net10.0-windows10.0.22621.0/win-x64` registriert.
  `scripts/RegisterCamera.ps1` richtet die Leserechte für Local Service und die COM-Registrierung
  ein; `-FrameServerNeuStarten` erneuert optional den Windows Frame Server. Anwendung und
  Kameraquelle verwenden gemeinsam die Dateien dieses Release-Ordners.
- Für die Weitergabe erstellt `scripts/BuildPackage.ps1` ein ZIP unter `packages/`.
  Das enthaltene `scripts/package/Install.ps1` installiert die Anwendung nach
  `C:\Program Files\DroneLive2VirtualCamera` und registriert die COM-Kameraquelle dort.
  Bestehende `configuration.json` beibehalten; .NET 10 Desktop Runtime x64 und FFmpeg prüfen.
  Das Paket liefert FFmpeg samt Lizenzhinweisen mit; `Install.ps1` installiert es nur, wenn
  auf dem Ziel-PC kein FFmpeg gefunden wird, nach `FFmpeg\` im Installationsordner.
- Neue Auslieferungen verwenden `http://localhost:4444` aus `scripts/package/configuration.json`.
  Die Entwicklungskonfiguration `src/DroneLive.App/configuration.json` enthält die Adresse des
  Test-Hubs und ist nicht versioniert; versioniert wird nur `configuration.example.json`. Die Adresse
  des Test-Hubs nicht in versionierte Dateien schreiben. Paketbau überschreibt ausschließlich die
  Konfiguration im Paket-Zwischenordner.
- Serveradresse ausschließlich lesbar anzeigen. Server, FFmpeg-Pfad und technische Optionen
  kommen aus `configuration.json` neben der EXE; den FFmpeg-Pfad nicht im Hauptfenster anzeigen.
- Widersprüchliche Aktionen und Änderungen an Drohne oder Ausgabeformat während einer
  Übertragung sperren. Die Administrator-Konfiguration niemals durch die Oberfläche überschreiben.
- Minimieren und Schließen mit aktiver Übertragung verschieben die Anwendung in den Tray;
  der Stream läuft weiter. Schließen ohne aktive Übertragung erfordert eine Bestätigung.
  Das Tray-Menü bietet Wiederherstellen, Stopp und bestätigtes Beenden mit passenden Icons.

Überblick und Befehle stehen in `README.md`; Einrichtung (`docs/dev-setup.md`), Paket und Installation
(`docs/windows-install.md`), Architektur (`docs/architecture.md`) und Prüfungen (`docs/tests.md`) in `docs/`,
Architekturentscheidungen in `docs/decisions/` (Englisch).

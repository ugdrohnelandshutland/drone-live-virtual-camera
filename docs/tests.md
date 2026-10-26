# API-Abgleich und Prüfungen

## API-Abgleich

Am 10.10.2026 wurden `/healthz` und `/api/drones` auf einem laufenden Test-Hub abgerufen. Die anonymen Testantworten liegen unter `tests/fixtures/`.
Der Server liefert `{ "drones": [...] }` mit `id`, `name`, **`state`** und optionalem `video.codec`.
Statusänderungen über `/api/events` lösen bei `event: drone-updated` eine erneute Abfrage aus.
Der SSE-Kanal verbindet sich nach einer Unterbrechung erneut.

## Prüfprogramm

```powershell
dotnet run --project tests/DroneLive.Tests -c Release -- --ffmpeg "C:\Pfad\ffmpeg.exe"
# Optionaler Live-Test am eigenen Hub:
dotnet run --project tests/DroneLive.Tests -c Release -- --ffmpeg "C:\Pfad\ffmpeg.exe" --server "https://eigener-hub/"
# Gezielte Kamera-/Bildspeicherprüfung ohne FFmpeg oder Server:
dotnet run --project tests/DroneLive.Tests -c Release -- --bildaustausch
# Tatsächlich registrierte DLLs mit dem aktuellen Build vergleichen:
dotnet run --project tests/DroneLive.Tests -c Release -- --installation
# Optionale Prüfung der installierten Kamera über den Windows Frame Server:
dotnet run --project tests/DroneLive.Tests -c Release -- --kamera
```

Die Prüfungen benötigen den FFmpeg-Encoder `libx264` zum Erzeugen ihrer Testsignale.
Sie prüfen die realen API-Feldnamen, alle AVC-NAL-Längen, ungültige NAL-Längen (überspringen statt Abbruch), Zeitstempel, fehlerhafte Tags,
Codec-Ablehnung, Bildaustausch, Kein-Signal-Frist, RGB32/NV12-Samples, Media-Source-Ereignisse,
alle vier Schlusscodes, die Backoff-Rücksetzung nach dekodierten Bildern, eine fragmentierte
130-KiB-Nachricht und einen Auflösungswechsel innerhalb derselben Verbindung. Das Aufräumen verwaister
Bilddateien darf weder laufende Sender noch vom Frame Server gelesene Dateien entfernen. Oberflächen- und Bildvorschauen werden in `artifacts/test-results/` erzeugt.
Zusätzlich werden Konfigurationsfehler, die Übernahme alter Benutzereinstellungen, EXE-Icon,
feste Fenstergröße, größere Darstellung, Bedienungssperren und Tray-Aktionen geprüft.
Ein gezielt angehaltener Schreibvorgang mit teilweise überschriebenem Speicher reproduziert
den früheren Flackerfehler. Die Kamera-Samples müssen das letzte vollständige Bild enthalten;
Fristablauf und ausdrücklicher Signalverlust müssen weiterhin „Kein Signal“ liefern.
Parallele Schreib- und Sample-Anforderungen prüfen zusätzlich RGB32 und NV12 in HD und Full HD
auf unerwartete Kein-Signal-Bilder sowie vermischte Bildinhalte.
Zusätzliche Tests halten den Nachfolger über mehr als zwei Sekunden unvollständig, während
vollständige Bilder veröffentlicht werden, und aktivieren neue Leser während des Schreibens.
Vier zehnsekündige Dauertests prüfen HD/Full HD bei 30/60-Hz-Sendern und Kameraanforderungen
mit etwa 30 Hz. Installationsprüfungen erkennen auch gleich große, aber abweichende DLLs.
Die Oberflächenprüfungen verwenden einen echten lokal erzeugten H.264-Stream und prüfen
Schließen, Minimieren, Wiederherstellen, Stopp und bestätigtes Beenden. Eine Testkamera ersetzt
dabei die COM-Kamera, damit keine Registrierung benötigt wird; geprüft wird ihre Lebensdauer:
Bereitstellung beim Programmstart, Weiterverwendung über Stopp und erneuten Start, Neubereitstellung
nur bei geänderter Bildqualität und Entfernen erst beim Beenden. Die Prüfung `--kamera` zeigt
zusätzlich, dass derselbe geöffnete Frame-Server-Leser nach „Kein Signal“ wieder neue Bilder erhält.
Der optionale Live-Test dekodiert 15 Sekunden vom Hub und speichert ein Vorschaubild.

## Ergebnisse

Der Release-Build wurde ohne Warnungen oder Fehler abgeschlossen. 153 lokale Prüfungen wurden
am 12.10.2026 bestanden. Im separaten Live-Test wurden zuletzt 500 Frames in 15 Sekunden über WSS dekodiert;
ein echter Kamerawechsel mit Code 4001 wurde in einem früheren Live-Durchlauf ebenfalls verarbeitet.
Die Oberflächenvorschau, das skalierte Live-Testbild und „Kein Signal“ wurden visuell geprüft.
Die Kameraquelle wurde am 10.10.2026 direkt aus `src/DroneLive.App/bin/Release/net10.0-windows10.0.22621.0/win-x64`
registriert und mit dem Build abgeglichen.
Sechs zusätzliche Prüfungen über `MFEnumDeviceSources`, registrierte Geräteaktivierung und
`IMFSourceReader` bestanden: Der Windows Frame Server lieferte 290 korrekte unkomprimierte Samples
in zehn Sekunden ohne falsche Kein-Signal-Bilder. Ein anschließend ausdrücklich ausgelöster
Signalverlust lieferte das Ersatzbild. Nach dem Umbau auf eine dauerhaft bereitgestellte Kamera
bestanden am 12.10.2026 `--installation` und sieben Kameraprüfungen, mit 244 Samples in zehn Sekunden (nach der Umgestaltung)
ohne falsche Bilder; nach „Kein Signal“ erhielt derselbe geöffnete Leser das Bild der nächsten Übertragung.
Das prüft die separate COM-/Frame-Server-Strecke;
die Kameraauswahl und Darstellung in Teams bleiben als Prüfung in Teams selbst erforderlich.

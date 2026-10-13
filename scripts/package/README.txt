DroneLive2VirtualCamera - Installation für Windows x64

Voraussetzungen:
- Windows 11 ab Version 22H2.
- .NET 10 Desktop Runtime x64 (enthält die benötigte .NET-Laufzeit).
- FFmpeg mit H.264-Decoder. Ein passendes FFmpeg liegt im ZIP und wird automatisch
  installiert, wenn auf dem PC keines gefunden wird.

Installation:
1. Das ZIP vollständig in einen beliebigen Ordner entpacken.
2. Kamera-Verbraucher wie Teams schließen. Läuft DroneLive2VirtualCamera noch,
   fragt das Skript nach und beendet die Anwendung für das Update.
3. In diesem Ordner ein Terminal / eine PowerShell öffnen und ausführen:

   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1

4. Die Windows-Administratorabfrage bestätigen.
5. DroneLive2VirtualCamera über das Startmenü öffnen.

Das Skript installiert nach C:\Program Files\DroneLive2VirtualCamera, richtet
die Zugriffsrechte für Windows Frame Server und den Bildaustausch ein und
registriert VCamNetSampleSource.comhost.dll systemweit als COM-Kameraquelle.
Die Anwendung läuft danach mit normalen Benutzerrechten.

FFmpeg wird im PATH und unter C:\Program Files\FFmpeg gesucht. Wird keines
gefunden, installiert das Skript das mitgelieferte FFmpeg nach
C:\Program Files\DroneLive2VirtualCamera\FFmpeg und trägt es in configuration.json
ein; bei späteren Updates wird diese Kopie mit erneuert. Lizenz und Herkunft:
licenses\FFmpeg.txt und licenses\FFmpeg-GPLv3.txt.
Soll ein bereits vorhandenes FFmpeg an einem anderen Ort verwendet werden, bei der
Installation den Pfad mitgeben:

   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -FFmpegPfad "C:\Pfad\ffmpeg.exe"

Serveradresse und FFmpeg-Pfad stehen in configuration.json im Installationsordner.
Bei einer Neuinstallation ist http://localhost:4444 als Server voreingestellt.
Für einen Hub auf einem anderen PC die Serveradresse in dieser Datei anpassen.
Diese Datei zum Ändern mit Administratorrechten bearbeiten. Eine vorhandene
Konfiguration bleibt bei Updates erhalten. Ein ausdrücklich übergebener
-FFmpegPfad aktualisiert den Decoder-Pfad.

Update einer bestehenden Installation:
Dasselbe Skript aus einem neuen, vollständig entpackten Paket ausführen. Es
erkennt die vorhandene Installation und zeigt den bisherigen und neuen Paketstand.
- Eine laufende Anwendung wird nach Rückfrage beendet und nach dem Update wieder
  gestartet. Ohne Rückfrage: zusätzlich -AnwendungBeenden angeben.
- configuration.json bleibt unverändert; Dateien, die das neue Paket nicht mehr
  enthält, werden entfernt. Eigene Dateien im Ordner bleiben erhalten.
- Vor dem Update wird die bisherige Installation gesichert. Scheitert das Update,
  wird sie automatisch wiederhergestellt und die Kamera erneut registriert.

Nur Paket und Voraussetzungen prüfen, ohne Installation oder Administratorrechte:

   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -NurPruefen

Nach erfolgreicher Installation kann der entpackte Paketordner entfernt werden.
Der gemeinsame Bildspeicher befindet sich unter C:\ProgramData\DroneLive2VirtualCamera\Frames.
Eine vorhandene Registrierung aus einem Entwicklungsordner wird durch die
Registrierung im Installationsordner ersetzt. Der Release-Build selbst bleibt bestehen.

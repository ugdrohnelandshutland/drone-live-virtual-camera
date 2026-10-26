# DroneLive2VirtualCamera – Installationspaket für Windows

Diese Anleitung beschreibt Bau und Installation des Pakets für den Konferenz-PC. Die Kurzfassung
für Anwender liegt dem ZIP als `README.txt` bei (Vorlage: `scripts/package/README.txt`).

## Paket bauen

```powershell
.\scripts\BuildPackage.ps1
```

Erstellt `packages/DroneLive2VirtualCamera-Setup-win-x64.zip` mit Release-Dateien,
Lizenzhinweisen, Dateiprüfsummen, Installationsanleitung, einem eigenständigen PowerShell-Skript
und einem mitgelieferten FFmpeg. Das ZIP enthält `Install.ps1`, `README.txt`, `package-manifest.json`
(Format 2), `Application/`, `FFmpeg/` und `licenses/`. Als FFmpeg wird das im PATH gefundene
`ffmpeg.exe` verwendet, ersatzweise `C:\Program Files\FFmpeg\ffmpeg.exe`; mit `-FFmpegQuelle` lässt
sich eine andere Datei angeben. Der Paketbau prüft H.264-Decoder sowie die Filter `scale` und `pad`
und schreibt Version, Build-Konfiguration und Lizenzangabe nach `licenses/FFmpeg.txt`; der GPLv3-Text
liegt als `licenses/FFmpeg-GPLv3.txt` bei. Mit FFmpeg wird das Paket deutlich größer.
Beim Paketbau ersetzt die Auslieferungsvorlage ausschließlich die Paketkonfiguration mit
`http://localhost:4444`; die Entwicklungs- und Release-Konfiguration bleiben unverändert.

## Installieren

Voraussetzungen auf dem Ziel-PC: Windows 11 ab 22H2 (x64) und die .NET 10 Desktop Runtime x64.
Auf dem Ziel-PC das ZIP vollständig entpacken und im entpackten Ordner ausführen:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
# Ein vorhandenes FFmpeg außerhalb von PATH und C:\Program Files\FFmpeg ausdrücklich verwenden:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -FFmpegPfad "C:\Pfad\ffmpeg.exe"
```

Das Skript fordert bei Bedarf Administratorrechte an, prüft Windows 11 x64 ab 22H2,
.NET 10 Desktop Runtime x64, FFmpeg und die Paketdateien. Es kopiert die Anwendung nach
`C:\Program Files\DroneLive2VirtualCamera`, richtet die Leserechte und den Bildaustausch ein,
registriert die COM-Kameraquelle dort und legt einen Startmenü-Eintrag an.
Kamera-Verbraucher vor der Installation schließen.

## Updates

Dasselbe `Install.ps1` aus einem neuen Paket erkennt eine vorhandene Installation und
meldet bisherigen und neuen Paketstand (`Paketstand` im Manifest). Eine laufende Anwendung wird im
sichtbaren Aufruf nach Rückfrage beendet (oder ohne Rückfrage mit `-AnwendungBeenden`) und nach
erfolgreichem Update mit Benutzerrechten wieder gestartet. Der Frame Server wird für den Austausch
der Kamera-DLLs angehalten und danach wieder gestartet. Vor dem Kopieren sichert das Skript die
bisherige Installation; scheitert Kopieren oder Registrierung, wird sie wiederhergestellt und erneut
registriert. Gesperrte Dateien werden mehrfach versucht. `installation.json` im Installationsordner
merkt sich Paketstand und gelieferte Dateien, damit spätere Updates nicht mehr gelieferte Dateien
entfernen; ohne diese Liste (ältere Installationen) wird nichts gelöscht. Dabei bleibt
die vorhandene `configuration.json` erhalten; ein ausdrücklicher `-FFmpegPfad` aktualisiert
den Decoder-Pfad. Der Paketordner kann danach entfernt werden.

## FFmpeg

FFmpeg wird zuerst am eingetragenen `FFmpegPfad`, dann im PATH und unter `C:\Program Files\FFmpeg`
gesucht. Fehlt es, installiert das Skript das mitgelieferte FFmpeg nach
`C:\Program Files\DroneLive2VirtualCamera\FFmpeg` und trägt `FFmpeg\ffmpeg.exe` relativ zur EXE ein;
Updates erneuern diese Kopie (eine identische, womöglich laufende bleibt unberührt), ohne die Konfiguration zu verändern. Ein ausdrücklich angegebener,
aber fehlender `-FFmpegPfad` bricht die Installation ab.
Die .NET Runtime bleibt eine separate Voraussetzung. `-NurPruefen` prüft das entpackte
Paket und die Voraussetzungen ohne Systemänderung. Der Release-Build bleibt zusätzlich erhalten;
seine Kamera-Registrierung wird beim Installieren durch die Registrierung im Zielordner ersetzt.

## Paket prüfen

`scripts/TestPackage.ps1` prüft das tatsächliche ZIP mit Windows PowerShell 5.1:
Dateivollständigkeit, Kopieren, bytegenauen Konfigurationserhalt bei Updates, ausdrückliche
Decoderänderung sowie Ablehnung beschädigter Dateien und ungültiger Manifestpfade. Für FFmpeg bildet
sie einen PC ohne FFmpeg nach und prüft Installation, Lauffähigkeit und relativen Eintrag des
mitgelieferten FFmpeg, dessen Erneuerung bei Updates und die Erkennung eines ausgetauschten FFmpeg.
Die Prüfung installiert nur in einen Testordner und verändert die COM-Registrierung nicht.
Zusätzlich prüft sie den Auslieferungsstandard `http://localhost:4444` und dass die Entwicklungskonfiguration im Release-Ordner unverändert bleibt.
Am 12.10.2026 bestanden 39 Paketprüfungen unter Windows PowerShell 5.1 (Paket mit FFmpeg 6.1.1, 60,8 MB), darunter Dateiliste, Aufräumen bei Updates, Wiederherstellung aus der Sicherung und gesperrte Dateien. Die COM-Registrierung und das Beenden der Anwendung prüft sie nicht.

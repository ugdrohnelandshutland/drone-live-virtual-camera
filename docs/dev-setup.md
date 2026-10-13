# Entwicklungsumgebung

Einrichtung, Konfiguration und Bedienung auf dem Entwicklungs- oder Test-PC. Installation über das
Paket: `docs/windows-install.md`.

## Projektstruktur

```text
src/DroneLive.App/       Windows-Forms-Anwendung
src/DroneLive.Core/      Streamverarbeitung und Bildaustausch
tests/DroneLive.Tests/   Prüfprogramm
tests/fixtures/          Geprüfte API-Testdaten
scripts/                 Registrierung, Paketbau und Hilfswerkzeuge
scripts/package/         Install.ps1 und Vorlagen für die Auslieferung
vendor/                  Externe Kameraquelle mit Lizenz und Herkunft
docs/                    Dokumentation, Architekturentscheidungen und Designentwurf
packages/                Fertige Installationspakete (nicht im Repository)
artifacts/test-results/  Prüfausgaben und Vorschaubilder (nicht im Repository)
```

Datei-, Ordner-, Projekt- und zugehörige Typnamen sind englisch. Dokumentation,
Installationsanleitung, Kommentare und Oberfläche bleiben deutsch.

## Voraussetzungen und Start

- Windows 11 ab 22H2, x64. Die Vorschau nutzt ebenfalls das Windows-Zielframework dieses Projekts.
- .NET 10 Desktop Runtime x64; zum Bauen das .NET 10 SDK.
- `ffmpeg.exe` mit H.264-Decoder, Skalierungsfilter und BGRA-Rohbildausgabe. Für die Entwicklung
  separat installieren; das Installationspaket liefert es mit und installiert es bei Bedarf.
  Das Programm kann im PATH liegen oder über `FFmpegPfad` in `configuration.json` festgelegt werden.
- Netzwerkzugriff auf den HTTP/WebSocket-Port des Hubs. Für einen HTTPS-Hub wird WSS verwendet.

Im Projektverzeichnis:

```powershell
dotnet build DroneLive2VirtualCamera.slnx -c Release
```

Die Lösung ordnet die COM-Kameraquelle ausdrücklich der Projektplattform `x64` zu. Damit werden
`Debug|x64` und `Release|x64` verwendet, auch wenn in Visual Studio die Lösungsplattform „Any CPU“
ausgewählt ist. Die Anwendung und die Kameraquelle verwenden die x64-Laufzeit.
Die Projekte heißen `DroneLive.App`, `DroneLive.Core` und `DroneLive.Tests`.
Die Lösung bleibt `DroneLive2VirtualCamera.slnx`, die EXE heißt weiterhin `DroneLive2VirtualCamera.exe`.

Einmalig in einer **PowerShell als Administrator**:

```powershell
.\scripts\RegisterCamera.ps1
# Aktualisierung, nachdem Anwendung und Kamera-Verbraucher geschlossen wurden:
.\scripts\RegisterCamera.ps1 -FrameServerNeuStarten
```

Das Skript registriert `VCamNetSampleSource.comhost.dll` direkt aus
`src\DroneLive.App\bin\Release\net10.0-windows10.0.22621.0\win-x64`.
Es setzt die nötigen Leserechte für Windows Frame Server (Local Service) auf diesem Release-Ordner
und richtet unter `%ProgramData%\DroneLive2VirtualCamera\Frames` den gemeinsamen Bildspeicher ein.
Beim Kamerastart entfernt die Anwendung dort Bilddateien abgebrochener Sitzungen, sofern weder eine
laufende Anwendung noch der Windows Frame Server sie geöffnet hat.
Anwendung und Kameraquelle verwenden dieselben DLLs in diesem Release-Ordner. Der Ordner muss
an diesem Ort bestehen bleiben; bei einem Umzug wird das Installationsskript erneut ausgeführt.
Vor einem erneuten Build oder einer Aktualisierung Anwendung und Kamera-Verbraucher schließen.
Solange der Windows Frame Server die Kameraquelle geladen hat, sind ihre DLLs gesperrt; dann in
einer Administrator-PowerShell `Stop-Service FrameServer -Force` ausführen.
Mit `-FrameServerNeuStarten` wird ein laufender Windows Frame Server für die Registrierung
angehalten und anschließend wieder gestartet, damit alte geladene DLLs nicht weiterverwendet werden.
Die Anwendung prüft vor dem Kamerastart den tatsächlich registrierten COM-Ordner und die
SHA256-Prüfsummen von Kern, Kameraquelle und COM-Host. Ein abweichender Stand wird mit einer
Installationsmeldung im Statusbereich abgelehnt; eine neue Vorschau-EXE allein aktualisiert die COM-Quelle nicht.

## Konfiguration

Die Datei **`configuration.json` neben der EXE im Release-Ordner** wird vor dem Start durch den Administrator eingerichtet:

```json
{
  "Serveradresse": "http://localhost:4444",
  "FFmpegPfad": "ffmpeg.exe",
  "Hardwaredekodierung": true,
  "VirtuelleKamera": true
}
```

Für die Entwicklung `src/DroneLive.App/configuration.example.json` nach
`src/DroneLive.App/configuration.json` kopieren und dort die Adresse des eigenen Test-Hubs
eintragen. Diese Datei ist nicht versioniert (`.gitignore`); fehlt sie, kopiert der Build die
Vorlage. Das Installationspaket verwendet dagegen immer `http://localhost:4444`
aus `scripts/package/configuration.json`. Für den Betrieb die tatsächliche Adresse mit Protokoll
und gegebenenfalls Port in der installierten Konfiguration eintragen. `FFmpegPfad` erlaubt einen vollständigen Pfad
(in JSON etwa `C:\\Program Files\\FFmpeg\\ffmpeg.exe`), Umgebungsvariablen, einen Pfad relativ zur EXE
oder den Dateinamen eines Programms im PATH. Änderungen erfordern einen Neustart. Fehlende oder
ungültige Konfiguration wird mit einer verständlichen Fehlermeldung abgelehnt. Die Oberfläche
ändert diese Datei nicht. Für Vorschautests ohne COM-Installation `VirtuelleKamera` auf `false` setzen.

## Bedienung

Danach die Anwendung direkt aus dem Release-Ordner starten:

```powershell
.\src\DroneLive.App\bin\Release\net10.0-windows10.0.22621.0\win-x64\DroneLive2VirtualCamera.exe
```

1. Die virtuelle Kamera wird beim Programmstart bereitgestellt; die Drohnenliste wird automatisch geladen.
2. In Teams „Drone Live Kamera“ (ggf. mit Windows-Zusatz) als Kamera auswählen. Bis zum Start zeigt sie „Kein Signal“.
3. Eine Drohne mit Live-Signal und die Bildqualität „HD“ oder „Full HD“ wählen und „Übertragung starten“ anklicken.

Die Kamera bleibt vom Programmstart bis zum Beenden der Anwendung bestehen. Für einen Drohnenwechsel
„Übertragung stoppen“, eine andere Drohne wählen und erneut starten: Teams behält die Kamera und
zeigt dazwischen „Kein Signal“. Nur eine geänderte Bildqualität stellt die Kamera in der neuen festen
Größe neu bereit; Teams muss sie danach gegebenenfalls erneut auswählen. Lässt sich die Kamera beim
Programmstart nicht bereitstellen, zeigt der Status den Installationshinweis, und „Übertragung starten“
versucht die Bereitstellung erneut. Ohne neue Frames erhält
ein angeschlossener Verbraucher weiterhin „Kein Signal“.

**Gestaltung:** Fenster, Symbol und Kein-Signal-Bild folgen dem Design der Hub-Testseite
(Tagmodus; Farben, Segoe UI, Kacheln mit Nummernfeld und Statusfarben), siehe ADR 0005. Das Hauptfenster hat fest
780 × 548 Pixel Innenfläche bei 100 % Skalierung: Kopfzeile mit Schriftzug „Drohnen-Livestream“,
Zusatz „für Videokonferenzen“ und nur lesbarer Serveradresse; links die Drohnenliste als Kacheln
mit Platz für vier Einträge (weitere per Scrollen), darunter „HD“/„Full HD“ und eine Schaltfläche,
die zwischen „Übertragung starten“ und „Übertragung stoppen“ wechselt; rechts die Vorschau mit
Leiste für Nummer, Drohne, Ausgabegröße und Status. Das Symbol – Schild der Testseite mit
Videokamera und Live-Punkt – zeichnet `BrandGraphics` in `DroneLive.Core`; daraus entstehen auch
Tray-Symbol mit Statuspunkt und Kein-Signal-Bild. `scripts/GenerateIcons.ps1` erzeugt damit über
`dotnet run scripts/GenerateIcons.cs` die Datei `src/DroneLive.App/Resources/Application.ico`.
Der Designentwurf liegt als PDF unter `docs/design/`. Der FFmpeg-Pfad und technische Optionen
sind ausschließlich in der Konfiguration zugänglich. Protokollmeldungen stehen unter „Diagnose“.
Offline-Drohnen und nicht unterstützte Videoformate lassen sich nicht starten. Während Start,
Stopp und Übertragung sind widersprüchliche Aktionen sowie Änderungen an Drohne und Qualität
gesperrt. Der Kamerawechsel an der Drohne darf während der Übertragung erfolgen.

**Fenster und Windows-Tray:** Minimieren verschiebt die Anwendung in den Infobereich der Taskleiste.
Schließen bei laufender Übertragung tut dasselbe; der Stream und die virtuelle Kamera bleiben aktiv,
auch während einer automatischen Wiederverbindung. Doppelklick auf das Symbol oder
„Fenster öffnen“ im Tray-Menü stellt das Fenster wieder her. Dort sind auch „Übertragung stoppen“
und „Beenden …“ verfügbar. Schließen ohne aktive Übertragung erfordert eine Bestätigung, die darauf
hinweist, dass die virtuelle Kamera danach für die Videokonferenz entfällt.
„Beenden …“ fragt ebenfalls nach und weist bei aktiver Übertragung auf deren Ende hin.
Die Standardantwort ist jeweils „Nein“. Fenster, EXE und Tray verwenden dasselbe Symbol; in Tray
und Taskleiste zeigt ein Punkt den Zustand (grün Verbunden, gelb Kein Signal, rot Fehler, ohne Punkt bereit).
Schaltflächen, Status und Tray-Menü besitzen zusätzliche Bedienungssymbole.

Drohnenauswahl und Ausgabeauflösung werden unter `%LocalAppData%\DroneLive2VirtualCamera\settings.json`
gespeichert. Vorhandene Auswahlwerte aus dem bisherigen Einstellungsort werden beim ersten Start
übernommen. Alte Server-/Decoder-Einträge in dieser Benutzerdatei werden ignoriert.
Serveradresse und Port sind nicht fest im Anwendungscode hinterlegt. Die Protokollanzeige ist
in ihrer Größe begrenzt. Zur COM-Abmeldung dient `scripts/UnregisterCamera.ps1` als Administrator.

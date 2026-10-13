# Drone Live Virtual Camera

Windows-Anwendung (`DroneLive2VirtualCamera.exe`), die das Livevideo einer ausgewählten Drohne vom
[Drone Live Stream Hub](https://github.com/ugdrohnelandshutland/drone-live-stream-hub) per FLV
über WebSocket empfängt, dekodiert und als virtuelle Kamera für Teams und andere
Videokonferenzen bereitstellt – auf dem Konferenz-PC im lokalen Einsatznetz.

Ziele und Vorgaben stehen in `AGENTS.md`, Einrichtung, Architektur und Prüfungen in `docs/`,
Architekturentscheidungen in `docs/decisions/` (Englisch).

## Entwicklung

Voraussetzungen: Windows 11 ab 22H2 (x64), .NET 10 SDK und ffmpeg mit `libx264` im PATH (nur für
Entwicklung und Tests). Siehe `docs/dev-setup.md`.

```powershell
dotnet build DroneLive2VirtualCamera.slnx -c Release
.\scripts\RegisterCamera.ps1          # einmalig als Administrator: Kameraquelle aus dem Release-Ordner registrieren
.\src\DroneLive.App\bin\Release\net10.0-windows10.0.22621.0\win-x64\DroneLive2VirtualCamera.exe
# Serveradresse und FFmpeg-Pfad: configuration.json neben der EXE (Vorlage: src/DroneLive.App/configuration.example.json)
dotnet run --project tests/DroneLive.Tests -c Release                    # Prüfprogramm (146 Prüfungen, braucht ffmpeg)
dotnet run --project tests/DroneLive.Tests -c Release -- --bildaustausch   # nur Bildspeicher und Kameraquelle
dotnet run --project tests/DroneLive.Tests -c Release -- --installation    # registrierte DLLs = aktueller Build?
dotnet run --project tests/DroneLive.Tests -c Release -- --kamera          # virtuelle Kamera über den Windows Frame Server
.\scripts\GenerateIcons.ps1           # Application.ico aus BrandGraphics neu erzeugen
```

Solange der Windows Frame Server die Kameraquelle geladen hat, sperrt er ihre DLLs; vor einem
Release-Build Anwendung und Kamera-Verbraucher schließen und als Administrator
`Stop-Service FrameServer -Force` ausführen. Weitere Prüfbefehle: `docs/tests.md`.

## Windows-Paket (Konferenz-PC)

```powershell
.\scripts\BuildPackage.ps1            # → packages/DroneLive2VirtualCamera-Setup-win-x64.zip
.\scripts\TestPackage.ps1             # prüft das ZIP unter Windows PowerShell 5.1
```

Das ZIP enthält Anwendung, Kameraquelle, ein FFmpeg samt Lizenzhinweisen und `Install.ps1`, das
auch bestehende Installationen aktualisiert; auf dem Konferenz-PC muss die .NET 10 Desktop
Runtime x64 installiert sein. Installation, Updates und Betrieb: `docs/windows-install.md`;
Entwurf: ADR 0001 und 0004.

## Entstehung

Dieses Projekt wurde mit Unterstützung von Claude (Anthropic) über Claude Code entwickelt. Die
Projektvorgaben dafür stehen in `AGENTS.md`.

## Lizenz

MIT, siehe `LICENSE`. Die Kameraquelle basiert auf
[VCamNetSample](https://github.com/smourier/VCamNetSample) (MIT, `vendor/VCamNetSample/`); die
verwendeten Bibliotheken DirectNCore und C#/WinRT stehen unter MIT. Das Installationspaket
liefert FFmpeg unter GPLv3 mit, siehe `scripts/package/licenses/`.

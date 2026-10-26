#Requires -Version 5.1
[CmdletBinding()]
param([string]$PaketZip)
$ErrorActionPreference = 'Stop'
if (-not $PaketZip) { $PaketZip = Join-Path $PSScriptRoot '../packages/DroneLive2VirtualCamera-Setup-win-x64.zip' }
$projektWurzel = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$pruefWurzel = Join-Path $projektWurzel ('artifacts/test-results/package-' + [Guid]::NewGuid().ToString('N'))
$entpackt = Join-Path $pruefWurzel 'extracted'
$ziel = Join-Path $pruefWurzel 'installed'
$bestanden = 0
function Pruefe {
    param([bool]$Bedingung, [string]$Text)
    if (-not $Bedingung) { throw "Paketprüfung fehlgeschlagen: $Text" }
    $script:bestanden++
    Write-Host "OK: $Text"
}
function Erwarte-Fehler {
    param([scriptblock]$Aktion, [string]$Text)
    $abgelehnt = $false
    try { & $Aktion | Out-Null } catch { $abgelehnt = $true }
    Pruefe $abgelehnt $Text
}
New-Item -ItemType Directory -Path $entpackt -Force | Out-Null
Expand-Archive -LiteralPath $PaketZip -DestinationPath $entpackt
. (Join-Path $entpackt 'Install.ps1')
$manifest = Pruefe-Paket $entpackt
Pruefe (@($manifest.Dateien).Count -ge 10) 'Das tatsächliche ZIP enthält Anwendung, Abhängigkeiten, Kameraquelle und Konfiguration'
Pruefe ((Test-Path -LiteralPath (Join-Path $entpackt 'README.txt')) -and (Test-Path -LiteralPath (Join-Path $entpackt 'licenses/VCamNetSample.txt'))) 'Installationsanleitung und Lizenzhinweise sind enthalten'
$ffmpegHinweis = Get-Content -LiteralPath (Join-Path $entpackt 'licenses/FFmpeg.txt') -Raw -Encoding UTF8
Pruefe ((Test-Path -LiteralPath (Join-Path $entpackt 'FFmpeg/ffmpeg.exe')) -and $ffmpegHinweis.Contains($manifest.FFmpeg.Version) -and
    (Get-Content -LiteralPath (Join-Path $entpackt 'licenses/FFmpeg-GPLv3.txt') -TotalCount 2)[1].Trim() -eq 'Version 3, 29 June 2007') 'Das ZIP enthält FFmpeg mit Versions-, Build- und GPLv3-Lizenzhinweis'
$registrierungsPfad = 'Registry::HKEY_CLASSES_ROOT\CLSID\{c7e7fc74-f2c4-4b11-b47d-01642a9ce7a6}\InprocServer32'
$vorher = (Get-Item -LiteralPath $registrierungsPfad).GetValue('')
Installiere-Paket $entpackt $ziel '' $true
Pruefe ((Get-Item -LiteralPath $registrierungsPfad).GetValue('') -eq $vorher -and -not (Test-Path -LiteralPath $ziel)) 'NurPruefen verändert weder COM-Registrierung noch Installationsordner'
$decoderProgramm = Ermittle-FFmpeg '' (Join-Path $entpackt 'Application/configuration.json')
Kopiere-Anwendung $entpackt $ziel $manifest $decoderProgramm $false
$konfigPfad = Join-Path $ziel 'configuration.json'
$neu = Get-Content -LiteralPath $konfigPfad -Raw -Encoding UTF8 | ConvertFrom-Json
Pruefe ($neu.FFmpegPfad -eq $decoderProgramm -and -not (Test-Path -LiteralPath (Join-Path $ziel 'FFmpeg'))) 'Neue Installation übernimmt ein vorhandenes FFmpeg und installiert das mitgelieferte nicht'
Pruefe ($neu.Serveradresse -eq 'http://localhost:4444') 'Neuinstallation verwendet standardmäßig http://localhost:4444 statt des Testservers'
# Die eigene Entwicklungskonfiguration ist nicht versioniert; ohne sie verwendet der Build die Vorlage.
$entwicklungsKonfiguration = Join-Path $projektWurzel 'src/DroneLive.App/configuration.json'
if (-not (Test-Path -LiteralPath $entwicklungsKonfiguration)) { $entwicklungsKonfiguration = Join-Path $projektWurzel 'src/DroneLive.App/configuration.example.json' }
$releaseKonfiguration = Join-Path $projektWurzel 'src/DroneLive.App/bin/Release/net10.0-windows10.0.22621.0/win-x64/configuration.json'
Pruefe ((Get-FileHash -LiteralPath $entwicklungsKonfiguration).Hash -eq (Get-FileHash -LiteralPath $releaseKonfiguration).Hash) 'Paketbau lässt die Entwicklungskonfiguration im Release-Ordner unverändert'
foreach ($datei in $manifest.Dateien) {
    if ($datei.Name -eq 'configuration.json') { continue }
    Pruefe ((Get-FileHash -LiteralPath (Join-Path $ziel $datei.Name)).Hash -eq $datei.SHA256) ("Installierte Datei stimmt mit dem ZIP überein: $($datei.Name)")
}
$neu.Serveradresse = 'https://konfigurationspruefung.invalid:8443/'
$neu.FFmpegPfad = 'ffmpeg.exe'
$neu.Hardwaredekodierung = $false
$neu | ConvertTo-Json | Set-Content -LiteralPath $konfigPfad -Encoding UTF8
$konfigVorher = (Get-FileHash -LiteralPath $konfigPfad).Hash
Kopiere-Anwendung $entpackt $ziel $manifest $decoderProgramm $false
Pruefe ((Get-FileHash -LiteralPath $konfigPfad).Hash -eq $konfigVorher) 'Update erhält die vorhandene Konfiguration vollständig und bytegenau'
Kopiere-Anwendung $entpackt $ziel $manifest $decoderProgramm $true
$aktualisiert = Get-Content -LiteralPath $konfigPfad -Raw -Encoding UTF8 | ConvertFrom-Json
Pruefe ($aktualisiert.Serveradresse -eq $neu.Serveradresse -and -not $aktualisiert.Hardwaredekodierung -and $aktualisiert.FFmpegPfad -eq $decoderProgramm) 'Expliziter FFmpeg-Pfad aktualisiert nur den Decoder und erhält die anderen Einstellungen'
Erwarte-Fehler { Ermittle-FFmpeg (Join-Path $pruefWurzel 'nicht-vorhanden.exe') $konfigPfad } 'Ein ausdrücklich ungültiger Decoder-Pfad wird vor der Installation abgelehnt'
# Updates: Dateiliste, Aufräumen früherer Versionen, Sicherung und gesperrte Dateien.
$liste = Lese-Installation $ziel
Pruefe ($liste -and $liste.Paketstand -eq $manifest.Paketstand -and @($liste.Dateien).Count -eq @($manifest.Dateien).Count) 'Installation merkt sich Paketstand und gelieferte Dateien'
'Datei einer früheren Version' | Set-Content -LiteralPath (Join-Path $ziel 'Veraltet.dll')
'Eigene Notiz des Administrators' | Set-Content -LiteralPath (Join-Path $ziel 'notizen.txt')
$liste.Dateien = @($liste.Dateien) + 'Veraltet.dll'
$liste | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ziel 'installation.json') -Encoding UTF8
$konfigVorUpdate = (Get-FileHash -LiteralPath $konfigPfad).Hash
Kopiere-Anwendung $entpackt $ziel $manifest $decoderProgramm $false
Pruefe (-not (Test-Path -LiteralPath (Join-Path $ziel 'Veraltet.dll')) -and (Test-Path -LiteralPath (Join-Path $ziel 'notizen.txt')) -and
    (Get-FileHash -LiteralPath $konfigPfad).Hash -eq $konfigVorUpdate) 'Update entfernt nicht mehr gelieferte Dateien und lässt fremde Dateien und die Konfiguration stehen'
$kern = Join-Path $ziel 'DroneLive.Core.dll'
$kernVorher = (Get-FileHash -LiteralPath $kern).Hash
$sicherung = Sichere-Installation $ziel
try {
    # Abgebrochenes Update nachstellen: eine Datei halb ersetzt, die EXE fehlt.
    [IO.File]::AppendAllText($kern, 'halb ersetzt')
    Remove-Item -LiteralPath (Join-Path $ziel 'DroneLive2VirtualCamera.exe')
    Stelle-Installation-Wieder-Her $ziel $sicherung
    Pruefe ((Get-FileHash -LiteralPath $kern).Hash -eq $kernVorher -and (Test-Path -LiteralPath (Join-Path $ziel 'DroneLive2VirtualCamera.exe')) -and
        (Get-FileHash -LiteralPath $konfigPfad).Hash -eq $konfigVorUpdate) 'Fehlgeschlagenes Update wird aus der Sicherung vollständig wiederhergestellt'
}
finally { Remove-Item -LiteralPath $sicherung -Recurse -Force }
$sperre = [IO.File]::Open($kern, 'Open', 'Read', 'Read')
try {
    $meldung = ''
    try { Kopiere-Datei (Join-Path $entpackt 'Application/DroneLive.Core.dll') $kern } catch { $meldung = $_.Exception.Message }
    Pruefe ($meldung -like '*gesperrt*') 'Eine gesperrte Datei bricht nach mehreren Versuchen mit verständlicher Meldung ab'
}
finally { $sperre.Dispose() }
# Ziel-PC ohne FFmpeg nachbilden: weder PATH noch Standardorte enthalten eines.
$gespeicherterPfad = $env:PATH
try {
    $env:PATH = Join-Path $env:SystemRoot 'System32'
    $ohneFFmpeg = Ermittle-FFmpeg '' (Join-Path $entpackt 'Application/configuration.json') -Standardorte @()
}
finally { $env:PATH = $gespeicherterPfad }
Pruefe ($null -eq $ohneFFmpeg) 'Ein Ziel-PC ohne FFmpeg wird erkannt'
$zielMitFFmpeg = Join-Path $pruefWurzel 'installed-ffmpeg'
Kopiere-Anwendung $entpackt $zielMitFFmpeg $manifest $ohneFFmpeg $false
$mitgeliefertesFFmpeg = Join-Path $zielMitFFmpeg 'FFmpeg\ffmpeg.exe'
$konfigMitFFmpeg = Join-Path $zielMitFFmpeg 'configuration.json'
$eingetragen = (Get-Content -LiteralPath $konfigMitFFmpeg -Raw -Encoding UTF8 | ConvertFrom-Json).FFmpegPfad
Pruefe ($eingetragen -eq 'FFmpeg\ffmpeg.exe' -and (Get-FileHash -LiteralPath $mitgeliefertesFFmpeg).Hash -eq $manifest.FFmpeg.SHA256) 'Fehlendes FFmpeg wird aus dem Paket installiert und relativ zur Anwendung eingetragen'
$ffmpegAusgabe = @(& $mitgeliefertesFFmpeg -hide_banner -version)
Pruefe ($LASTEXITCODE -eq 0 -and $ffmpegAusgabe[0] -like "ffmpeg version $($manifest.FFmpeg.Version)*") 'Das installierte FFmpeg ist lauffähig und entspricht der Paketversion'
$konfigMitFFmpegVorher = (Get-FileHash -LiteralPath $konfigMitFFmpeg).Hash
$erkannt = Ermittle-FFmpeg '' $konfigMitFFmpeg -Standardorte @()
Pruefe ($erkannt -eq [IO.Path]::GetFullPath($mitgeliefertesFFmpeg)) 'Update erkennt das zuvor mitgelieferte FFmpeg'
Kopiere-Anwendung $entpackt $zielMitFFmpeg $manifest $erkannt $false
Pruefe ((Get-FileHash -LiteralPath $konfigMitFFmpeg).Hash -eq $konfigMitFFmpegVorher -and (Get-FileHash -LiteralPath $mitgeliefertesFFmpeg).Hash -eq $manifest.FFmpeg.SHA256) 'Update erneuert das mitgelieferte FFmpeg und erhält die Konfiguration bytegenau'
$sperre = [IO.File]::Open($mitgeliefertesFFmpeg, 'Open', 'Read', 'Read')
try { Kopiere-Anwendung $entpackt $zielMitFFmpeg $manifest $erkannt $false; $gesperrtOk = $true }
catch { $gesperrtOk = $false }
finally { $sperre.Dispose() }
Pruefe $gesperrtOk 'Update ersetzt ein identisches, gesperrtes (laufendes) mitgeliefertes FFmpeg nicht und bricht nicht ab'
'abweichend' | Set-Content -LiteralPath $mitgeliefertesFFmpeg
Kopiere-Anwendung $entpackt $zielMitFFmpeg $manifest $erkannt $false
Pruefe ((Get-FileHash -LiteralPath $mitgeliefertesFFmpeg).Hash -eq $manifest.FFmpeg.SHA256) 'Update ersetzt ein abweichendes mitgeliefertes FFmpeg durch die Paketversion'
$fremderOrdner = Join-Path $pruefWurzel 'other-files'
New-Item -ItemType Directory -Path $fremderOrdner | Out-Null
'Bestehende andere Anwendung' | Set-Content -LiteralPath (Join-Path $fremderOrdner 'other.txt')
Erwarte-Fehler { Kopiere-Anwendung $entpackt $fremderOrdner $manifest $decoderProgramm $false } 'Ein bestehender fremder Ordner wird nicht überschrieben'
Pruefe ((Get-Content -LiteralPath (Join-Path $fremderOrdner 'other.txt')) -eq 'Bestehende andere Anwendung') 'Fremde Dateien bleiben erhalten'
$manifPfad = Join-Path $entpackt 'package-manifest.json'
$manifestText = [IO.File]::ReadAllText($manifPfad)
$falscherHash = $manifestText | ConvertFrom-Json
$falscherHash.FFmpeg.SHA256 = '0' * 64
$falscherHash | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifPfad -Encoding UTF8
Erwarte-Fehler { Pruefe-Paket $entpackt } 'Ein beschädigtes oder ausgetauschtes mitgeliefertes FFmpeg wird erkannt'
[IO.File]::WriteAllText($manifPfad, $manifestText, [Text.UTF8Encoding]::new($true))
$manifest.Dateien[0].Name = '../outside.dll'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifPfad -Encoding UTF8
Erwarte-Fehler { Pruefe-Paket $entpackt } 'Pfadmanipulation im Paketmanifest wird abgelehnt'
[IO.File]::WriteAllText($manifPfad, $manifestText, [Text.UTF8Encoding]::new($true))
$beschaedigt = Join-Path $entpackt 'Application/DroneLive.Core.dll'
[IO.File]::AppendAllText($beschaedigt, 'Beschaedigung fuer die Paketpruefung')
Erwarte-Fehler { Pruefe-Paket $entpackt } 'Eine beschädigte DLL wird durch die Paket-Prüfsumme erkannt'
Remove-Item -LiteralPath $beschaedigt
Erwarte-Fehler { Pruefe-Paket $entpackt } 'Eine fehlende DLL wird vor der Installation erkannt'
Write-Host "$bestanden Paketprüfungen bestanden. Testdateien: $pruefWurzel"

#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ZielOrdner = (Join-Path $env:ProgramW6432 'DroneLive2VirtualCamera'),
    [string]$FFmpegPfad,
    [switch]$NurPruefen,
    [switch]$AnwendungBeenden,
    [string]$ProtokollPfad
)
$ErrorActionPreference = 'Stop'
$script:Programmname = 'DroneLive2VirtualCamera'
# Merkt sich, welche Dateien diese Installation angelegt hat; Updates entfernen damit nicht mehr gelieferte Dateien.
$script:Installationsliste = 'installation.json'

function Kopiere-Datei {
    param([string]$Quelle, [string]$Ziel)
    # Virenscanner oder ein gerade beendeter Prozess können Dateien kurz sperren; deshalb mehrere Versuche.
    for ($versuch = 1; ; $versuch++) {
        try { Copy-Item -LiteralPath $Quelle -Destination $Ziel -Force; return }
        catch {
            if ($versuch -ge 5) { throw "Datei ist gesperrt und kann nicht ersetzt werden: $Ziel. Bitte Anwendung und Kamera-Verbraucher wie Teams schließen und erneut versuchen." }
            Start-Sleep -Seconds 1
        }
    }
}

function Lese-Installation {
    param([string]$Ziel)
    $pfad = Join-Path $Ziel $script:Installationsliste
    if (-not (Test-Path -LiteralPath $pfad -PathType Leaf)) { return $null }
    try { return Get-Content -LiteralPath $pfad -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return $null }
}

function Beende-Anwendung {
    # Liefert $true, wenn die Anwendung lief und beendet wurde.
    $laufend = @(Get-Process -Name $script:Programmname -ErrorAction SilentlyContinue)
    if ($laufend.Count -eq 0) { return $false }
    # Das Fenster liegt oft unsichtbar im Tray; ein geordnetes Schließen würde zudem nachfragen. Die virtuelle Kamera endet mit dem Prozess.
    $laufend | Stop-Process -Force -ErrorAction Stop
    $laufend | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
    if (Get-Process -Name $script:Programmname -ErrorAction SilentlyContinue) { throw 'Die Anwendung konnte nicht beendet werden.' }
    return $true
}

function Sichere-Installation {
    param([string]$Ziel)
    if (-not (Test-Path -LiteralPath (Join-Path $Ziel "$($script:Programmname).exe"))) { return $null }
    $sicherung = Join-Path ([IO.Path]::GetTempPath()) ("$($script:Programmname)-Sicherung-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $sicherung -Force | Out-Null
    Get-ChildItem -LiteralPath $Ziel -Force | Copy-Item -Destination $sicherung -Recurse -Force
    return $sicherung
}

function Stelle-Installation-Wieder-Her {
    param([string]$Ziel, [string]$Sicherung)
    Get-ChildItem -LiteralPath $Ziel -Force | Remove-Item -Recurse -Force
    Get-ChildItem -LiteralPath $Sicherung -Force | Copy-Item -Destination $Ziel -Recurse -Force
}

function Pruefe-Paket {
    param([string]$PaketOrdner)
    $anwendung = [IO.Path]::GetFullPath((Join-Path $PaketOrdner 'Application'))
    $manifestPfad = Join-Path $PaketOrdner 'package-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPfad -PathType Leaf)) { throw 'package-manifest.json fehlt. Das ZIP zuerst vollständig entpacken.' }
    $manifest = Get-Content -LiteralPath $manifestPfad -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.Produkt -ne 'DroneLive2VirtualCamera' -or $manifest.Format -ne 2 -or @($manifest.Dateien).Count -eq 0 -or
        -not $manifest.FFmpeg -or $manifest.FFmpeg.Name -ne 'ffmpeg.exe') {
        throw 'Das Paketmanifest ist ungültig.'
    }
    foreach ($lizenz in @('VCamNetSample.txt', 'FFmpeg.txt', 'FFmpeg-GPLv3.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PaketOrdner "licenses/$lizenz") -PathType Leaf)) { throw 'Die mitgelieferten Lizenzhinweise fehlen.' }
    }
    # Das mitgelieferte FFmpeg liegt getrennt von den Anwendungsdateien und wird nur bei Bedarf installiert.
    $ffmpeg = Join-Path $PaketOrdner 'FFmpeg/ffmpeg.exe'
    if (-not (Test-Path -LiteralPath $ffmpeg -PathType Leaf)) { throw 'Das mitgelieferte FFmpeg fehlt im Paket.' }
    if ((Get-FileHash -LiteralPath $ffmpeg -Algorithm SHA256).Hash -ne $manifest.FFmpeg.SHA256) { throw 'Das mitgelieferte FFmpeg ist beschädigt.' }
    $namen = @{}
    foreach ($datei in $manifest.Dateien) {
        # Das Paket enthält nur Dateien unmittelbar im Anwendungsordner, keine beliebigen Zielpfade.
        if ([string]::IsNullOrWhiteSpace($datei.Name) -or [IO.Path]::GetFileName($datei.Name) -ne $datei.Name -or
            $datei.Name.Contains('/') -or $datei.Name.Contains('\') -or $datei.Name.Contains(':') -or $namen.ContainsKey($datei.Name)) {
            throw "Ungültiger oder doppelter Dateiname im Paket: $($datei.Name)"
        }
        $namen[$datei.Name] = $true
        $pfad = Join-Path $anwendung $datei.Name
        if (-not (Test-Path -LiteralPath $pfad -PathType Leaf)) { throw "Paketdatei fehlt: $($datei.Name)" }
        if ((Get-FileHash -LiteralPath $pfad -Algorithm SHA256).Hash -ne $datei.SHA256) { throw "Paketdatei beschädigt: $($datei.Name)" }
    }
    foreach ($name in @('DroneLive2VirtualCamera.exe', 'DroneLive2VirtualCamera.dll', 'DroneLive2VirtualCamera.deps.json',
        'DroneLive2VirtualCamera.runtimeconfig.json', 'configuration.json', 'DroneLive.Core.dll',
        'VCamNetSampleSource.dll', 'VCamNetSampleSource.comhost.dll', 'VCamNetSampleSource.deps.json', 'VCamNetSampleSource.runtimeconfig.json')) {
        if (-not $namen.ContainsKey($name)) { throw "Erforderliche Paketdatei fehlt im Manifest: $name" }
    }
    return $manifest
}

function Finde-Programm {
    param([string]$Pfad, [string]$Basis)
    $Pfad = [Environment]::ExpandEnvironmentVariables($Pfad)
    if (-not [IO.Path]::IsPathRooted($Pfad)) { $Pfad = Join-Path $Basis $Pfad }
    if (Test-Path -LiteralPath $Pfad -PathType Leaf) { return [IO.Path]::GetFullPath($Pfad) }
    return $null
}

# Liefert ein vorhandenes FFmpeg oder $null; dann installiert Kopiere-Anwendung das mitgelieferte.
function Ermittle-FFmpeg {
    param([string]$Vorgabe, [string]$Konfiguration,
        [string[]]$Standardorte = @((Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'FFmpeg/ffmpeg.exe'),
            (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'FFmpeg/bin/ffmpeg.exe')))
    $einstellung = Get-Content -LiteralPath $Konfiguration -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($einstellung.Serveradresse) -or [string]::IsNullOrWhiteSpace($einstellung.FFmpegPfad)) {
        throw "Die Konfiguration ist unvollständig: $Konfiguration"
    }
    $basis = Split-Path $Konfiguration -Parent
    if (-not [string]::IsNullOrWhiteSpace($Vorgabe)) {
        # Ein ausdrücklich angegebener Pfad muss stimmen; hier wird nicht still auf das mitgelieferte FFmpeg ausgewichen.
        $pfad = Finde-Programm $Vorgabe $basis
        if (-not $pfad) { throw "Das angegebene FFmpeg wurde nicht gefunden: $Vorgabe" }
        return $pfad
    }
    $kandidaten = @($einstellung.FFmpegPfad)
    $befehl = Get-Command -Name ffmpeg.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($befehl) { $kandidaten += $befehl.Source }
    $kandidaten += $Standardorte
    foreach ($kandidat in $kandidaten) {
        $pfad = Finde-Programm $kandidat $basis
        if ($pfad) { return $pfad }
    }
    return $null
}

function Verwendet-MitgeliefertesFFmpeg {
    param([string]$Decoder, [string]$Ziel)
    if ([string]::IsNullOrWhiteSpace($Decoder)) { return $true }
    $verwaltet = Join-Path ([IO.Path]::GetFullPath($Ziel)) 'FFmpeg\ffmpeg.exe'
    return [string]::Equals([IO.Path]::GetFullPath($Decoder), $verwaltet, [StringComparison]::OrdinalIgnoreCase)
}

function Pruefe-Voraussetzungen {
    $architektur = $env:PROCESSOR_ARCHITECTURE
    if ($env:PROCESSOR_ARCHITEW6432) { $architektur = $env:PROCESSOR_ARCHITEW6432 }
    if ($architektur -ne 'AMD64') { throw 'Dieses Paket benötigt Windows x64.' }
    $windows = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    if ([int]$windows.CurrentBuildNumber -lt 22621) { throw 'Windows 11 ab Version 22H2 wird benötigt.' }
    $dotnet = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'dotnet/dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw '.NET 10 Desktop Runtime x64 fehlt. Bitte installieren und dieses Skript erneut ausführen.' }
    $laufzeiten = & $dotnet --list-runtimes
    if ($LASTEXITCODE -ne 0 -or -not ($laufzeiten -match '^Microsoft.WindowsDesktop.App 10\.0\.') -or
        -not ($laufzeiten -match '^Microsoft.NETCore.App 10\.0\.')) {
        throw '.NET 10 Desktop Runtime x64 fehlt. Bitte installieren und dieses Skript erneut ausführen.'
    }
}

function Kopiere-Anwendung {
    param([string]$PaketOrdner, [string]$Ziel, $Manifest, [string]$Decoder, [bool]$DecoderExplizit)
    $Ziel = [IO.Path]::GetFullPath($Ziel)
    $wurzel = [IO.Path]::GetPathRoot($Ziel)
    if ($Ziel.TrimEnd('\') -eq $wurzel.TrimEnd('\') -or
        $Ziel.TrimEnd('\') -eq ([Environment]::GetFolderPath('ProgramFiles')).TrimEnd('\') -or
        $Ziel.TrimEnd('\') -eq $env:SystemRoot.TrimEnd('\')) { throw 'Bitte einen eigenen Anwendungsordner als Installationsziel verwenden.' }
    if ((Test-Path -LiteralPath $Ziel) -and -not (Test-Path -LiteralPath (Join-Path $Ziel 'DroneLive2VirtualCamera.exe')) -and
        @(Get-ChildItem -LiteralPath $Ziel -Force).Count -gt 0) { throw "Der Zielordner enthält bereits andere Dateien: $Ziel" }
    if ((Test-Path -LiteralPath $Ziel) -and ((Get-Item -LiteralPath $Ziel).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Der Installationsordner darf keine Verzeichnisverknüpfung sein.'
    }
    $vorhandeneKonfiguration = Test-Path -LiteralPath (Join-Path $Ziel 'configuration.json')
    $bisher = Lese-Installation $Ziel
    New-Item -ItemType Directory -Path $Ziel -Force | Out-Null
    # Dateien früherer Versionen entfernen, die dieses Paket nicht mehr liefert; die Konfiguration bleibt immer erhalten.
    $neueNamen = @{}
    foreach ($datei in $Manifest.Dateien) { $neueNamen[$datei.Name.ToLowerInvariant()] = $true }
    # Ohne Dateiliste (Installationen vor diesem Stand) wird nichts gelöscht: Neben der EXE könnte ein bewusst abgelegtes FFmpeg liegen.
    $veraltet = @()
    if ($bisher -and $bisher.Dateien) { $veraltet = @($bisher.Dateien | ForEach-Object { [string]$_ }) }
    foreach ($name in $veraltet) {
        if ([string]::IsNullOrWhiteSpace($name) -or [IO.Path]::GetFileName($name) -ne $name -or $name -eq 'configuration.json' -or $neueNamen.ContainsKey($name.ToLowerInvariant())) { continue }
        $alt = Join-Path $Ziel $name
        if (Test-Path -LiteralPath $alt -PathType Leaf) { Remove-Item -LiteralPath $alt -Force }
    }
    foreach ($datei in $Manifest.Dateien) {
        $zielPfad = Join-Path $Ziel $datei.Name
        if ($datei.Name -eq 'configuration.json' -and (Test-Path -LiteralPath $zielPfad)) { continue }
        Kopiere-Datei (Join-Path (Join-Path $PaketOrdner 'Application') $datei.Name) $zielPfad
        if ((Get-FileHash -LiteralPath $zielPfad -Algorithm SHA256).Hash -ne $datei.SHA256) { throw "Kopierte Datei stimmt nicht mit dem Paket überein: $($datei.Name)" }
    }
    # Fehlt FFmpeg, wird das mitgelieferte installiert; eine so installierte Kopie wird bei Updates erneuert.
    $mitgeliefert = Verwendet-MitgeliefertesFFmpeg $Decoder $Ziel
    if ($mitgeliefert) {
        $ffmpegZiel = Join-Path $Ziel 'FFmpeg\ffmpeg.exe'
        New-Item -ItemType Directory -Path (Split-Path $ffmpegZiel -Parent) -Force | Out-Null
        # Identische Kopie nicht ersetzen: Ein laufendes FFmpeg sperrt die Datei.
        if (-not (Test-Path -LiteralPath $ffmpegZiel -PathType Leaf) -or (Get-FileHash -LiteralPath $ffmpegZiel -Algorithm SHA256).Hash -ne $Manifest.FFmpeg.SHA256) {
            Kopiere-Datei (Join-Path $PaketOrdner 'FFmpeg/ffmpeg.exe') $ffmpegZiel
        }
        if ((Get-FileHash -LiteralPath $ffmpegZiel -Algorithm SHA256).Hash -ne $Manifest.FFmpeg.SHA256) { throw 'Das kopierte FFmpeg stimmt nicht mit dem Paket überein.' }
        # Relativ zur EXE, damit der Eintrag auch nach einem Verschieben des Ordners gilt.
        $Decoder = 'FFmpeg\ffmpeg.exe'
    }
    $konfiguration = Join-Path $Ziel 'configuration.json'
    $inhalt = Get-Content -LiteralPath $konfiguration -Raw -Encoding UTF8 | ConvertFrom-Json
    # Vorhandene Einstellungen behalten; nur eine ausdrückliche Decoder-Vorgabe oder ein fehlendes FFmpeg ändern sie.
    if (($DecoderExplizit -or $mitgeliefert -or -not $vorhandeneKonfiguration) -and $inhalt.FFmpegPfad -ne $Decoder) {
        $inhalt.FFmpegPfad = $Decoder
        $inhalt | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $konfiguration -Encoding UTF8
    }
    $lizenzZiel = Join-Path $Ziel 'licenses'
    New-Item -ItemType Directory -Path $lizenzZiel -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $PaketOrdner 'licenses') -File | Copy-Item -Destination $lizenzZiel -Force
    [ordered]@{ Produkt = $script:Programmname; Paketstand = $Manifest.Paketstand; Dateien = @($Manifest.Dateien | ForEach-Object { $_.Name }) } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Ziel $script:Installationsliste) -Encoding UTF8
}

function Installiere-Paket {
    param([string]$PaketOrdner, [string]$Ziel, [string]$DecoderVorgabe, [bool]$Pruefung, [bool]$AnwendungBeenden = $false)
    $manifest = Pruefe-Paket $PaketOrdner
    Pruefe-Voraussetzungen
    $update = Test-Path -LiteralPath (Join-Path $Ziel "$($script:Programmname).exe")
    $bisher = Lese-Installation $Ziel
    $art = 'Neuinstallation'
    if ($update) {
        $vonStand = 'unbekannter Stand'
        if ($bisher -and $bisher.Paketstand) { $vonStand = "Paketstand $($bisher.Paketstand)" }
        $art = "Update der vorhandenen Installation ($vonStand -> Paketstand $($manifest.Paketstand))"
    }
    $konfiguration = Join-Path $Ziel 'configuration.json'
    if (-not (Test-Path -LiteralPath $konfiguration)) { $konfiguration = Join-Path $PaketOrdner 'Application/configuration.json' }
    $decoder = Ermittle-FFmpeg $DecoderVorgabe $konfiguration
    $ffmpegHinweis = $decoder
    if (Verwendet-MitgeliefertesFFmpeg $decoder $Ziel) {
        $ffmpegHinweis = "mitgeliefertes FFmpeg $($manifest.FFmpeg.Version) unter $(Join-Path $Ziel 'FFmpeg\ffmpeg.exe')"
    }
    if ($Pruefung) {
        Write-Host "Paket und Voraussetzungen geprüft: $(@($manifest.Dateien).Count) Dateien; FFmpeg: $ffmpegHinweis"
        Write-Host "Installationsziel: $Ziel ($art). Es wurden keine Dateien oder Registrierungen verändert."
        return
    }
    if (Get-Process -Name $script:Programmname -ErrorAction SilentlyContinue) {
        if (-not $AnwendungBeenden) { throw 'Die Anwendung läuft noch. Bitte über das Tray-Menü beenden oder Install.ps1 mit -AnwendungBeenden ausführen.' }
        $null = Beende-Anwendung
    }
    Write-Host $art
    $frameServerWiederStarten = $false
    $sicherung = $null
    try {
        # Der Frame Server hält die Kamera-DLLs geladen; ohne Anhalten ließen sie sich nicht ersetzen.
        $dienst = Get-Service -Name FrameServer -ErrorAction SilentlyContinue
        if ($dienst -and $dienst.Status -eq 'Running') {
            Stop-Service -Name FrameServer -Force
            $frameServerWiederStarten = $true
        }
        $sicherung = Sichere-Installation $Ziel
        try {
            Kopiere-Anwendung $PaketOrdner $Ziel $manifest $decoder (-not [string]::IsNullOrWhiteSpace($DecoderVorgabe))
            Registriere-Kamera $Ziel
        }
        catch {
            $fehler = $_
            if ($sicherung) {
                # Halb aktualisierte Installationen vermeiden: alten Stand zurückspielen und dessen Kamera erneut registrieren.
                Write-Host 'Update fehlgeschlagen; die bisherige Installation wird wiederhergestellt.'
                try { Stelle-Installation-Wieder-Her $Ziel $sicherung; Registriere-Kamera $Ziel; Write-Host 'Bisherige Installation wiederhergestellt.' }
                catch { Write-Host ("Wiederherstellung fehlgeschlagen: " + $_.Exception.Message + " Sicherung: $sicherung"); $sicherung = $null }
            }
            elseif (Test-Path -LiteralPath $Ziel) { Remove-Item -LiteralPath $Ziel -Recurse -Force -ErrorAction SilentlyContinue }
            throw $fehler
        }
        Write-Host "Installation abgeschlossen: $Ziel"
        Write-Host "FFmpeg: $ffmpegHinweis"
        Write-Host 'DroneLive2VirtualCamera kann jetzt über das Startmenü gestartet werden.'
        Write-Host "Server und FFmpeg werden in $(Join-Path $Ziel 'configuration.json') eingestellt."
    }
    finally {
        if ($sicherung -and (Test-Path -LiteralPath $sicherung)) { Remove-Item -LiteralPath $sicherung -Recurse -Force -ErrorAction SilentlyContinue }
        if ($frameServerWiederStarten) { Start-Service -Name FrameServer }
    }
}

function Registriere-Kamera {
    param([string]$Ziel)
    $Ziel = [IO.Path]::GetFullPath($Ziel)
    & icacls.exe $Ziel /grant '*S-1-5-19:(OI)(CI)RX' /T | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Leserechte für den Windows Frame Server konnten nicht gesetzt werden.' }
    $bildOrdner = Join-Path $env:ProgramData 'DroneLive2VirtualCamera/Frames'
    New-Item -ItemType Directory -Path $bildOrdner -Force | Out-Null
    & icacls.exe $bildOrdner /grant '*S-1-5-11:(OI)(CI)M' '*S-1-5-19:(OI)(CI)RX' /T | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Zugriffsrechte für den Bildaustausch konnten nicht gesetzt werden.' }
    $comHost = Join-Path $Ziel 'VCamNetSampleSource.comhost.dll'
    $prozess = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/regsvr32.exe') -ArgumentList @('/s', ('"' + $comHost + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($prozess.ExitCode -ne 0) { throw "COM-Registrierung fehlgeschlagen (Code $($prozess.ExitCode))." }
    $registriert = (Get-Item -LiteralPath 'Registry::HKEY_CLASSES_ROOT\CLSID\{c7e7fc74-f2c4-4b11-b47d-01642a9ce7a6}\InprocServer32').GetValue('')
    if (-not [string]::Equals($registriert, $comHost, [StringComparison]::OrdinalIgnoreCase)) { throw 'Die COM-Registrierung zeigt nicht auf den Installationsordner.' }
    $verknuepfung = Join-Path ([Environment]::GetFolderPath('CommonStartMenu')) 'Programs/DroneLive2VirtualCamera.lnk'
    $shell = New-Object -ComObject WScript.Shell
    try {
        $link = $shell.CreateShortcut($verknuepfung)
        $link.TargetPath = Join-Path $Ziel 'DroneLive2VirtualCamera.exe'
        $link.WorkingDirectory = $Ziel
        $link.IconLocation = $link.TargetPath + ',0'
        $link.Save()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)
    }
    finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
}

# Dot-Sourcing lädt nur die Funktionen, beispielsweise für die Paketprüfungen.
if ($MyInvocation.InvocationName -ne '.') {
    $transkriptAktiv = $false
    try {
        $ZielOrdner = [IO.Path]::GetFullPath($ZielOrdner)
        $identitaet = [Security.Principal.WindowsIdentity]::GetCurrent()
        $administrator = (New-Object Security.Principal.WindowsPrincipal($identitaet)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        $beenden = $AnwendungBeenden.IsPresent
        $neuStarten = $false
        # Nur im sichtbaren Aufruf nachfragen; der Aufruf mit Administratorrechten läuft ohne Fenster.
        if (-not $NurPruefen -and -not $ProtokollPfad -and (Get-Process -Name $script:Programmname -ErrorAction SilentlyContinue)) {
            if (-not $beenden) {
                $antwort = Read-Host 'DroneLive2VirtualCamera läuft noch und wird für das Update beendet; eine laufende Übertragung wird getrennt. Fortfahren? (J/N)'
                if ($antwort -notmatch '^[JjYy]') { throw 'Abgebrochen. Die Anwendung läuft weiter; bitte später erneut installieren.' }
            }
            $beenden = $true
            $neuStarten = $true
            # Prozesse anderer Benutzer beendet anschließend der Aufruf mit Administratorrechten.
            try { $null = Beende-Anwendung } catch { }
        }
        if (-not $NurPruefen -and (-not $administrator -or -not [Environment]::Is64BitProcess)) {
            $protokoll = Join-Path ([IO.Path]::GetTempPath()) ('DroneLive2VirtualCamera-Setup-' + [Guid]::NewGuid().ToString('N') + '.log')
            $powershell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
            if (-not [Environment]::Is64BitProcess) { $powershell = Join-Path $env:SystemRoot 'Sysnative/WindowsPowerShell/v1.0/powershell.exe' }
            $argumente = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'), '-ZielOrdner', ('"' + $ZielOrdner + '"'), '-ProtokollPfad', ('"' + $protokoll + '"'))
            if ($FFmpegPfad) { $argumente += @('-FFmpegPfad', ('"' + $FFmpegPfad + '"')) }
            if ($beenden) { $argumente += '-AnwendungBeenden' }
            Write-Host 'Für Kopieren und COM-Registrierung bitte die Windows-Administratorabfrage bestätigen.'
            $prozess = Start-Process -FilePath $powershell -Verb RunAs -ArgumentList $argumente -WindowStyle Hidden -Wait -PassThru
            if (Test-Path -LiteralPath $protokoll) { Get-Content -LiteralPath $protokoll -Encoding UTF8 | Write-Host }
            if ($prozess.ExitCode -ne 0) { throw "Installation fehlgeschlagen. Protokoll: $protokoll" }
            # Der sichtbare Aufruf läuft mit Benutzerrechten; nur so startet die Anwendung nicht als Administrator.
            if ($neuStarten) {
                Start-Process -FilePath (Join-Path $ZielOrdner "$($script:Programmname).exe") -WorkingDirectory $ZielOrdner
                Write-Host 'DroneLive2VirtualCamera wurde wieder gestartet.'
            }
        }
        else {
            if ($ProtokollPfad) { Start-Transcript -Path $ProtokollPfad -Force | Out-Null; $transkriptAktiv = $true }
            Installiere-Paket $PSScriptRoot $ZielOrdner $FFmpegPfad $NurPruefen.IsPresent $beenden
            if ($neuStarten) { Write-Host 'Die Anwendung wurde für das Update beendet. Bitte über das Startmenü ohne Administratorrechte neu starten.' }
        }
    }
    catch {
        Write-Host ("Installation abgebrochen: " + $_.Exception.Message) -ForegroundColor Red
        # Eine für das Update beendete Anwendung mit der wiederhergestellten Version erneut starten.
        $exe = Join-Path $ZielOrdner "$($script:Programmname).exe"
        if ($neuStarten -and -not $administrator -and (Test-Path -LiteralPath $exe)) {
            Start-Process -FilePath $exe -WorkingDirectory $ZielOrdner -ErrorAction SilentlyContinue
        }
        exit 1
    }
    finally { if ($transkriptAktiv) { Stop-Transcript -ErrorAction SilentlyContinue | Out-Null } }
}

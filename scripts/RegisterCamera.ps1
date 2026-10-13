#Requires -RunAsAdministrator
param(
    [string]$BuildOrdner = (Join-Path $PSScriptRoot '../src/DroneLive.App/bin/Release/net10.0-windows10.0.22621.0/win-x64'),
    [switch]$FrameServerNeuStarten
)
$ErrorActionPreference = 'Stop'
$quelle = (Resolve-Path -LiteralPath $BuildOrdner).Path
$comHost = Join-Path $quelle 'VCamNetSampleSource.comhost.dll'
if (-not (Test-Path -LiteralPath $comHost)) { throw 'COM-Host fehlt. Zuerst den Release-Build erstellen.' }
if (-not (Test-Path -LiteralPath (Join-Path $quelle 'DroneLive2VirtualCamera.exe'))) { throw 'DroneLive2VirtualCamera.exe fehlt. Zuerst den Release-Build erstellen.' }
if (-not (Test-Path -LiteralPath (Join-Path $quelle 'configuration.json'))) { throw 'configuration.json fehlt im Build.' }
if ((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber -lt 22621) {
    throw 'Windows 11 ab Version 22H2 wird benötigt.'
}
$frameServerWiederStarten = $false
if ($FrameServerNeuStarten) {
    $frameServer = Get-Service -Name 'FrameServer' -ErrorAction SilentlyContinue
    if ($frameServer -and $frameServer.Status -eq 'Running') {
        # Beim Aktualisieren dürfen Kamera-Verbraucher nicht mehr auf alte COM-DLLs zugreifen.
        Stop-Service -Name 'FrameServer' -Force -ErrorAction Stop
        $frameServerWiederStarten = $true
    }
}
try {
# Die Ausgabe bleibt im Build-Ordner; der Windows Frame Server liest dieselben Dateien wie die Anwendung.
& icacls.exe $quelle /grant '*S-1-5-19:(OI)(CI)RX' /T | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Leserechte für den Frame Server konnten nicht gesetzt werden.' }
$bildOrdner = Join-Path $env:ProgramData 'DroneLive2VirtualCamera/Frames'
New-Item -ItemType Directory -Path $bildOrdner -Force | Out-Null
& icacls.exe $bildOrdner /grant '*S-1-5-11:(OI)(CI)M' '*S-1-5-19:(OI)(CI)RX' /T | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Zugriffsrechte für den Bildaustausch konnten nicht gesetzt werden.' }
$prozess = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/regsvr32.exe') -ArgumentList @('/s', ('"' + $comHost + '"')) -Wait -PassThru -WindowStyle Hidden
if ($prozess.ExitCode -ne 0) { throw "COM-Registrierung fehlgeschlagen (Code $($prozess.ExitCode))." }
$registrierteQuelle = (Get-Item -LiteralPath 'Registry::HKEY_CLASSES_ROOT\CLSID\{c7e7fc74-f2c4-4b11-b47d-01642a9ce7a6}\InprocServer32').GetValue('')
if (-not [string]::Equals($registrierteQuelle, $comHost, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Die Kameraquelle wurde nicht aus dem Build-Ordner registriert: $registrierteQuelle"
}
Write-Host "Kameraquelle im Release-Ordner eingerichtet. Anwendung starten: $(Join-Path $quelle 'DroneLive2VirtualCamera.exe')"
}
finally {
    if ($frameServerWiederStarten) { Start-Service -Name 'FrameServer' -ErrorAction Stop }
}

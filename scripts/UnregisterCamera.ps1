#Requires -RunAsAdministrator
param([string]$BuildOrdner = (Join-Path $PSScriptRoot '../src/DroneLive.App/bin/Release/net10.0-windows10.0.22621.0/win-x64'))
$ErrorActionPreference = 'Stop'
$comHost = Join-Path (Resolve-Path -LiteralPath $BuildOrdner).Path 'VCamNetSampleSource.comhost.dll'
if (-not (Test-Path -LiteralPath $comHost)) { throw 'Die registrierte Kameraquelle wurde nicht gefunden.' }
$prozess = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/regsvr32.exe') -ArgumentList @('/u', '/s', ('"' + $comHost + '"')) -Wait -PassThru -WindowStyle Hidden
if ($prozess.ExitCode -ne 0) { throw "COM-Abmeldung fehlgeschlagen (Code $($prozess.ExitCode))." }
Write-Host 'Kameraquelle abgemeldet. Installationsdateien und Einstellungen bleiben erhalten.'

#Requires -Version 5.1
# Erzeugt src/DroneLive.App/Resources/Application.ico aus der gemeinsamen Zeichnung in DroneLive.Core.
$ErrorActionPreference = 'Stop'
$ziel = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/DroneLive.App/Resources/Application.ico'))
& dotnet run (Join-Path $PSScriptRoot 'GenerateIcons.cs') -- $ziel
if ($LASTEXITCODE -ne 0) { throw 'Das Anwendungssymbol konnte nicht erzeugt werden.' }

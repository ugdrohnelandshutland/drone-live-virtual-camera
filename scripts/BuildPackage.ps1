#Requires -Version 5.1
[CmdletBinding()]
param([switch]$OhneBuild, [string]$FFmpegQuelle)
$ErrorActionPreference = 'Stop'
$projektWurzel = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseOrdner = Join-Path $projektWurzel 'src/DroneLive.App/bin/Release/net10.0-windows10.0.22621.0/win-x64'
# Das Paket liefert FFmpeg mit; Install.ps1 installiert es nur, wenn auf dem Ziel-PC keins gefunden wird.
if (-not $FFmpegQuelle) {
    $befehl = Get-Command -Name ffmpeg.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($befehl) { $FFmpegQuelle = $befehl.Source } else { $FFmpegQuelle = Join-Path $env:ProgramFiles 'FFmpeg/ffmpeg.exe' }
}
if (-not (Test-Path -LiteralPath $FFmpegQuelle -PathType Leaf)) { throw "FFmpeg für das Paket wurde nicht gefunden: $FFmpegQuelle. Pfad mit -FFmpegQuelle angeben." }
$FFmpegQuelle = (Resolve-Path -LiteralPath $FFmpegQuelle).Path
$ffmpegVersionsausgabe = @(& $FFmpegQuelle -hide_banner -version)
if ($LASTEXITCODE -ne 0 -or $ffmpegVersionsausgabe[0] -notmatch '^ffmpeg version (\S+)') { throw "Die FFmpeg-Datei ist nicht ausführbar: $FFmpegQuelle" }
$ffmpegVersion = $Matches[1]
# Die Anwendung benötigt den H.264-Decoder sowie die Filter scale und pad.
$ffmpegDecoder = @(& $FFmpegQuelle -hide_banner -decoders)
$ffmpegFilter = @(& $FFmpegQuelle -hide_banner -filters)
if (-not ($ffmpegDecoder -match '^\s*V\S*\s+h264\s') -or -not ($ffmpegFilter -match '^\s*\S+\s+scale\s') -or -not ($ffmpegFilter -match '^\s*\S+\s+pad\s')) {
    throw "Das FFmpeg für das Paket bietet nicht H.264-Decoder, scale und pad: $FFmpegQuelle"
}
$ffmpegLizenz = @(& $FFmpegQuelle -hide_banner -L)
if (-not $OhneBuild) {
    & dotnet build (Join-Path $projektWurzel 'DroneLive2VirtualCamera.slnx') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Der Release-Build ist fehlgeschlagen.' }
}
if (-not (Test-Path -LiteralPath (Join-Path $releaseOrdner 'DroneLive2VirtualCamera.exe'))) { throw 'Release-Build fehlt.' }
$paketWurzel = Join-Path $projektWurzel 'packages'
New-Item -ItemType Directory -Path $paketWurzel -Force | Out-Null
$zwischenOrdner = Join-Path $paketWurzel ('staging-' + [Guid]::NewGuid().ToString('N'))
$zipPfad = Join-Path $paketWurzel 'DroneLive2VirtualCamera-Setup-win-x64.zip'
try {
    $anwendung = Join-Path $zwischenOrdner 'Application'
    $lizenzen = Join-Path $zwischenOrdner 'licenses'
    New-Item -ItemType Directory -Path $anwendung -Force | Out-Null
    New-Item -ItemType Directory -Path $lizenzen -Force | Out-Null
    foreach ($datei in Get-ChildItem -LiteralPath $releaseOrdner -File) {
        if ($datei.Extension -in @('.dll', '.exe', '.json')) { Copy-Item -LiteralPath $datei.FullName -Destination $anwendung }
    }
    # Eine eigene Auslieferungskonfiguration verhindert, dass die Testadresse im Paket landet.
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package/configuration.json') -Destination (Join-Path $anwendung 'configuration.json') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package/Install.ps1') -Destination $zwischenOrdner
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package/README.txt') -Destination $zwischenOrdner
    Copy-Item -LiteralPath (Join-Path $projektWurzel 'vendor/VCamNetSample/LICENSE') -Destination (Join-Path $lizenzen 'VCamNetSample.txt')
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'package/licenses') -File | Copy-Item -Destination $lizenzen
    $ffmpegOrdner = Join-Path $zwischenOrdner 'FFmpeg'
    New-Item -ItemType Directory -Path $ffmpegOrdner -Force | Out-Null
    Copy-Item -LiteralPath $FFmpegQuelle -Destination (Join-Path $ffmpegOrdner 'ffmpeg.exe')
    # Herkunft, Build-Konfiguration und Lizenz aus der tatsächlich mitgelieferten Datei festhalten.
    $ffmpegHinweis = @('FFmpeg - mitgeliefert im Ordner FFmpeg des Pakets.',
        'Install.ps1 installiert es nur, wenn auf dem Ziel-PC kein FFmpeg gefunden wird.', '',
        'Versionsangabe und Build-Konfiguration der mitgelieferten Datei:') + $ffmpegVersionsausgabe +
        @('', 'Lizenzangabe (ffmpeg -L):') + $ffmpegLizenz +
        @('', 'Vollständiger Lizenztext: FFmpeg-GPLv3.txt',
        'Quellcode von FFmpeg: https://ffmpeg.org/download.html, Versionsstände unter https://ffmpeg.org/releases/',
        'Die oben genannte Build-Konfiguration führt die eingebundenen Bibliotheken auf; deren Quellen nennt der Herausgeber des Builds.')
    $ffmpegHinweis | Set-Content -LiteralPath (Join-Path $lizenzen 'FFmpeg.txt') -Encoding UTF8
    # Lizenzdateien der tatsächlich verwendeten NuGet-Pakete aus dem lokalen Paketcache mitgeben.
    $assets = Get-Content -LiteralPath (Join-Path $projektWurzel 'src/DroneLive.App/obj/project.assets.json') -Raw | ConvertFrom-Json
    foreach ($bibliothek in $assets.libraries.PSObject.Properties) {
        if ($bibliothek.Value.type -ne 'package') { continue }
        foreach ($cache in $assets.packageFolders.PSObject.Properties.Name) {
            $bibliotheksOrdner = Join-Path $cache $bibliothek.Value.path
            if (-not (Test-Path -LiteralPath $bibliotheksOrdner)) { continue }
            $lizenzDateien = Get-ChildItem -LiteralPath $bibliotheksOrdner -Recurse -File |
                Where-Object { $_.Name -match '^(LICENSE|LICENCE|NOTICE|THIRD.PARTY.NOTICES)(\..*)?$' }
            foreach ($lizenz in $lizenzDateien) {
                $zielName = $bibliothek.Name.Replace('/', '-') + '-' + $lizenz.Name
                Copy-Item -LiteralPath $lizenz.FullName -Destination (Join-Path $lizenzen $zielName)
            }
            break
        }
    }
    $dateien = @(Get-ChildItem -LiteralPath $anwendung -File | Sort-Object Name | ForEach-Object {
        [ordered]@{ Name = $_.Name; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    $ffmpegEintrag = [ordered]@{ Name = 'ffmpeg.exe'; Version = $ffmpegVersion
        SHA256 = (Get-FileHash -LiteralPath (Join-Path $ffmpegOrdner 'ffmpeg.exe') -Algorithm SHA256).Hash }
    # Der Paketstand zeigt bei Updates, von welchem auf welchen Stand aktualisiert wird.
    $paketstand = (Get-Date).ToString('yyyy-MM-dd HH:mm')
    [ordered]@{ Format = 2; Produkt = 'DroneLive2VirtualCamera'; Paketstand = $paketstand; Architektur = 'win-x64'; Dateien = $dateien; FFmpeg = $ffmpegEintrag } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $zwischenOrdner 'package-manifest.json') -Encoding UTF8
    . (Join-Path $zwischenOrdner 'Install.ps1')
    $null = Pruefe-Paket $zwischenOrdner
    Compress-Archive -Path (Join-Path $zwischenOrdner '*') -DestinationPath $zipPfad -CompressionLevel Optimal -Force
    $pruefsumme = (Get-FileHash -LiteralPath $zipPfad -Algorithm SHA256).Hash
    ($pruefsumme + '  ' + (Split-Path $zipPfad -Leaf)) | Set-Content -LiteralPath ($zipPfad + '.sha256.txt') -Encoding ASCII
    Write-Host "Installationspaket erstellt: $zipPfad"
    Write-Host "Mitgeliefertes FFmpeg: $ffmpegVersion aus $FFmpegQuelle"
    Write-Host "Paketstand: $paketstand"
    Write-Host ('Größe: {0:N1} MB; SHA256: {1}' -f ((Get-Item -LiteralPath $zipPfad).Length / 1MB), $pruefsumme)
}
finally {
    # Ausschließlich das zuvor erzeugte Zwischenverzeichnis innerhalb des Paketordners entfernen.
    $aufgeloest = [IO.Path]::GetFullPath($zwischenOrdner)
    if ((Split-Path $aufgeloest -Parent) -ne [IO.Path]::GetFullPath($paketWurzel) -or
        (Split-Path $aufgeloest -Leaf) -notmatch '^staging-[a-f0-9]{32}$') { throw 'Unsicherer Zwischenpfad.' }
    if (Test-Path -LiteralPath $aufgeloest) { Remove-Item -LiteralPath $aufgeloest -Recurse -Force }
}

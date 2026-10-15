# Starts a local BRouter server for development (DECISIONS 018), so tests and trials do not load brouter.de.
# Downloads BRouter (GitHub release, checked against its SHA-256) and the segment files of Madrid and León
# (brouter.de/brouter/segments4, OpenStreetMap data under ODbL) into .\data, which git ignores.
#
#   powershell -ExecutionPolicy Bypass -File tools\brouter-local\start.ps1            # start (downloads what is missing)
#   powershell -ExecutionPolicy Bypass -File tools\brouter-local\start.ps1 -Update    # download the segments again
#
# Then point the console at it:  $env:VIENTOHUD_BROUTER_URL = "http://localhost:17777/"
param(
    [switch]$Update,
    # 5x5 degree tiles named by their south-west corner: W5_N40 has Madrid, W10_N40 León and La Bañeza.
    [string[]]$Tiles = @("W5_N40", "W10_N40"),
    [int]$Port = 17777
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$version = "1.7.10"
$zipSha256 = "023FEC3BA997758E8CD7AB9E1BAE52E962AF3F00B57683E3DE86B84FFAD01532"
$userAgent = "VientoHud/0.1"

$data = Join-Path $PSScriptRoot "data"
$brouterDir = Join-Path $data "brouter-$version"
$zip = Join-Path $data "brouter-$version.zip"
$segments = Join-Path $data "segments4"
$customProfiles = Join-Path $data "customprofiles"
New-Item -ItemType Directory -Force $data, $segments, $customProfiles | Out-Null

if (-not (Test-Path (Join-Path $brouterDir "brouter-$version-all.jar"))) {
    if (-not (Test-Path $zip)) {
        Write-Host "Downloading BRouter $version..."
        Invoke-WebRequest -UserAgent $userAgent -OutFile $zip `
            "https://github.com/abrensch/brouter/releases/download/v$version/brouter-$version.zip"
    }
    $actual = (Get-FileHash -Algorithm SHA256 $zip).Hash
    if ($actual -ne $zipSha256) {
        Remove-Item $zip
        throw "brouter-$version.zip has SHA-256 $actual, expected $zipSha256. Deleted it; try again."
    }
    Expand-Archive -Force $zip $data
}

foreach ($tile in $Tiles) {
    $file = Join-Path $segments "$tile.rd5"
    if ($Update -or -not (Test-Path $file)) {
        Write-Host "Downloading segments $tile.rd5 (tens of MB, once)..."
        Invoke-WebRequest -UserAgent $userAgent -OutFile "$file.part" "https://brouter.de/brouter/segments4/$tile.rd5"
        Move-Item -Force "$file.part" $file
    }
}

Write-Host "BRouter $version on http://localhost:$Port/ (Ctrl+C to stop). Segments: $($Tiles -join ', ')"
# As misc/scripts/standalone/server.cmd in the BRouter repository, with more memory for 100 km loops. BRouter takes the
# uploaded-profiles folder relative to the profiles folder (an absolute path gets appended to it), hence ..\..\.
& java -Xmx512M -Xms128M -Xmn8M -DmaxRunningTime=300 -DuseRFCMimeType=false `
    -cp (Join-Path $brouterDir "brouter-$version-all.jar") btools.server.RouteServer `
    $segments (Join-Path $brouterDir "profiles2") "..\..\customprofiles" $Port 1

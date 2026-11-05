# Builds the signed Release APK of VientoHud (DECISIONS 052, as CieloHud's decision 032).
# The signing key is NOT in the repository: it lives in %USERPROFILE%\.vientohud\ (keystore and password file).
# Keep a backup of both: without them, a new version cannot update the installed one.
# The Thunderforest key is read from VIENTOHUD_THUNDERFOREST_KEY at build time (DECISIONS 050); without it the
# APK uses OpenStreetMap tiles.
#
# Usage:  powershell -File scripts\publish-apk.ps1
# Output: artifacts\VientoHud-<version>.apk

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$keyDir = Join-Path $env:USERPROFILE '.vientohud'
$keystore = Join-Path $keyDir 'vientohud-release.keystore'
$passFile = Join-Path $keyDir 'vientohud-release.pass'
if (-not (Test-Path $keystore) -or -not (Test-Path $passFile)) {
    throw "Signing key not found in $keyDir (vientohud-release.keystore and vientohud-release.pass)."
}
if (-not $env:VIENTOHUD_THUNDERFOREST_KEY) {
    Write-Warning 'VIENTOHUD_THUNDERFOREST_KEY is not set: the APK will use OpenStreetMap tiles, not OpenCycleMap.'
}

$project = Join-Path $root 'src\VientoHud.App\VientoHud.App.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ } | Select-Object -First 1
$out = Join-Path $root 'artifacts\publish'

# Clean: a failed signing step must not leave an unsigned APK that a second run takes as up to date.
Remove-Item -Recurse -Force (Join-Path $root 'src\VientoHud.App\bin\Release'), (Join-Path $root 'src\VientoHud.App\obj\Release'), $out -ErrorAction SilentlyContinue

# apksigner reads "file:" passwords line by line (store, then key) from the same file; an environment variable can be used twice.
$env:VIENTOHUD_SIGNING_PASS = (Get-Content $passFile -Raw).Trim()
try {
    dotnet publish $project -f net10.0-android -c Release -o $out `
        -p:AndroidPackageFormat=apk `
        -p:AndroidKeyStore=true `
        -p:AndroidSigningKeyStore="$keystore" `
        -p:AndroidSigningKeyAlias=vientohud `
        -p:AndroidSigningKeyPass=env:VIENTOHUD_SIGNING_PASS `
        -p:AndroidSigningStorePass=env:VIENTOHUD_SIGNING_PASS
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
}
finally {
    Remove-Item Env:VIENTOHUD_SIGNING_PASS -ErrorAction SilentlyContinue
}

$apk = Get-ChildItem $out -Filter '*-Signed.apk' | Select-Object -First 1
$target = Join-Path $root "artifacts\VientoHud-$version.apk"
Copy-Item $apk.FullName $target -Force

# Never hand out an unsigned or wrongly signed APK: it must verify, signed by VientoHud.
$buildTools = (Get-ChildItem "$env:LOCALAPPDATA\Android\Sdk\build-tools" | Sort-Object Name | Select-Object -Last 1).FullName
$java = Get-ChildItem 'C:\Program Files\Microsoft\jdk-17*\bin\java.exe' | Select-Object -First 1
$verify = cmd /c "`"$($java.FullName)`" -jar `"$buildTools\lib\apksigner.jar`" verify --print-certs `"$target`" 2>&1"
if ($LASTEXITCODE -ne 0 -or -not ($verify -match 'CN=VientoHud')) { throw "The APK does not verify:`n$verify" }
($verify | Select-String 'certificate DN').Line
$hash = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
"APK: $target"
"SHA-256: $hash"

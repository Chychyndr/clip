param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $PSScriptRoot "publish-unpackaged.ps1"
$appOutput = Join-Path $root "artifacts\Clip-$Runtime"
$installerProject = Join-Path $root "ClipInstaller\ClipInstaller.csproj"
$payloadDirectory = Join-Path $root "ClipInstaller\Resources"
$payload = Join-Path $payloadDirectory "clip-payload.zip"
$installerOutput = Join-Path $root "artifacts\installer"
$setupPath = Join-Path $root "artifacts\ClipSetup.exe"

& $publishScript -Configuration $Configuration -Runtime $Runtime
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (Test-Path $payload) {
    Remove-Item -LiteralPath $payload -Force
}

Compress-Archive -Path (Join-Path $appOutput "*") -DestinationPath $payload -Force

if (Test-Path $installerOutput) {
    Remove-Item -LiteralPath $installerOutput -Recurse -Force
}

dotnet publish $installerProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $installerOutput

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Copy-Item -LiteralPath (Join-Path $installerOutput "ClipInstaller.exe") -Destination $setupPath -Force
Write-Host "Created $setupPath"

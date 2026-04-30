param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "artifacts\Clip-win-x64"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src\Clip.App\Clip.App.csproj"
$nugetConfig = Join-Path $root "NuGet.Config"
$outputPath = Join-Path $root $Output

if (Test-Path -LiteralPath $outputPath) {
    try {
        Remove-Item -LiteralPath $outputPath -Recurse -Force -ErrorAction Stop
    }
    catch {
        $fallback = "$outputPath-$([DateTime]::Now.ToString('yyyyMMdd-HHmmss'))"
        Write-Warning "Could not clean '$outputPath'. A file may still be locked. Publishing to '$fallback' instead."
        $outputPath = $fallback
    }
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

dotnet restore $project --configfile $nugetConfig
dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $outputPath

$zip = "$outputPath.zip"
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}

Compress-Archive -Path (Join-Path $outputPath "*") -DestinationPath $zip -Force
Write-Host "Published $Runtime to $outputPath"
Write-Host "Archive: $zip"

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "Clip\Clip.csproj"
$output = Join-Path $root "artifacts\Clip-$Runtime"

if (Test-Path $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

dotnet publish $project -c $Configuration -r $Runtime --self-contained true -o $output

$bin = Join-Path $output "Resources\bin"
$required = @("yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe")
foreach ($name in $required) {
    $path = Join-Path $bin $name
    if (-not (Test-Path $path)) {
        Write-Warning "$name was not found in $bin. Copy it into Clip\Resources\bin and publish again."
    }
}

Write-Host "Published Clip to $output"

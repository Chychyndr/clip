param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "Clip\Clip.csproj"
$output = Join-Path $root "artifacts\Clip-$Runtime"

Get-Process Clip -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        Stop-Process -Id $_.Id -Force
        [void]$_.WaitForExit(5000)
    }
    catch {
    }
}

if (Test-Path $output) {
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $output -Recurse -Force
            break
        }
        catch {
            if ($attempt -eq 5) {
                throw
            }

            Start-Sleep -Milliseconds 700
        }
    }
}

dotnet clean $project -c $Configuration -r $Runtime -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet publish $project -c $Configuration -r $Runtime -p:Platform=$Platform --self-contained true -o $output
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$framework = "net8.0-windows10.0.19041.0"
$buildOutput = Join-Path $root "Clip\bin\$Platform\$Configuration\$framework\$Runtime"
if (Test-Path $buildOutput) {
    Get-ChildItem -LiteralPath $buildOutput -Filter "*.xbf" -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $output $_.Name) -Force
    }

    Get-ChildItem -LiteralPath $buildOutput -Filter "*.pri" -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $output $_.Name) -Force
    }

    Get-ChildItem -LiteralPath $buildOutput -Directory | ForEach-Object {
        $sourceDirectory = $_.FullName
        $xbfFiles = Get-ChildItem -LiteralPath $sourceDirectory -Filter "*.xbf" -File -Recurse
        if ($xbfFiles.Count -gt 0) {
            $targetDirectory = Join-Path $output $_.Name
            Copy-Item -LiteralPath $sourceDirectory -Destination $targetDirectory -Recurse -Force
        }
    }
}

$bin = Join-Path $output "Resources\bin"
$required = @("yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe")
foreach ($name in $required) {
    $path = Join-Path $bin $name
    if (-not (Test-Path $path)) {
        Write-Warning "$name was not found in $bin. Copy it into Clip\Resources\bin and publish again."
    }
}

Write-Host "Published Clip to $output"

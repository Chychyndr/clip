param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "Clip\Clip.csproj"
$output = Join-Path $root "artifacts\Clip-$Runtime"
$installedOutput = Join-Path $env:LOCALAPPDATA "Programs\Clip"

function Stop-ProcessIfRunning {
    param([Parameter(Mandatory = $true)]$Process)

    try {
        Stop-Process -Id $Process.Id -Force
        [void]$Process.WaitForExit(5000)
    }
    catch {
    }
}

function Test-IsUnderDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Directory
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($Directory)) {
        return $false
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullDirectory = [System.IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    return $fullPath.StartsWith($fullDirectory, [System.StringComparison]::OrdinalIgnoreCase)
}

function Stop-ClipProcessTree {
    $toolNames = @("Clip", "yt-dlp", "ffmpeg", "ffprobe", "aria2c")
    Get-Process $toolNames -ErrorAction SilentlyContinue | ForEach-Object {
        $path = $null
        try {
            $path = $_.Path
        }
        catch {
        }

        $isClip = $_.ProcessName.Equals("Clip", [System.StringComparison]::OrdinalIgnoreCase)
        $isFromPublishOutput = $path -and (Test-IsUnderDirectory -Path $path -Directory $output)
        $isFromInstalledClip = (Test-Path -LiteralPath $installedOutput) -and $path -and (Test-IsUnderDirectory -Path $path -Directory $installedOutput)

        if ($isClip -or $isFromPublishOutput -or $isFromInstalledClip) {
            Write-Host "Stopping $($_.ProcessName) ($($_.Id))"
            Stop-ProcessIfRunning -Process $_
        }
    }
}

function Clear-FileAttributes {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $_.Attributes = [System.IO.FileAttributes]::Normal
        }
        catch {
        }
    }
}

function Remove-DirectoryRobust {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    Stop-ClipProcessTree
    Clear-FileAttributes -Path $Path

    $lastError = $null
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            $lastError = $_
            Stop-ClipProcessTree
            Start-Sleep -Milliseconds (500 * $attempt)
        }
    }

    $stalePath = "$Path.stale-$(Get-Date -Format 'yyyyMMddHHmmss')"
    try {
        Move-Item -LiteralPath $Path -Destination $stalePath -Force -ErrorAction Stop
        Write-Warning "Could not delete $Path because Windows still has a file handle open. Renamed it to $stalePath and publishing into a fresh folder."
        return
    }
    catch {
        throw "Could not clean $Path. Close Clip and any yt-dlp/ffmpeg processes, then retry. Last error: $($lastError.Exception.Message)"
    }
}

Remove-DirectoryRobust -Path $output

dotnet restore $project -r $Runtime -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
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

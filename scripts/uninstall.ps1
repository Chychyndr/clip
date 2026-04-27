param(
    [switch]$RemoveUserData
)

$ErrorActionPreference = "Stop"

$installDirectory = Join-Path $env:LOCALAPPDATA "Programs\Clip"
$programsDirectory = Join-Path $env:LOCALAPPDATA "Programs"
$uninstaller = Join-Path $installDirectory "ClipUninstall.exe"
$appExe = Join-Path $installDirectory "Clip.exe"
$startMenuDirectory = Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs\Clip"
$appDataDirectory = Join-Path $env:LOCALAPPDATA "Clip"
$uninstallRegistryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Clip"

function Test-IsSafeInstallPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $target = [IO.Path]::GetFullPath($Path)
    $trimChars = @([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $root = [IO.Path]::GetFullPath($programsDirectory).TrimEnd($trimChars) + [IO.Path]::DirectorySeparatorChar
    return $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)
}

if (Test-Path -LiteralPath $uninstaller) {
    & $uninstaller /uninstall /silent
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
else {
    if (Test-Path -LiteralPath $installDirectory) {
        if (-not (Test-IsSafeInstallPath -Path $installDirectory)) {
            throw "Unsafe install path: $installDirectory"
        }

        Get-Process Clip -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                $path = $_.Path
                if ($path -and ([IO.Path]::GetFullPath($path) -eq [IO.Path]::GetFullPath($appExe))) {
                    Stop-Process -Id $_.Id -Force
                    [void]$_.WaitForExit(5000)
                }
            }
            catch {
            }
        }

        Remove-Item -LiteralPath $installDirectory -Recurse -Force
    }

    Remove-Item -LiteralPath $startMenuDirectory -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $uninstallRegistryPath -Recurse -Force -ErrorAction SilentlyContinue
}

if ($RemoveUserData -and (Test-Path -LiteralPath $appDataDirectory)) {
    Remove-Item -LiteralPath $appDataDirectory -Recurse -Force
}

Write-Host "Clip uninstalled."

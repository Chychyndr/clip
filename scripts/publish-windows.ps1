param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Platform = "x64",
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"

$publishScript = Join-Path $PSScriptRoot "publish-unpackaged.ps1"
if (-not [string]::IsNullOrWhiteSpace($Output)) {
    Write-Warning "The Output parameter is ignored by the WinUI publisher. Use scripts\publish-unpackaged.ps1 to publish to artifacts\Clip-$Runtime."
}

& $publishScript -Configuration $Configuration -Runtime $Runtime -Platform $Platform
exit $LASTEXITCODE

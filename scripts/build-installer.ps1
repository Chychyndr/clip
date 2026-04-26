param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$CertificatePath = "",
    [securestring]$CertificatePassword,
    [string]$CertificateThumbprint = "",
    [string]$TimestampServer = "http://timestamp.digicert.com",
    [string]$SignToolPath = ""
)

$ErrorActionPreference = "Stop"

function Test-ShouldSign {
    return -not [string]::IsNullOrWhiteSpace($CertificatePath) -or -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)
}

function Get-CodeSigningTool {
    if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) {
        return (Resolve-Path -LiteralPath $SignToolPath).Path
    }

    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $windowsKits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path -LiteralPath $windowsKits) {
        $candidate = Get-ChildItem -LiteralPath $windowsKits -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "x64\signtool.exe" } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1

        if ($candidate) {
            return $candidate
        }
    }

    throw "signtool.exe was not found. Install Windows SDK or run from Visual Studio Developer PowerShell."
}

function Invoke-CodeSign {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-ShouldSign)) {
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificatePath) -and -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw "Use either CertificatePath or CertificateThumbprint, not both."
    }

    $signTool = Get-CodeSigningTool
    $arguments = @("sign", "/fd", "SHA256", "/tr", $TimestampServer, "/td", "SHA256")
    $passwordPointer = [IntPtr]::Zero
    try {
        if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
            $arguments += @("/f", (Resolve-Path -LiteralPath $CertificatePath).Path)
            if ($CertificatePassword) {
                $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertificatePassword)
                $arguments += @("/p", [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer))
            }
        }
        else {
            $arguments += @("/sha1", $CertificateThumbprint)
        }

        $arguments += $Path
        & $signTool @arguments
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
    finally {
        if ($passwordPointer -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
        }
    }
}

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

Invoke-CodeSign -Path (Join-Path $appOutput "Clip.exe")

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
Invoke-CodeSign -Path $setupPath
Remove-Item -LiteralPath $payload -Force -ErrorAction SilentlyContinue
Write-Host "Created $setupPath"

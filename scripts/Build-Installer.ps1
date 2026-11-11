<#
.SYNOPSIS
Builds a self-contained Windows installer for KotobaSenpai.

.DESCRIPTION
Requires the .NET 10 SDK, Inno Setup 6 or 7, and the two meikiocr ONNX models.
Bundles the pinned UniDic archive so dictionary setup works offline. The archive
is prepared at build time; pass -UniDicArchive to use an existing local ZIP.
The installer is written to artifacts/installer. Build intermediates are removed
from the system temporary directory when the script exits.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1 -InnoSetupCompiler "C:\Tools\Inno Setup 7\ISCC.exe"

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1 -Version "1.0.0"
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Version,
    [string]$OutputDirectory,
    [string]$InnoSetupCompiler,
    [string]$UniDicArchive
)

$ErrorActionPreference = "Stop"

function Find-InnoSetupCompiler {
    param([string]$ExplicitPath)

    if ($ExplicitPath) {
        $resolved = (Resolve-Path -LiteralPath $ExplicitPath -ErrorAction Stop).Path
        if ((Get-Item -LiteralPath $resolved).PSIsContainer) {
            $resolved = Join-Path $resolved "ISCC.exe"
        }
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "Inno Setup compiler not found: $resolved"
        }
        return $resolved
    }

    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate
        }
    }

    $bundledCompiler = Get-ChildItem -LiteralPath (Join-Path $script:RepositoryRoot "artifacts\packaging-tools") `
        -Filter "ISCC.exe" -File -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if ($bundledCompiler) {
        return $bundledCompiler
    }

    throw "Inno Setup 6 or 7 was not found. Install Inno Setup or pass -InnoSetupCompiler with the path to ISCC.exe."
}

$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not $Version) {
    $buildProperties = [xml](Get-Content -LiteralPath (Join-Path $script:RepositoryRoot 'Directory.Build.props') -Raw)
    $Version = [string]$buildProperties.Project.PropertyGroup.Version
}
$parsedVersion = $null
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or
    -not [System.Version]::TryParse($Version, [ref]$parsedVersion) -or
    $parsedVersion.Major -gt 65534 -or $parsedVersion.Minor -gt 65534 -or $parsedVersion.Build -gt 65534) {
    throw "The application version must contain three numbers between 0 and 65534, for example 1.0.2."
}
$project = Join-Path $script:RepositoryRoot "src\KotobaSenpai.App\KotobaSenpai.App.csproj"
$modelsDirectory = Join-Path $script:RepositoryRoot "src\KotobaSenpai.Platform.Windows\Models"
$requiredModels = @(
    "meiki.text.detect.v0.1.960x544.onnx",
    "meiki.text.rec.v0.960x32.onnx"
)
$notices = Join-Path $script:RepositoryRoot "THIRD-PARTY-NOTICES"

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $script:RepositoryRoot "artifacts\installer"
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Application project not found: $project"
}
foreach ($model in $requiredModels) {
    $modelPath = Join-Path $modelsDirectory $model
    if (-not (Test-Path -LiteralPath $modelPath -PathType Leaf)) {
        throw "Required OCR model not found: $modelPath"
    }
}
if (-not (Test-Path -LiteralPath $notices -PathType Leaf)) {
    throw "Third-party notices not found: $notices"
}
if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    throw ".NET SDK was not found. Install the .NET 10 SDK and run this script again."
}

$compiler = Find-InnoSetupCompiler -ExplicitPath $InnoSetupCompiler
$bundledArchive = & (Join-Path $PSScriptRoot "Prepare-UniDic.ps1") -ArchivePath $UniDicArchive
$installerName = "KotobaSenpai-$Version-win-x64-Setup.exe"
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$workDirectory = Join-Path $tempRoot ("KotobaSenpai-installer-" + [guid]::NewGuid().ToString("N"))
$publishDirectory = Join-Path $workDirectory "publish"
$scriptPath = Join-Path $PSScriptRoot "KotobaSenpai.iss"

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $workDirectory -Force | Out-Null

try {
    Write-Host "Publishing KotobaSenpai for win-x64..."
    $publishArguments = @(
        "publish", $project,
        "--configuration", $Configuration,
        "--runtime", "win-x64",
        "--self-contained", "true",
        "-p:Version=$Version",
        "--output", $publishDirectory
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw ".NET publish failed with exit code $LASTEXITCODE."
    }

    foreach ($model in $requiredModels) {
        $publishedModel = Join-Path (Join-Path $publishDirectory "Models") $model
        if (-not (Test-Path -LiteralPath $publishedModel -PathType Leaf)) {
            throw "Required OCR model is missing from the published application: $publishedModel"
        }
    }
    $publishedArchive = Join-Path $publishDirectory ("Assets\UniDic\" + [System.IO.Path]::GetFileName($bundledArchive))
    if (-not (Test-Path -LiteralPath $publishedArchive -PathType Leaf)) {
        throw "Required offline UniDic archive is missing from the published application: $publishedArchive"
    }
    if ((Get-FileHash -LiteralPath $publishedArchive -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $bundledArchive -Algorithm SHA256).Hash) {
        throw "Published UniDic archive does not match the verified release asset."
    }
    & (Join-Path $script:RepositoryRoot "tests\Check-AgentCli.ps1") -PublishDirectory $publishDirectory

    Write-Host "Compiling installer..."
    $compilerArguments = @('/Qp', "/DPublishDir=$publishDirectory", "/DNoticesFile=$notices",
        "/DAppIcon=$(Join-Path $script:RepositoryRoot 'src\KotobaSenpai.App\Assets\Branding\app-icon.ico')",
        "/DAppVersion=$Version", "/DOutputDir=$OutputDirectory", $scriptPath)
    & $compiler @compilerArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
    }

    $installerPath = Join-Path $OutputDirectory $installerName
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw "Inno Setup completed without producing the installer: $installerPath"
    }

    $installer = Get-Item -LiteralPath $installerPath
    $stableInstallerPath = Join-Path $OutputDirectory 'KotobaSenpai-win-x64-Setup.exe'
    Copy-Item -LiteralPath $installerPath -Destination $stableInstallerPath -Force
    Write-Host "Installer created: $($installer.FullName)"
    Write-Host "Latest-release installer alias created: $stableInstallerPath"
    Write-Host ("Size: {0:N1} MiB" -f ($installer.Length / 1MB))
}
finally {
    $resolvedTempRoot = [System.IO.Path]::GetFullPath($tempRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $resolvedWorkDirectory = [System.IO.Path]::GetFullPath($workDirectory)
    if (-not $resolvedWorkDirectory.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove temporary build files outside the system temporary directory: $resolvedWorkDirectory"
    }
    if (Test-Path -LiteralPath $resolvedWorkDirectory) {
        Remove-Item -LiteralPath $resolvedWorkDirectory -Recurse -Force
    }
}

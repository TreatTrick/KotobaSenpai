<#
.SYNOPSIS
Prepares the pinned UniDic archive for offline application distribution.

.DESCRIPTION
Reuses the verified local asset, imports an existing archive with -ArchivePath,
or downloads it at build time. The URL, filename, and SHA-256 come from
UniDicAssets.cs. No dictionary download is performed by the application.
#>
[CmdletBinding()]
param([string]$ArchivePath)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$assetConstants = Get-Content -LiteralPath (Join-Path $repositoryRoot "src\KotobaSenpai.Core\Japanese\UniDicAssets.cs") -Raw

function Get-AssetConstant {
    param([string]$Name)
    $match = [regex]::Match($assetConstants, 'public const string ' + [regex]::Escape($Name) + ' = "([^"]+)";')
    if (-not $match.Success) {
        throw "UniDic asset constant not found: $Name"
    }
    return $match.Groups[1].Value
}

$expectedHash = Get-AssetConstant "Sha256"
$sourceUrl = Get-AssetConstant "SourceUrl"
$fileName = Get-AssetConstant "ArchiveFileName"
$assetDirectory = Join-Path $repositoryRoot "src\KotobaSenpai.App\Assets\UniDic"
$destination = Join-Path $assetDirectory $fileName

function Assert-ArchiveHash {
    param([string]$Path)
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) {
        throw "UniDic archive SHA-256 mismatch at '$Path' (got $actualHash, expected $expectedHash). Use the original pinned archive."
    }
}

if ($ArchivePath) {
    $source = (Resolve-Path -LiteralPath $ArchivePath).Path
    Assert-ArchiveHash $source
    if ($source -ne $destination) {
        New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
}
elseif (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
    New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
    $temporaryArchive = Join-Path $assetDirectory ($fileName + "." + [guid]::NewGuid().ToString("N") + ".partial")
    try {
        Write-Host "Downloading pinned UniDic release asset..."
        $ProgressPreference = "SilentlyContinue"
        Invoke-WebRequest -Uri $sourceUrl -OutFile $temporaryArchive -UseBasicParsing -TimeoutSec 1800
        Assert-ArchiveHash $temporaryArchive
        Move-Item -LiteralPath $temporaryArchive -Destination $destination -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryArchive -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryArchive -Force
        }
    }
}

Assert-ArchiveHash $destination
Write-Host "Verified offline UniDic archive: $destination"
Write-Output $destination

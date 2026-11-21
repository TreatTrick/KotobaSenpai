<#
.SYNOPSIS
Prepares the pitch accent index for offline application distribution.

.DESCRIPTION
Reuses the bundled asset, imports an existing index with -DatabasePath,
or builds it from the pinned Kanjium accents.txt or a local -SourcePath.
#>
[CmdletBinding()]
param([string]$DatabasePath, [string]$SourcePath)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$assetDirectory = Join-Path $repositoryRoot "src/KotobaSenpai.App/Assets/Dictionary"
$destination = Join-Path $assetDirectory "pitch-accent.db"

if ($DatabasePath -and $SourcePath) {
    throw "Provide either -DatabasePath or -SourcePath, not both."
}

$builderArguments = @()
if ($SourcePath) {
    $resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
    if (-not (Test-Path -LiteralPath $resolvedSource -PathType Leaf)) {
        throw "Pitch accent source must be a file: $resolvedSource"
    }
    $builderArguments = @('--source', $resolvedSource)
}

$source = $null
if ($DatabasePath) {
    $source = (Resolve-Path -LiteralPath $DatabasePath).Path
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Pitch accent database must be a file: $source"
    }
}

if ($source -ne $destination -and ($source -or $SourcePath -or -not (Test-Path -LiteralPath $destination -PathType Leaf))) {
    New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
    $temporaryDatabase = Join-Path $assetDirectory ("pitch-accent.db." + [guid]::NewGuid().ToString("N") + ".partial")
    try {
        if ($source) {
            Copy-Item -LiteralPath $source -Destination $temporaryDatabase
        }
        else {
            $builder = Join-Path $repositoryRoot "tools/PitchAccentIndexBuilder/PitchAccentIndexBuilder.csproj"
            Push-Location -LiteralPath $repositoryRoot
            try {
                & dotnet run --project $builder --configuration Release -- --output $temporaryDatabase @builderArguments | Out-Host
                if ($LASTEXITCODE -ne 0) {
                    throw "Pitch accent index builder failed with exit code $LASTEXITCODE."
                }
            }
            finally {
                Pop-Location
            }
        }

        if (-not (Test-Path -LiteralPath $temporaryDatabase -PathType Leaf)) {
            throw "Pitch accent index preparation did not produce a database: $temporaryDatabase"
        }
        if (Test-Path -LiteralPath $destination -PathType Leaf) {
            [System.IO.File]::Replace($temporaryDatabase, $destination, [NullString]::Value)
        }
        else {
            [System.IO.File]::Move($temporaryDatabase, $destination)
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryDatabase -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryDatabase -Force
        }
    }
}

Write-Host "Prepared offline pitch accent index: $destination"
Write-Output $destination

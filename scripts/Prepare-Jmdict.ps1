<#
.SYNOPSIS
Prepares the JMdict index for offline application distribution.

.DESCRIPTION
Reuses the bundled asset, imports an existing index with -DatabasePath,
or builds it from JMdict English data at build time.
#>
[CmdletBinding()]
param([string]$DatabasePath)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$assetDirectory = Join-Path $repositoryRoot "src/KotobaSenpai.App/Assets/Dictionary"
$destination = Join-Path $assetDirectory "jmdict.db"

$source = $null
if ($DatabasePath) {
    $source = (Resolve-Path -LiteralPath $DatabasePath).Path
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "JMdict database must be a file: $source"
    }
}

if ($source -ne $destination -and ($source -or -not (Test-Path -LiteralPath $destination -PathType Leaf))) {
    New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
    $temporaryDatabase = Join-Path $assetDirectory ("jmdict.db." + [guid]::NewGuid().ToString("N") + ".partial")
    try {
        if ($source) {
            Copy-Item -LiteralPath $source -Destination $temporaryDatabase
        }
        else {
            $builder = Join-Path $repositoryRoot "tools/JmdictIndexBuilder/JmdictIndexBuilder.csproj"
            Push-Location -LiteralPath $repositoryRoot
            try {
                & dotnet run --project $builder --configuration Release -- --output $temporaryDatabase | Out-Host
                if ($LASTEXITCODE -ne 0) {
                    throw "JMdict index builder failed with exit code $LASTEXITCODE."
                }
            }
            finally {
                Pop-Location
            }
        }

        if (-not (Test-Path -LiteralPath $temporaryDatabase -PathType Leaf)) {
            throw "JMdict index preparation did not produce a database: $temporaryDatabase"
        }
        Move-Item -LiteralPath $temporaryDatabase -Destination $destination -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryDatabase -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryDatabase -Force
        }
    }
}

Write-Host "Prepared offline JMdict index: $destination"
Write-Output $destination

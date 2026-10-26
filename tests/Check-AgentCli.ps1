param([Parameter(Mandatory = $true)][string]$PublishDirectory)

$ErrorActionPreference = 'Stop'
$cliPath = Join-Path $PublishDirectory 'KotobaSenpai.Cli.exe'
$capabilityJson = & $cliPath capabilities --json
if ($LASTEXITCODE -ne 0) { throw 'Capability discovery failed.' }
$capabilities = $capabilityJson | ConvertFrom-Json
if ($capabilities.schemaVersion -ne 1 -or $capabilities.commands.Count -ne 5) {
    throw 'The published capability contract is incomplete.'
}
if ($capabilities.protocols[0] -ne 'openai-responses') { throw 'The default protocol priority changed.' }
if ($capabilities.commands.command -match 'undo') { throw 'A removed command is still advertised.' }

$standaloneRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$standaloneDirectory = Join-Path $standaloneRoot ('ks-cli-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $standaloneDirectory)
try {
    foreach ($name in @('KotobaSenpai.Cli.exe', 'KotobaSenpai.Cli.dll', 'KotobaSenpai.Cli.deps.json',
            'KotobaSenpai.Cli.runtimeconfig.json', 'KotobaSenpai.Core.dll', 'KotobaSenpai.Platform.Windows.dll')) {
        Copy-Item -LiteralPath (Join-Path $PublishDirectory $name) -Destination $standaloneDirectory
    }
    $standaloneJson = & (Join-Path $standaloneDirectory 'KotobaSenpai.Cli.exe') capabilities --json
    if ($LASTEXITCODE -ne 0 -or ($standaloneJson | ConvertFrom-Json).commands.Count -ne 5) {
        throw 'The CLI cannot discover capabilities without the App executable.'
    }
}
finally {
    $resolvedStandalone = [System.IO.Path]::GetFullPath($standaloneDirectory)
    if (-not $resolvedStandalone.StartsWith($standaloneRoot, [StringComparison]::OrdinalIgnoreCase) -or
            [System.IO.Path]::GetFileName($resolvedStandalone) -notmatch '^ks-cli-[0-9a-f]{32}$') {
        throw 'The CLI check directory is outside the expected temporary location.'
    }
    Remove-Item -LiteralPath $resolvedStandalone -Recurse -Force
}

$cases = @(
    @{ Input = '{invalid-json'; Code = 'invalid_json' },
    @{ Input = '{"schemaVersion":99,"apiKey":"test-secret"}'; Code = 'unsupported_schema_version' },
    @{ Input = '{"schemaVersion":1,"endpoint":"https://api.example.com/v1","model":"test-model"}'; Code = 'missing_api_key' },
    @{ Input = '{"schemaVersion":1,"endpoint":"https://api.example.com/v1","apiKey":"YOUR_API_KEY","model":"test-model"}'; Code = 'missing_api_key' },
    @{ Input = '{"schemaVersion":1,"endpoint":"https://api.example.com/v1","apiKey":"test-secret"}'; Code = 'missing_model' }
)
foreach ($case in $cases) {
    $outputJson = $case.Input | & $cliPath llm configure --stdin --json
    if ($LASTEXITCODE -ne 2) { throw 'Invalid input did not return exit code 2.' }
    $result = $outputJson | ConvertFrom-Json
    if ($result.code -ne $case.Code) { throw "Unexpected result code: $($result.code)." }
    if ($outputJson -match 'test-secret') { throw 'A credential leaked into the CLI output.' }
}
$removedCommandJson = '{"schemaVersion":1}' | & $cliPath llm undo --stdin --json
if ($LASTEXITCODE -ne 2 -or ($removedCommandJson | ConvertFrom-Json).code -ne 'invalid_command') {
    throw 'The removed undo command is still accepted.'
}
$helpText = & $cliPath --help
if ($LASTEXITCODE -ne 0 -or -not ($helpText -match 'capabilities --json')) { throw 'CLI help is unavailable.' }
Write-Output 'Published CLI checks passed without provider requests or settings writes.'

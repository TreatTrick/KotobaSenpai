#requires -Version 7.0
<#
.SYNOPSIS
Compiles the real installer script and checks update waiting and restart with an isolated application.
.DESCRIPTION
Uses a unique AppId and directories under artifacts, never the real installation or its settings.
Requires the .NET SDK and Inno Setup. Leaves logs and fixtures under artifacts for inspection.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$InnoSetupCompiler)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$checkRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot ('artifacts\update installer check ' + [guid]::NewGuid().ToString('N'))))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $checkRoot.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The installer fixture must stay inside the repository artifacts directory.'
}
$hostSource = Join-Path $checkRoot 'host'
$payload = Join-Path $checkRoot 'payload'
$installed = Join-Path $checkRoot 'installed app'
New-Item -ItemType Directory -Path $hostSource, $installed -Force | Out-Null
Set-Content -LiteralPath (Join-Path $hostSource 'Host.csproj') -Value @'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><AssemblyName>KotobaSenpai.App</AssemblyName><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>
'@
Set-Content -LiteralPath (Join-Path $hostSource 'Program.cs') -Value @'
if (args.Length == 1)
{
    File.WriteAllText(args[0] + ".ready", "ready");
    while (!File.Exists(args[0])) Thread.Sleep(25);
    return;
}
File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "started.txt"), "started\n");
'@
& dotnet publish (Join-Path $hostSource 'Host.csproj') -c Release --self-contained false -o $payload --nologo
if ($LASTEXITCODE -ne 0) { throw 'The installer fixture application did not compile.' }

$appId = [guid]::NewGuid().ToString().ToUpperInvariant()
$source = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\KotobaSenpai.iss') -Raw
$source = $source.Replace('AppId={{A32009BB-E6D3-49E5-A793-E0B1F2F51C11}', ('AppId={{' + $appId + '}'))
$source = $source.Replace('AppName=KotobaSenpai', 'AppName=KotobaSenpai update test')
# Keep the production wait logic but shorten its deadline for the timeout check.
$source = $source.Replace('WaitForSingleObject(ParentHandle, 60000)', 'WaitForSingleObject(ParentHandle, 1000)')
$source = [regex]::Replace($source, '(?ms)^\[Icons\].*?(?=^\[Run\])', '')
$fixtureScript = Join-Path $checkRoot 'Fixture.iss'
Set-Content -LiteralPath $fixtureScript -Value $source -Encoding utf8
$compilerArguments = @('/Qp', "/DPublishDir=$payload", "/DNoticesFile=$(Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES')",
    "/DAppIcon=$(Join-Path $repositoryRoot 'src\KotobaSenpai.App\Assets\Branding\app-icon.ico')",
    '/DAppVersion=0.0.0', "/DOutputDir=$checkRoot", $fixtureScript)
& $InnoSetupCompiler @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'The update installer script did not compile.' }

function Start-HiddenProcess([string]$Path, [string[]]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new($Path)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    return [Diagnostics.Process]::Start($info)
}
function Wait-ForFile([string]$Path) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $Path)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Expected fixture file was not created: $Path" }
        Start-Sleep -Milliseconds 25
    }
}

$installer = Join-Path $checkRoot 'KotobaSenpai-0.0.0-win-x64-Setup.exe'
$marker = Join-Path $installed 'started.txt'
$releaseParent = Join-Path $checkRoot 'release-parent'
$timeoutRelease = $null
$parent = $null
$setup = $null
Set-Content -LiteralPath (Join-Path $installed 'preserved.txt') -Value 'Keep this existing file.'
try {
    $parent = Start-HiddenProcess (Join-Path $payload 'KotobaSenpai.App.exe') @($releaseParent)
    Wait-ForFile ($releaseParent + '.ready')
    $parentId = $parent.Id
    $arguments = @('/VERYSILENT', '/SP-', '/NORESTART', '/SUPPRESSMSGBOXES', '/NOICONS',
        '/APPUPDATE=1', "/UPDATEPID=$parentId", "/DIR=$installed", "/LOG=$(Join-Path $checkRoot 'success.log')")
    $setup = Start-HiddenProcess $installer $arguments
    Start-Sleep -Milliseconds 400
    if (Test-Path -LiteralPath (Join-Path $installed 'KotobaSenpai.App.exe')) { throw 'Files were replaced before the parent exited.' }
    Set-Content -LiteralPath $releaseParent -Value 'exit'
    if (-not $parent.WaitForExit(5000)) { throw 'The fixture parent did not exit.' }
    if (-not $setup.WaitForExit(30000) -or $setup.ExitCode -ne 0) { throw 'The update installation failed.' }
    Wait-ForFile $marker
    if (@(Get-Content -LiteralPath $marker).Count -ne 1) { throw 'The updated app must start exactly once.' }
    if ((Get-Content -LiteralPath (Join-Path $installed 'preserved.txt')) -ne 'Keep this existing file.') { throw 'An existing file was changed.' }
    $registryRoot = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $registration = $registryRoot.OpenSubKey("Software\Microsoft\Windows\CurrentVersion\Uninstall\{$appId}_is1")
        try {
            if (-not $registration -or [IO.Path]::TrimEndingDirectorySeparator($registration.GetValue('InstallLocation')) -ne $installed) {
                throw 'The installer registration does not match the directory used by the update adapter.'
            }
        }
        finally { if ($registration) { $registration.Dispose() } }
    }
    finally { $registryRoot.Dispose() }
    $setup.Dispose()
    $setup = $null
    $parent.Dispose()

    $timeoutRelease = Join-Path $checkRoot 'release-timeout-parent'
    $parent = Start-HiddenProcess (Join-Path $payload 'KotobaSenpai.App.exe') @($timeoutRelease)
    Wait-ForFile ($timeoutRelease + '.ready')
    $timeoutTarget = Join-Path $checkRoot 'timeout target'
    $setup = Start-HiddenProcess $installer @('/VERYSILENT', '/SP-', '/NORESTART', '/SUPPRESSMSGBOXES',
        '/APPUPDATE=1', "/UPDATEPID=$($parent.Id)", "/DIR=$timeoutTarget", "/LOG=$(Join-Path $checkRoot 'timeout.log')")
    if (-not $setup.WaitForExit(30000) -or $setup.ExitCode -eq 0) { throw 'The installer did not abort when its parent stayed alive.' }
    if ($parent.HasExited) { throw 'The installer terminated its parent.' }
    if (Test-Path -LiteralPath (Join-Path $timeoutTarget 'KotobaSenpai.App.exe')) { throw 'The timeout path replaced files.' }
    Set-Content -LiteralPath $timeoutRelease -Value 'exit'
    $parent.WaitForExit(5000) | Out-Null
    Write-Host "Passed: compilation, parent wait, one restart, preserved files, registration, safe timeout. Logs: $checkRoot"
}
finally {
    Set-Content -LiteralPath $releaseParent -Value 'exit'
    if ($timeoutRelease) { Set-Content -LiteralPath $timeoutRelease -Value 'exit' }
    if ($parent) { if (-not $parent.WaitForExit(5000)) { $parent.Kill() }; $parent.Dispose() }
    if ($setup) { if (-not $setup.HasExited) { $setup.Kill($true) }; $setup.Dispose() }
    $uninstaller = Join-Path $installed 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        $cleanup = Start-HiddenProcess $uninstaller @('/VERYSILENT', '/NORESTART', '/SUPPRESSMSGBOXES')
        if (-not $cleanup.WaitForExit(30000) -or $cleanup.ExitCode -ne 0) { throw "Uninstall the isolated fixture using $uninstaller" }
        $cleanup.Dispose()
    }
}

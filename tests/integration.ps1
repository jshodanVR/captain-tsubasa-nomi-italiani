[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OriginalText,
    [Parameter(Mandatory=$true)][string]$MainExecutable,
    [Parameter(Mandatory=$true)][string]$OriginalLauncher,
    [string]$PreviousPatchedText,
    [string]$WorkDirectory,
    [string]$InstallerPath
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'patch\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $WorkDirectory) { $WorkDirectory = Join-Path $projectRoot '.test-work' }
if (-not $InstallerPath) { $InstallerPath = Join-Path $projectRoot ('dist\Holly-e-Benji-Nomi-Italia-Installer-v' + $manifest.version + '.exe') }
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
$arguments = @('--self-test')
foreach ($path in @($WorkDirectory,$OriginalText,$MainExecutable,$OriginalLauncher)) { $arguments += '"' + [IO.Path]::GetFullPath($path).TrimEnd('\') + '"' }
if ($PreviousPatchedText) { $arguments += '"' + [IO.Path]::GetFullPath($PreviousPatchedText) + '"' }
$process = Start-Process -FilePath $InstallerPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) {
    $errorLog = Join-Path ([IO.Path]::GetDirectoryName($InstallerPath)) 'installer-test-error.txt'
    if (Test-Path -LiteralPath $errorLog) { Get-Content -LiteralPath $errorLog }
    throw 'Verifica di integrazione non riuscita.'
}
Get-Content -LiteralPath (Join-Path $WorkDirectory 'result.txt')
$checks = @('embedded payload','Steam library parsing','alternate game directory','install','verification','reinstall','restore','idempotent restore','reject unknown version')
if ($PreviousPatchedText) { $checks += 'upgrade previous patch'; $checks += 'restore after upgrade' }
[ordered]@{ version=$manifest.version; result='passed'; checks=$checks; gameProcessLaunched=$false } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'installer-verification.json') -Encoding UTF8

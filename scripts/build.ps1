[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'dist' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'patch\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Compilatore .NET Framework non trovato su questo PC.' }
$baseName = 'Holly-e-Benji-Nomi-Italia-Installer-v' + $manifest.version
$executable = Join-Path $OutputDirectory ($baseName + '.exe')
$compileArguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',
    ('/win32manifest:' + (Join-Path $projectRoot 'installer\app.manifest')),
    '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',
    ('/resource:' + (Join-Path $projectRoot 'patch\manifest.json') + ',manifest.json'),
    ('/resource:' + (Join-Path $projectRoot 'patch\append-text.bin') + ',append-text.bin'),
    ('/resource:' + (Join-Path $projectRoot 'patch\table-patch.bin') + ',table-patch.bin'),
    ('/out:' + $executable),(Join-Path $projectRoot 'installer\Installer.cs'))
& $compiler @compileArguments
if ($LASTEXITCODE -ne 0) { throw 'Compilazione dell installatore non riuscita.' }
$bundleResult = Join-Path $OutputDirectory 'bundle-verification.txt'
$process = Start-Process -FilePath $executable -ArgumentList @('--verify-bundle',('"' + $bundleResult + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'Il pacchetto incorporato non supera i controlli.' }
$instructions = Join-Path $OutputDirectory 'INSTALLAZIONE.txt'
Copy-Item -LiteralPath (Join-Path $projectRoot 'INSTALLAZIONE.txt') -Destination $instructions -Force
$digest = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
$checksum = Join-Path $OutputDirectory ($baseName + '.sha256')
[IO.File]::WriteAllText($checksum, ($digest + '  ' + [IO.Path]::GetFileName($executable) + "`n"), [Text.Encoding]::ASCII)
Compress-Archive -LiteralPath @($executable,$instructions,$checksum) -DestinationPath (Join-Path $OutputDirectory ($baseName + '.zip')) -Force
Write-Output ('Installatore compilato e verificato: ' + $executable)

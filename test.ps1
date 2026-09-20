param([string]$ExecutableName = 'ScreenWatch.exe')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -ExecutableName $ExecutableName
$reports = Join-Path $PSScriptRoot 'test-output'
New-Item -ItemType Directory -Force $reports | Out-Null
$report = Join-Path $reports 'results.txt'
$app = Join-Path (Join-Path $PSScriptRoot 'dist') $ExecutableName
$process = Start-Process -FilePath $app -ArgumentList @('--self-test', ('"' + $report + '"')) -WindowStyle Hidden -PassThru -Wait
if (Test-Path $report) { Get-Content -LiteralPath $report -Encoding UTF8 }
if ($process.ExitCode -ne 0) { throw ('Tests failed: ' + $process.ExitCode) }

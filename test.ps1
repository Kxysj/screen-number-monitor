$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$reports = Join-Path $PSScriptRoot 'test-output'
New-Item -ItemType Directory -Force $reports | Out-Null
$report = Join-Path $reports 'results.txt'
$app = Join-Path $PSScriptRoot 'dist\ScreenWatch.exe'
$process = Start-Process -FilePath $app -ArgumentList @('--self-test', ('"' + $report + '"')) -WindowStyle Hidden -PassThru -Wait
if (Test-Path $report) { Get-Content -LiteralPath $report -Encoding UTF8 }
if ($process.ExitCode -ne 0) { throw ('Tests failed: ' + $process.ExitCode) }

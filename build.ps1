$ErrorActionPreference = 'Stop'
$sourceDir = Join-Path $PSScriptRoot 'src'
$outputDir = Join-Path $PSScriptRoot 'dist'
$framework = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$runtime = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path $framework)) { $framework = $runtime }
New-Item -ItemType Directory -Force $outputDir, (Join-Path $outputDir 'tests') | Out-Null
$compileArgs = @('/noconfig','/nostdlib+','/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001','/nowarn:1701')
$compileArgs += '/out:' + (Join-Path $outputDir 'ScreenWatch.exe')
$compileArgs += '/win32manifest:' + (Join-Path $sourceDir 'app.manifest')
$compileArgs += '/resource:' + (Join-Path $PSScriptRoot 'assets\alarm.wav') + ',ScreenWatch.alarm.wav'
foreach ($reference in @('mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Xml','System.Security','System.Net.Http','System.Web.Extensions','Facades\System.Runtime','Facades\System.Threading.Tasks','Facades\System.Runtime.InteropServices.WindowsRuntime')) {
    $compileArgs += '/r:' + (Join-Path $framework ($reference + '.dll'))
}
$compileArgs += '/r:' + (Join-Path $runtime 'System.Runtime.WindowsRuntime.dll')
$compileArgs += Get-ChildItem (Join-Path $env:WINDIR 'System32\WinMetadata\*.winmd') | ForEach-Object { '/r:' + $_.FullName }
$compileArgs += Get-ChildItem (Join-Path $sourceDir '*.cs') | ForEach-Object { $_.FullName }
& (Join-Path $runtime 'csc.exe') @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\alarm.wav'), (Join-Path $PSScriptRoot 'assets\ScreenWatch.exe.config'), (Join-Path $PSScriptRoot 'README.md'), (Join-Path $PSScriptRoot 'examples\demo.html') -Destination $outputDir -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'tests\decimal-regression.png') -Destination (Join-Path $outputDir 'tests') -Force
Write-Output (Join-Path $outputDir 'ScreenWatch.exe')

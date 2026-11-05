param([switch]$Test,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$testerRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 es necesario para compilar.' }
if (!$OutputDirectory) { $OutputDirectory=$testerRoot }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$refs=@('System.dll','System.Core.dll','System.Web.dll','System.Web.Extensions.dll','System.Xaml.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')
$argsList=@('/nologo','/optimize+','/platform:anycpu','/utf8output','/codepage:65001','/main:WOTester.Program',('/win32manifest:'+(Join-Path $testerRoot 'assets\app.manifest')))
foreach($ref in $refs){ $path=Join-Path $framework $ref; if(!(Test-Path -LiteralPath $path)){$path=Join-Path (Join-Path $framework 'WPF') $ref};$argsList+=('/r:'+$path)}
$argsList+=('/resource:'+(Join-Path $testerRoot 'src\MainWindow.xaml')+',WOTester.MainWindow.xaml')
$argsList+=('/resource:'+(Join-Path $testerRoot 'assets\probe.gd')+',WOTester.probe.gd')
$icon=Join-Path $testerRoot 'assets\wo-tester.ico'
if(Test-Path -LiteralPath $icon){$argsList+=('/win32icon:'+$icon)}
$sources=Get-ChildItem -LiteralPath (Join-Path $testerRoot 'src') -Filter '*.cs' | ForEach-Object FullName
& $compiler @argsList /target:winexe ('/out:'+(Join-Path $OutputDirectory 'WO Tester.exe')) @sources
if($LASTEXITCODE -ne 0){throw 'No se pudo compilar WO Tester.'}
& $compiler @argsList /target:exe ('/out:'+(Join-Path $OutputDirectory 'wo-tester-cli.exe')) @sources
if($LASTEXITCODE -ne 0){throw 'No se pudo compilar la CLI.'}
if($Test) {
 $testArgs=$argsList | Where-Object {$_ -notlike '/main:*'}
 & $compiler @testArgs /target:exe /main:WOTester.Tests ('/out:'+(Join-Path $testerRoot 'tests\WOTester.Tests.exe')) @sources (Join-Path $testerRoot 'tests\Tests.cs')
 if($LASTEXITCODE -ne 0){throw 'No se pudieron compilar las pruebas.'}
 & (Join-Path $testerRoot 'tests\WOTester.Tests.exe')
 if($LASTEXITCODE -ne 0){throw 'Fallaron las pruebas de WO Tester.'}
}
Get-Item -LiteralPath (Join-Path $OutputDirectory 'WO Tester.exe') | Select-Object Name,Length

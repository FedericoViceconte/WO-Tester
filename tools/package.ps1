param([string]$Version='1.0.0')
$ErrorActionPreference='Stop'
$testerRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dist=Join-Path $testerRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$package=Join-Path $dist 'WO-Tester-Windows.zip'
$items=@((Join-Path $testerRoot 'WO Tester.exe'),(Join-Path $testerRoot 'wo-tester-cli.exe'),(Join-Path $testerRoot 'GUIA-DE-USO.md'),(Join-Path $testerRoot 'LICENSE'))
Compress-Archive -LiteralPath $items -DestinationPath $package -Force
$hash=Get-FileHash -LiteralPath $package -Algorithm SHA256
($hash.Hash.ToLower()+'  WO-Tester-Windows.zip') | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding utf8
$testerCommit='unversioned-local-source'
if(Test-Path -LiteralPath (Join-Path $testerRoot '.git')) { $testerCommit=git -C $testerRoot rev-parse HEAD }
$receipt=@{version=$Version;builtUtc=[DateTime]::UtcNow.ToString('o');sha256=$hash.Hash.ToLower();sourceCommit=$testerCommit}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dist 'build-info.json') -Encoding utf8
Write-Output $package

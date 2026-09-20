param([Parameter(Mandatory=$true)][string]$PackageRoot)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name valheim,valheim_server -ErrorAction SilentlyContinue) { throw 'Wait for game tests to finish before installer mutation tests.' }
$testRoot = Join-Path $PSScriptRoot ('work/installer-tests/' + [Guid]::NewGuid().ToString('N'))
$testPackage = Join-Path $testRoot 'package'
$fakeGame = Join-Path $testRoot 'fake-game'
New-Item -ItemType Directory -Force -Path (Join-Path $testPackage 'BepInEx/plugins/Pikalajittelu'),(Join-Path $fakeGame 'BepInEx/core') | Out-Null
foreach ($file in @('Asenna.ps1','package.json')) { Copy-Item -LiteralPath (Join-Path $PackageRoot $file) -Destination $testPackage }
$payload = Join-Path $testPackage 'BepInEx/plugins/Pikalajittelu/Pikalajittelu.dll'
Copy-Item -LiteralPath (Join-Path $PackageRoot 'BepInEx/plugins/Pikalajittelu/Pikalajittelu.dll') -Destination $payload
Set-Content -LiteralPath (Join-Path $fakeGame 'valheim.exe') -Value 'Installer fixture only; never executable.'
Copy-Item -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/core/BepInEx.dll' -Destination (Join-Path $fakeGame 'BepInEx/core/BepInEx.dll')
$installer = Join-Path $testPackage 'Asenna.ps1'
$target = Join-Path $fakeGame 'BepInEx/plugins/Pikalajittelu/Pikalajittelu.dll'
$expected = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
$checks = 0
function Assert-Test($condition, $message) { if (!$condition) { throw $message }; $script:checks++ }
function Assert-Rejected($action, $pattern) {
    $rejected = $false
    try { & $action | Out-Null } catch { if ($_.Exception.Message -notmatch $pattern) { throw }; $rejected = $true }
    Assert-Test $rejected ('Installer should reject: ' + $pattern)
}
& $installer -GamePath $fakeGame -ValidateOnly
Assert-Test (!(Test-Path -LiteralPath $target)) 'Validation-only installed a DLL'
& $installer -GamePath $fakeGame
Assert-Test ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $expected) 'Install hash mismatch'
$sentinel = Join-Path $fakeGame 'BepInEx/user-config-sentinel.txt'
Set-Content -LiteralPath $sentinel -Value 'preserve'
& $installer -GamePath $fakeGame
Assert-Test (@(Get-ChildItem -LiteralPath (Join-Path $testPackage 'work') -Recurse -File -Filter Pikalajittelu.dll).Count -eq 1) 'Upgrade did not back up the previous DLL'
Assert-Test ((Get-Content -LiteralPath $sentinel -Raw).Trim() -eq 'preserve') 'Installer changed other configuration'
$duplicate = Join-Path $fakeGame 'BepInEx/plugins/Pikalajittelu.dll'
Copy-Item -LiteralPath $payload -Destination $duplicate
Assert-Rejected { & $installer -GamePath $fakeGame } 'Toinen Pikalajittelu'
Move-Item -LiteralPath $duplicate -Destination ($duplicate + '.test-disabled')
$payloadBytes = [IO.File]::ReadAllBytes($payload)
[IO.File]::WriteAllBytes($payload, [byte[]]@(1,2,3))
Assert-Rejected { & $installer -GamePath $fakeGame } 'tarkistussumma'
Assert-Test ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $expected) 'Rejected corrupt payload changed installed DLL'
[IO.File]::WriteAllBytes($payload, $payloadBytes)
Set-Content -LiteralPath (Join-Path $testPackage 'RELEASE-BLOCKED.txt') -Value 'test'
Assert-Rejected { & $installer -GamePath $fakeGame } 'kehityksessa'
Set-Content -LiteralPath (Join-Path $testRoot 'result.txt') -Value ('INSTALLER_PASS checks=' + $checks + '; fixture=' + $fakeGame)
Get-Content -LiteralPath (Join-Path $testRoot 'result.txt')

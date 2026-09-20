param([string]$ClientPath = (Join-Path $PSScriptRoot 'work/runtime-tests/client'), [ValidateSet('client','host')][string]$Disconnect = 'client')
$ErrorActionPreference = 'Stop'
if (Get-Process -Name valheim,valheim_server -ErrorAction SilentlyContinue) { throw 'A Valheim process is already running. It was not stopped.' }
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'work/runtime-tests'))
$client = (Resolve-Path -LiteralPath $ClientPath).Path
if (!$client.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    !(Test-Path -LiteralPath (Join-Path $client 'ISOLATED-VALIDATION'))) { throw 'Expected the marked isolated test client.' }
$remoteClient = (Resolve-Path -LiteralPath (Join-Path $workspace 'peer-client')).Path
if (!(Test-Path -LiteralPath (Join-Path $remoteClient 'ISOLATED-VALIDATION'))) { throw 'Prepare the second isolated client first.' }
& (Join-Path $PSScriptRoot 'Build-RuntimeTests.ps1')
$testId = [Guid]::NewGuid().ToString('N')
$runRoot = Join-Path $workspace ('runs/peer-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$peerRoot = Join-Path $workspace ('peer-data/' + $testId)
New-Item -ItemType Directory -Force -Path $runRoot,$peerRoot | Out-Null
foreach ($file in @('Pikalajittelu.dll','Pikalajittelu.Validation.dll')) {
    $buildFile = Join-Path $PSScriptRoot ('work/builds/Pikalajittelu2/' + $file)
    Copy-Item -LiteralPath $buildFile -Destination (Join-Path $client 'BepInEx/plugins') -Force
    New-Item -ItemType Directory -Force -Path (Join-Path $remoteClient 'BepInEx/plugins') | Out-Null
    Copy-Item -LiteralPath $buildFile -Destination (Join-Path $remoteClient 'BepInEx/plugins') -Force
    Copy-Item -LiteralPath $buildFile -Destination $runRoot
}
$processes = @()
foreach ($role in @('host','client')) {
    $roleClient = if ($role -eq 'host') { $client } else { $remoteClient }
    $log = Join-Path $runRoot ($role + '-player.log')
    $arguments = @('-batchmode','-nographics',('-storage-peer-' + $role),('-storage-peer-run=' + $testId),('-storage-disconnect=' + $Disconnect),'-logFile',('"' + $log + '"'))
    $process = Start-Process -FilePath (Join-Path $roleClient 'valheim.exe') -WorkingDirectory $roleClient -WindowStyle Hidden -PassThru -ArgumentList $arguments
    $processes += [pscustomobject]@{ Role=$role; Id=$process.Id; Started=$process.StartTime.ToUniversalTime().ToString('O'); Executable=(Join-Path $roleClient 'valheim.exe'); Result=(Join-Path $peerRoot ($role + '-result.txt')); Log=$log }
    [pscustomobject]@{ RunRoot=$runRoot; PeerRoot=$peerRoot; Processes=$processes; PluginSHA256=(Get-FileHash -LiteralPath (Join-Path $runRoot 'Pikalajittelu.dll') -Algorithm SHA256).Hash } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $workspace 'peer-run.json')
}
Copy-Item -LiteralPath (Join-Path $workspace 'peer-run.json') -Destination (Join-Path $runRoot 'run.json')
Get-Content -LiteralPath (Join-Path $workspace 'peer-run.json')

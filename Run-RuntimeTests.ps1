param([string]$ClientPath = (Join-Path $PSScriptRoot 'work/runtime-tests/client'), [switch]$World, [switch]$ReloadWorld, [switch]$Ui, [switch]$Large, [switch]$ExperimentalUiInput)
$ErrorActionPreference = 'Stop'
if ($World -and $ReloadWorld) { throw 'Choose World or ReloadWorld.' }
if ($Ui -and !$World) { throw 'UI rendering is part of the isolated World test.' }
if ($ExperimentalUiInput -and !$Ui) { throw 'Experimental input probes require Ui. They are not a validated input driver.' }
if ($Large -and !$World) { throw 'Large storage testing requires World.' }
if (Get-Process -Name valheim,valheim_server -ErrorAction SilentlyContinue) {
    throw 'Runtime test not started: a Valheim process is already running. It was not stopped.'
}
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'work/runtime-tests'))
$client = (Resolve-Path -LiteralPath $ClientPath).Path
if (!$client.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    !(Test-Path -LiteralPath (Join-Path $client 'ISOLATED-VALIDATION'))) {
    throw 'Expected a prepared, marked client inside this workspace runtime-tests directory.'
}
& (Join-Path $PSScriptRoot 'Build-RuntimeTests.ps1')
$runRoot = Join-Path $workspace ('runs/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
foreach ($file in @('validation-result.txt','validation-player.log')) {
    $previous = Join-Path $client $file
    if (Test-Path -LiteralPath $previous) { Move-Item -LiteralPath $previous -Destination (Join-Path $runRoot ('previous-' + $file)) }
}
$buildRoot = Join-Path $PSScriptRoot 'work/builds/Pikalajittelu2'
foreach ($file in @('Pikalajittelu.dll','Pikalajittelu.Validation.dll')) {
    Copy-Item -LiteralPath (Join-Path $buildRoot $file) -Destination (Join-Path $client 'BepInEx/plugins') -Force
    Copy-Item -LiteralPath (Join-Path $buildRoot $file) -Destination $runRoot
}
$log = Join-Path $client 'validation-player.log'
# No world is selected. The harness sets and verifies its own save directory in
# Awake and disables FileHelpers cloud storage before running scratch inventories.
$arguments = @('-batchmode','-nographics','-logFile',('"' + $log + '"'))
if ($Ui) { $arguments = @('-force-d3d11','-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-storage-ui-test','-logFile',('"' + $log + '"')) }
if ($World) { $arguments += '-storage-world-test' }
if ($ReloadWorld) { $arguments += '-storage-world-reload' }
if ($Large) { $arguments += '-storage-large-test' }
if ($ExperimentalUiInput) { $arguments += '-storage-ui-input-test' }
$windowStyle = 'Hidden'
# The user explicitly authorized a visible isolated UI test on 2026-09-20.
if ($Ui) { $windowStyle = 'Normal' }
$process = Start-Process -FilePath (Join-Path $client 'valheim.exe') -WorkingDirectory $client -WindowStyle $windowStyle -PassThru -ArgumentList $arguments
$record = [pscustomobject]@{ Id=$process.Id; Started=$process.StartTime.ToUniversalTime().ToString('O'); Executable=(Join-Path $client 'valheim.exe'); RunRoot=$runRoot; Result=(Join-Path $client 'validation-result.txt'); Log=$log; PluginSHA256=(Get-FileHash -LiteralPath (Join-Path $runRoot 'Pikalajittelu.dll') -Algorithm SHA256).Hash }
$record | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'run.json')
$record | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspace 'run.json')
$record | ConvertTo-Json

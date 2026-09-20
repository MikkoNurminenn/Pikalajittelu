param([string]$WorkPath = (Join-Path $PSScriptRoot 'work/tests'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $WorkPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
foreach ($suite in @('ConsolidationPlanTests', 'StoragePlannerTests', 'TransactionTests', 'StorageRulesTests', 'UndoHistoryTests', 'ReservationTests')) {
    $executable = Join-Path $WorkPath ($suite + '.exe')
    $sources = @('RecoveryRules.cs', 'InventoryProtection.cs', 'ConsolidationPlan.cs', 'StoragePlanner.cs', 'TransactionCoordinator.cs', 'StorageRules.cs', 'UndoGuard.cs', 'StorageHistory.cs', 'ReservationReply.cs') | ForEach-Object { Join-Path $PSScriptRoot ('src/' + $_) }
    & $compiler /nologo /target:exe /optimize+ ('/out:' + $executable) @sources (Join-Path $PSScriptRoot ('tests/' + $suite + '.cs'))
    if ($LASTEXITCODE -ne 0) { throw "Test build failed: $suite" }
    & $executable
    if ($LASTEXITCODE -ne 0) { throw "Test failed: $suite" }
}

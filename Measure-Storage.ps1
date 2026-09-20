param([string]$WorkPath = (Join-Path $PSScriptRoot 'work/benchmarks'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $WorkPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$executable = Join-Path $WorkPath 'StoragePerformance.exe'
& $compiler /nologo /target:exe /optimize+ ('/out:' + $executable) (Join-Path $PSScriptRoot 'src/RecoveryRules.cs') (Join-Path $PSScriptRoot 'src/StoragePlanner.cs') (Join-Path $PSScriptRoot 'tests/StoragePerformance.cs')
if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Benchmark invariant failed.' }

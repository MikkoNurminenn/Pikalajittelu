param([string]$GamePath = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim', [string]$OutputPath = (Join-Path $PSScriptRoot 'work/builds/Pikalajittelu2'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build.ps1') -GamePath $GamePath -OutputPath $OutputPath
$managed = Join-Path $GamePath 'valheim_Data/Managed'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$references = @('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','assembly_valheim.dll','assembly_utils.dll','UnityEngine.dll','UnityEngine.CoreModule.dll') | ForEach-Object { '/reference:' + (Join-Path $managed $_) }
$references += '/reference:' + (Join-Path $GamePath 'BepInEx/core/BepInEx.dll')
$references += '/reference:' + (Join-Path $GamePath 'BepInEx/core/0Harmony.dll')
$references += '/reference:' + (Join-Path $OutputPath 'Pikalajittelu.dll')
$references += '/reference:' + (Join-Path $managed 'UnityEngine.PhysicsModule.dll')
$references += '/reference:' + (Join-Path $managed 'SoftReferenceableAssets.dll')
$references += '/reference:' + (Join-Path $managed 'UnityEngine.ScreenCaptureModule.dll')
$references += '/reference:' + (Join-Path $managed 'UnityEngine.ImageConversionModule.dll')
$references += '/reference:' + (Join-Path $managed 'UnityEngine.AnimationModule.dll')
$references += '/reference:' + (Join-Path $managed 'UnityEngine.IMGUIModule.dll')
& $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ ('/out:' + (Join-Path $OutputPath 'Pikalajittelu.Validation.dll')) @references (Join-Path $PSScriptRoot 'tests/ValheimInventoryHarness.cs') (Join-Path $PSScriptRoot 'tests/ValheimWorldHarness.cs') (Join-Path $PSScriptRoot 'tests/ValheimPeerHarness.cs') (Join-Path $PSScriptRoot 'tests/ValheimLargeStorageHarness.cs') (Join-Path $PSScriptRoot 'tests/ValheimUiEventHarness.cs') (Join-Path $PSScriptRoot 'tests/ValheimModalInputHarness.cs')
if ($LASTEXITCODE -ne 0) { throw 'Runtime harness build failed.' }


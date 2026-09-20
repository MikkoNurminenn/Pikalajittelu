param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim',
    [string]$BepInExCore = '',
    [string]$OutputPath = (Join-Path $PSScriptRoot 'work/builds/Pikalajittelu2')
)
$ErrorActionPreference = 'Stop'
if (!$BepInExCore) { $BepInExCore = Join-Path $GamePath 'BepInEx\core' }
$managed = Join-Path $GamePath 'valheim_Data\Managed'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$destination = $OutputPath
New-Item -ItemType Directory -Force $destination | Out-Null
$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll', 'assembly_valheim.dll', 'assembly_utils.dll', 'UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.InputLegacyModule.dll', 'UnityEngine.PhysicsModule.dll') | ForEach-Object { '/reference:' + (Join-Path $managed $_) }
$references += '/reference:' + (Join-Path $BepInExCore 'BepInEx.dll')
$references += '/reference:' + (Join-Path $BepInExCore '0Harmony.dll')
$references += '/reference:' + (Join-Path $managed 'SoftReferenceableAssets.dll')
$references += '/reference:' + (Join-Path $managed 'UnityEngine.IMGUIModule.dll')
$references += '/reference:' + (Join-Path $managed 'UnityEngine.TextRenderingModule.dll')
$references += '/reference:' + (Join-Path $managed 'assembly_guiutils.dll')
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName
& $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ ('/out:' + (Join-Path $destination 'Pikalajittelu.dll')) @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Kaantaminen epaonnistui.' }

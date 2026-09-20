param(
    [Parameter(Mandatory=$true)][string]$TestedDll,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'
$binary = (Resolve-Path -LiteralPath $TestedDll).Path
if ([IO.Path]::GetFileName($binary) -ne 'Pikalajittelu.dll') { throw 'Select an explicitly tested Pikalajittelu.dll.' }
$version = '2.0.1-rc1'
$packageName = 'Pikalajittelu-' + $version
$stage = Join-Path $PSScriptRoot ('work/packages/' + [Guid]::NewGuid().ToString('N') + '/' + $packageName)
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'BepInEx/plugins/Pikalajittelu'),$OutputDirectory | Out-Null
Copy-Item -LiteralPath $binary -Destination (Join-Path $stage 'BepInEx/plugins/Pikalajittelu/Pikalajittelu.dll')
foreach ($name in @('README.md','LUEMINUT.md','TESTIT.md','CHANGELOG.md','Asenna.cmd','Asenna.ps1','Build.ps1','Build-RuntimeTests.ps1','Test.ps1','Test-Installer.ps1','Run-RuntimeTests.ps1','Run-PeerTests.ps1','Measure-Storage.ps1','New-ReleasePackage.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage
}
foreach ($name in @('assets','docs','src','tests')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage -Recurse }
$dllHash = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
[ordered]@{ name='Pikalajittelu'; version=$version; channel='release-candidate'; testedValheim='1.0.15'; pluginSha256=$dllHash;
    limitations=@('Real two-machine Steam/crossplay not verified','Full native OS mouse/keyboard interaction test incomplete','Legacy grave recovery outside validation');
    createdUtc=[DateTime]::UtcNow.ToString('O') } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stage 'package.json') -Encoding UTF8
$archive = Join-Path $OutputDirectory ($packageName + '.zip')
Compress-Archive -LiteralPath $stage -DestinationPath $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
Set-Content -LiteralPath ($archive + '.sha256') -Value ($hash + '  ' + [IO.Path]::GetFileName($archive)) -Encoding ASCII
[pscustomobject]@{ Archive=[IO.Path]::GetFullPath($archive); Staging=[IO.Path]::GetFullPath($stage); SHA256=$hash; PluginSHA256=$dllHash } | ConvertTo-Json

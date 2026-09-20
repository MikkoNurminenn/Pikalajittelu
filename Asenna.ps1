param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim',
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'RELEASE-BLOCKED.txt')) {
    throw 'Pikalajittelu 2 on kehityksessa. Asennus estetty kunnes pelissa ja moninpelissa tehtavat varmennukset on suoritettu. Katso TESTIT.md.'
}
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
if (!(Test-Path -LiteralPath (Join-Path $GamePath 'valheim.exe'))) { throw 'Valheimia ei loytynyt annetusta kansiosta.' }
$plugin = Join-Path $PSScriptRoot 'BepInEx\plugins\Pikalajittelu\Pikalajittelu.dll'
if (!(Test-Path -LiteralPath $plugin)) { throw 'Pura koko ZIP ennen asennusta.' }
$manifestPath = Join-Path $PSScriptRoot 'package.json'
if (!(Test-Path -LiteralPath $manifestPath)) { throw 'Paketin tarkistustiedosto puuttuu. Pura alkuperainen ZIP kokonaan.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.channel -ne 'release-candidate' -or $manifest.version -ne '2.0.1-rc1' -or
    (Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash -ne $manifest.pluginSha256) {
    throw 'Modipaketin versio tai tarkistussumma ei tasmaa. Asennusta ei tehty.'
}
$destination = Join-Path $GamePath 'BepInEx\plugins\Pikalajittelu'
$installed = Join-Path $destination 'Pikalajittelu.dll'
$pluginRoot = Join-Path $GamePath 'BepInEx\plugins'
if (Test-Path -LiteralPath $pluginRoot) {
    $duplicates = @(Get-ChildItem -LiteralPath $pluginRoot -Recurse -File -Filter 'Pikalajittelu.dll' |
        Where-Object { $_.FullName -ne $installed })
    if ($duplicates.Count -gt 0) { throw ('Toinen Pikalajittelu.dll loytyi. Poista vanha modiasennus ensin: ' + ($duplicates.FullName -join ', ')) }
}
if ($ValidateOnly) {
    Write-Output "Asennuspolku, paketin versio ja SHA256 OK. Peliin ei tehty muutoksia: $GamePath"
    return
}
if (Get-Process -Name valheim,valheim_server -ErrorAction SilentlyContinue) { throw 'Sulje Valheim ja paikallinen Valheim-palvelin ennen asennusta.' }
$core = Join-Path $GamePath 'BepInEx\core\BepInEx.dll'
if (!(Test-Path -LiteralPath $core)) {
    # Do not replace another loader or a partially installed mod setup.
    foreach ($existing in @('winhttp.dll', 'doorstop_config.ini', 'BepInEx')) {
        if (Test-Path -LiteralPath (Join-Path $GamePath $existing)) { throw "Kansiossa on jo modilataajan tiedostoja ($existing). Kayta nykyista modinhallintaa tai viimeistele BepInEx 5 -asennus ensin." }
    }
    $download = Join-Path $PSScriptRoot 'work'
    New-Item -ItemType Directory -Force $download | Out-Null
    $archive = Join-Path $download 'BepInExPack_Valheim-5.4.2350.zip'
    Invoke-WebRequest 'https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/5.4.2350/' -OutFile $archive -UseBasicParsing
    $expected = '37A91C000B4E88F2ED7A4BD7D812239852D2E36CBF0FF0A9F5FAACFBA46B105F'
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'BepInEx-paketin tarkistussumma ei vastaa valmistelussa tarkistettua pakettia.' }
    $extract = Join-Path $download 'BepInExPack'
    Expand-Archive -LiteralPath $archive -DestinationPath $extract -Force
    $pack = Join-Path $extract 'BepInExPack_Valheim'
    foreach ($file in @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version')) {
        Copy-Item -LiteralPath (Join-Path $pack $file) -Destination $GamePath
    }
    Copy-Item -LiteralPath (Join-Path $pack 'BepInEx') -Destination $GamePath -Recurse
}
else {
    $version = [Reflection.AssemblyName]::GetAssemblyName($core).Version
    if ($version.Major -ne 5) { throw "Modi vaatii BepInEx 5:n. Loytyi $version." }
}
New-Item -ItemType Directory -Force $destination | Out-Null
if (Test-Path -LiteralPath $installed) {
    $backup = Join-Path $PSScriptRoot ('work\backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Force $backup | Out-Null
    Copy-Item -LiteralPath $installed -Destination $backup
}
$staged = Join-Path $destination ('Pikalajittelu.' + [Guid]::NewGuid().ToString('N') + '.staged')
Copy-Item -LiteralPath $plugin -Destination $staged
if ((Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash -ne $manifest.pluginSha256) { throw 'Kopioidun modin tarkistussumma ei tasmaa. Vanhaa DLL:aa ei korvattu.' }
Move-Item -LiteralPath $staged -Destination $installed -Force
Write-Output 'Pikalajittelu 2.0.1-rc1 asennettu. Kaynnista Valheim Steamista. P = esikatsele lajittelu.'
Write-Output 'Esijulkaisu: lue LUEMINUT.md:sta testatut tilanteet ja testauksen rajat.'
Write-Output 'Lue achievement-ohje LUEMINUT.md-tiedostosta ennen pelaamista.'

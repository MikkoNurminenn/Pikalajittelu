# Development

Windows PowerShell and the .NET Framework C# compiler are used by the build scripts.
No proprietary game assemblies are checked in.

## Pure logic

Run `./Test.ps1`. It compiles and runs the six pure suites into `work/tests`.
GitHub Actions runs only these suites; a green CI result does not imply an
in-game or network test. `./Measure-Storage.ps1` runs the pure planning benchmark.

## Plugin build

Install Valheim and BepInEx 5 locally, then run:

```powershell
./Build.ps1 -GamePath 'C:\Program Files (x86)\Steam\steamapps\common\Valheim'
```

Output: `work/builds/Pikalajittelu2/Pikalajittelu.dll`. Do not install test harness
assemblies into a normal game installation.

## Isolated runtime tests

Prepare separate game copies under `work/runtime-tests/client` and, for peer
tests, `work/runtime-tests/peer-client`. Each needs its own BepInEx installation,
configuration directory and an `ISOLATED-VALIDATION` marker. The harness disables
its own cloud saves and uses separate local save paths. It can create and quit
test worlds, so the marker must never be placed in your normal game directory.

```powershell
./Run-RuntimeTests.ps1 -World
./Run-RuntimeTests.ps1 -ReloadWorld
./Run-RuntimeTests.ps1 -World -Ui
./Run-RuntimeTests.ps1 -World -Ui -Large
./Run-PeerTests.ps1 -Disconnect client
./Run-PeerTests.ps1 -Disconnect host
```

Runners refuse to start while a Valheim process is already running. `-Ui` opens
a visible test game; other runs are hidden. Test intros are skipped via native
skip functions. Peer tests listen only on loopback and do not publish a server.
`-ExperimentalUiInput` is an unsuccessful experimental probe, not a validated
input driver. See [validation](../TESTIT.md).

## Packaging

`New-ReleasePackage.ps1 -TestedDll <path>` packages a selected, already-tested DLL.
It never silently rebuilds that binary. It includes the installer, source,
brand assets, documentation and checksums. No game libraries, saved characters,
local config, raw logs or test harness DLLs belong in a release.

`Test-Installer.ps1 -PackageRoot <extracted release folder>` checks the installer
in a separate fixture game directory. It does not use a real world.

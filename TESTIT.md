# Validation: 2.0.1-rc1

Tested 20 September 2026 with Valheim 1.0.15, Unity 6000.0.75f1 and BepInEx 5.
Tests ran in separate game copies with isolated local characters/worlds and
cloud saves disabled for the test harness.

| Scope | Evidence |
| --- | --- |
| Protection and legacy planning regressions | 40,173 assertions; 500 randomized scenarios |
| Storage planner | 17,385 checks; 2,500 randomized inventories |
| Transaction error handling | 12 checks: disk errors, stale state, ownership loss, rollback failures |
| Rules persistence | 36 checks |
| Undo/history | 19 checks |
| Reservation response state | 9 checks |
| Large actual game storage | WORLD_PASS 632: 84 chests, 671 stacks, 451 transfer rows, metadata conservation and exact undo |
| Client socket disconnect during reservation, followed by retry | Host 73 / client 71 checks passed |
| Host socket disconnect during reservation, final code | Host 67 / client 71 checks passed |
| Actual world, foreign ward, rendered UI and input-blocking state | WORLD_PASS 122 |
| 2.0.1 modal input correction and full world/UI regression | WORLD_PASS 225 |
| 2.0.1 client disconnect, both sorting directions and host recovery | Host 73 / client 71 checks passed |
| 2.0.1 Windows PowerShell installer in a fixture directory | 8 checks passed |

The two-process test used native ZNet handshake, RPCs, ownership changes and ZDO
replication over loopback CustomSocket. It covered both sorting directions, a
remote peer with sorter patches disabled, an occupied chest, preview without
mutation, deposit, exact undo, late/duplicate grants and actual socket closure
during a reservation. Inventory bytes were unchanged after the rejected transfer.
The host could use the chest again after the disconnected owner was gone.

The world test covered 28 tin, partial stacks, a protected hotbar object,
metadata, ZDO serialization, full-chest swaps, stale-undo rejection and foreign
ward denial. UI screenshots covered Finnish/English, 1600×900 and 1280×720,
including 150% scaling. Native `Player.TakeInput` was blocked while the mod window
was visible and restored after closing it.

## Known gaps — do not treat this as a stable release

- No real two-machine Steam or crossplay transport test.
- No verified full mouse/keyboard path. The Windows input automation tool failed
  to start. Experimental synthetic IMGUI input also failed at the Cancel test;
  it is not recorded as a passed click test. Rendering/state tests do not replace
  a real user interaction test.
- No process-crash or power-loss durability claim. A network socket disconnect
  is different from interruption during character/world saving.
- Legacy opt-in grave recovery is outside this validation and should stay off.
- Other mods can change inventory behavior; the complete mod ecosystem is not tested.

The experimental input probe remains opt-in (`-ExperimentalUiInput`) for further
debugging. It is not enabled by the normal visual runtime test or in the plugin.

The 84-chest run measured preview 44 ms, commit 801 ms, undo 862 ms on the test
machine. Commit/undo still run on the main thread and may briefly hitch.

The 2.0.1 test injects non-zero native query results synchronously, without a
frame running while those inputs are injected. Four sets of 24 gameplay queries
verify that buttons, mouse movement, scroll, sticks and triggers are blocked in
preview/manager/closing state and restored after closing. The native
PlayerController movement/look gate is also checked, and the actual Update-driven
confirmation queue completes after the close guard. This distinguishes gameplay
input isolation from the still-unverified full OS mouse/keyboard dispatch path.

Earlier large-storage and two-direction transport figures above are baseline
2.0.0 evidence unless explicitly marked 2.0.1. The release manifest records the
exact selected 2.0.1 DLL SHA256. No game binary is rebuilt during packaging.

Before using a main world, exercise P → Cancel, P → Confirm, Left Alt+P →
search/settings, Left Shift+P → organize, Escape to close, and conditional undo
in a test world. Retain normal world and character backups.



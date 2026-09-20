# Changelog

## 2.0.1-rc1

- Fix gameplay input leaking through the storage windows: block the native
  PlayerController gate as well as Player and the gameplay ZInput queries.
- Keep Unity IMGUI input available for text fields, buttons and key capture.
- Consume closing click/Escape for the closing frame and the next frame.
- Preserve queued transfer confirmations and wait correctly before chest-rule saves.
- Add runtime fault-injection checks for pressed buttons, mouse/stick vectors,
  triggers and scroll, plus controller gating and input restoration. World/UI
  regression run passed 225 checks; this is not an OS input-driver certification.
- Publish the Imagegen emblem, banner, brand guide and bilingual documentation.

## 2.0.0-rc1

Initial local release candidate with a new preview-based storage planner.

- Inventory deposit with protected essentials and configurable keep quantities.
- Multi-chest consolidation, including exchanges between full chests.
- Chest naming, item/category rules, exclusions and nearby search.
- Finnish/English interface, scale and shortcut settings.
- Metadata-aware item identity, transaction snapshots and post-transfer checks.
- Conditional session undo and persistent transfer history.
- Native ownership reservations and suppression of stale/duplicate replies.
- Expired reservations from disconnected peers no longer permanently block a chest
  on the host; disconnected clients cannot start transfers.
- Checksummed installer and the gold/forest Pikalajittelu brand identity.

Known validation gaps remain. See [TESTIT.md](TESTIT.md).

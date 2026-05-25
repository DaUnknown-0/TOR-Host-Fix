# TOR Host Fix

An external fix plugin for [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles) (TOR) 4.8.0 that patches several host-side bugs **without modifying TOR's source**. It resolves TOR types via reflection, so it stays a no-op if TOR's internals change rather than crashing.

This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.

## Fixes

1. **Host cooldown freeze** — resets `RoleDraft.isRunning` when it gets stuck at `true`, which otherwise freezes all kill cooldowns across rounds.
2. **Host-only assignment crashes** — wraps the draft coroutine's host-only role/modifier/target assignment calls (Guesser gamemode, Lawyer target, modifiers) in a finalizer so a single exception can't kill the coroutine and leave the draft stuck.
3. **Snitch reveal misses an evil host** — TOR resets the Snitch room map at the end of `StartMeeting`'s prefix, dropping the host's early `ShareRoom` (the host starts the meeting, so it sends first). A `StartMeeting` postfix re-broadcasts the room after the reset so an evil host appears in the Snitch's reveal like everyone else.

## Features

- **Auto-update**: checks this repo's GitHub releases on the main menu and offers an in-game update button.
- **Version display**: shows `Host Fix vX.Y.Z` in the top-corner version readout — **for the host only** (this plugin only matters on the host's machine).

## Requirements

- [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles) 4.8.0 (hard dependency)
- BepInEx IL2CPP 6.0.0-be.697
- Among Us (Steam build matching your TOR version)

## Building

Unlike most TOR add-ons, this plugin does **not** reference `TheOtherRoles.dll` at compile time — it uses reflection. So a plain build is enough:

```
dotnet build -c Release
```

The output `HostFixPlugin.dll` lands in `bin/Release/net6.0/`.

To auto-copy to your Among Us install, set the `AmongUsLatest` environment variable to your Among Us folder (the one containing `Among Us.exe` and `BepInEx/`).

## Releasing

Push a tag like `v1.0.0`. The GitHub Actions workflow stamps the version into the source, builds, and publishes a release with `HostFixPlugin.dll` attached. The auto-updater picks it up from there.

## Installation

1. Install The Other Roles into your Among Us BepInEx setup.
2. Copy `HostFixPlugin.dll` into `<Among Us>/BepInEx/plugins/`.
3. Start the game.

## License

This project is licensed under the **GNU General Public License v3.0** — see [LICENSE](LICENSE).

It is a derivative work of [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles), which is also GPL-3.0. As required by the GPL, the full source of this modification is available in this repository, and any redistribution or modified version must remain under GPL-3.0.

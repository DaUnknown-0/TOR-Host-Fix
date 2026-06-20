# TOR - Hostfix

An external fix plugin for [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles) (TOR) 4.8.0 that patches several host-side bugs **without modifying TOR's source**. It resolves TOR types via reflection, so it stays a no-op if TOR's internals change rather than crashing.

This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.

## Features

1. **Host cooldown freeze** — resets `RoleDraft.isRunning` when it gets stuck at `true`, which otherwise freezes all kill cooldowns across rounds.
2. **Host-only assignment crashes** — wraps the draft coroutine's host-only role/modifier/target assignment calls (Guesser gamemode, Lawyer target, modifiers) in a finalizer so a single exception can't kill the coroutine and leave the draft stuck.
3. **Snitch reveal misses an evil host** — TOR resets the Snitch room map at the end of `StartMeeting`'s prefix, dropping the host's early `ShareRoom` (the host starts the meeting, so it sends first). A `StartMeeting` postfix re-broadcasts the room after the reset so an evil host appears in the Snitch's reveal like everyone else. The scheduled re-broadcast is cleared at every round reset (and whenever the game is no longer running), so a meeting that ends within the ~0.15 s delay can't leak a stray `ShareRoom` into the next game (fixed in 1.0.16).
- **Auto-update** — checks this repo's GitHub releases on the main menu and offers an in-game update button.
- **Version display** — shows `Hostfix vX.Y.Z` in the top-corner version readout, **for the host only** (this plugin only matters on the host's machine).

## Download & Install

1. Install The Other Roles into your Among Us BepInEx setup.
2. Download the latest `HostFixPlugin.dll` from the [Releases page](https://github.com/DaUnknown-0/TOR-Host-Fix/releases/latest).
3. Copy `HostFixPlugin.dll` into `<Among Us>/BepInEx/plugins/`.
4. Start the game.

After the first install, the in-game auto-updater checks this repo's GitHub releases on the main menu and offers an update button — manual downloads are only needed for the initial setup.

## License

This project is licensed under the **GNU General Public License v3.0** — see [LICENSE](LICENSE).

It is a derivative work of [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles), which is also GPL-3.0. As required by the GPL, the full source of this modification is available in this repository, and any redistribution or modified version must remain under GPL-3.0.

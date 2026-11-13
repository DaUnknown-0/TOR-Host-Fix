# TOR - Host Tools

A host-only plugin (formerly **TOR - Hostfix**) for [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles) (TOR) 4.8.0 that patches several host-side bugs **without modifying TOR's source**. It resolves TOR types via reflection, so it stays a no-op if TOR's internals change rather than crashing.

This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.

## Features

Only the host needs this mod. The plugin GUID, the DLL name (`HostFixPlugin.dll`) and this repository keep their old names, so existing installs update in place.

### Fixes (need only The Other Roles)

1. **Host cooldown freeze**: resets `RoleDraft.isRunning` when it gets stuck at `true`, which otherwise freezes all kill cooldowns across rounds.
2. **Host-only assignment crashes**: wraps the draft coroutine's host-only role/modifier/target assignment calls (Guesser gamemode, Lawyer target, modifiers) in a finalizer so a single exception can't kill the coroutine and leave the draft stuck.
3. **Draft safety net**: resets a draft stuck with an empty pick order and removes disconnected pickers from it.
4. **Snitch and an evil host**: an evil host was missing from the Snitch's meeting list, because the host's room reached every client before TOR cleared its room list. The host now sends its room again right after the meeting starts (config `[Fixes] SnitchHostRoom`, on by default).

### Tools (need only The Other Roles)

Since 1.1.0 the tools work on their own. With [TOR - Forgotten Fixes](https://github.com/DaUnknown-0/Useful-TOR-stuff) installed they sit in its lobby menu; without it Host Tools brings its own menu tile in the lower left corner. Every window uses the game's own menu look and blocks the game's buttons behind it while it is open.

- **Settings presets**: every mod option and the Among Us settings under one name, as many as you like; each preset can carry a range of lobby sizes and is suggested when the lobby fits. Moved here from Forgotten Fixes.
- **Settings export and import**: completes TOR's copy/paste string with a file export and an import picker. Moved here from Forgotten Fixes.
- **Web settings page**: a local page on `127.0.0.1` for editing every lobby setting in the browser. Moved here from Forgotten Fixes.
- **Lobby size above 15**: the option "Maximum Lobby Size (/size)" lets `/size` go up to 25 (experimental). Moved here from Forgotten Fixes.
- **Statistics**: every round you host is kept (`BepInEx/config/HostTools/stats.tsv`). Per player: rounds (and how many as impostor), **early deaths** (killed by someone else before the first meeting, rated from three rounds as crew or neutral: yellow from 15 %, red from 30 %), first victim, average time of death, survival, wins and kills; plus one line per map. Today, 7 days, 30 days or all.
- **Connections** (needs Forgotten Fixes, every player has to answer the host's ping): a traffic light per player from timed round trips and the drops of the evening, and a notice as soon as a player turns red.
- **Start check**: one list of everything that stands in the way of a start.
- **Settings log**: what changed since the last round, and the last ten rounds to go back to.
- **Evening plan**: an order of presets with a number of rounds each; the next one is suggested after its rounds, nothing loads by itself.
- **Player notes**: private notes on players, shown to the host when the player joins.
- **Settings rescue**: when you are host again and the settings differ from those you last hosted with, yours are offered.
- **Lobby guard**: your own block list, and a hint when somebody sits in the lobby without Forgotten Fixes.

### Also

- **Auto-update**: checks this repo's GitHub releases on the main menu and offers an in-game update button.
- **Version display**: shows `Host Tools vX.Y.Z` in the top-corner version readout, **for the host only**.

## Download & Install

1. Install The Other Roles into your Among Us BepInEx setup.
2. Download the latest `HostFixPlugin.dll` from the [Releases page](https://github.com/DaUnknown-0/TOR-Host-Fix/releases/latest).
3. Copy `HostFixPlugin.dll` into `<Among Us>/BepInEx/plugins/`.
4. Start the game.

After the first install, the in-game auto-updater checks this repo's GitHub releases on the main menu and offers an update button — manual downloads are only needed for the initial setup.

## License

This project is licensed under the **GNU General Public License v3.0** — see [LICENSE](LICENSE).

It is a derivative work of [The Other Roles](https://github.com/TheOtherRolesAU/TheOtherRoles), which is also GPL-3.0. As required by the GPL, the full source of this modification is available in this repository, and any redistribution or modified version must remain under GPL-3.0.

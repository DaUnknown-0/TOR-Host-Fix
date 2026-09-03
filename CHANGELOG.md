# Changelog — TOR - Hostfix

## Unreleased

### Bug-Audit 2026-09-03
- **Draft-Picker-Entfernung** (`HostFixPlugin.cs`, Fix 3 `RemoveDisconnectedPicker`): disconnectete
  Spieler wurden nur entfernt, wenn sie ganz vorne in der Draft-pickOrder standen. TORs eigener
  Draft-Code iteriert aber die komplette Liste und warf weiterhin, wenn ein disconnecteter Spieler
  weiter hinten in der Warteschlange stand. Jetzt wird jede Position geprüft, nicht nur der Kopf.
- **Updater-Fehlerbehandlung** (`HostFixUpdater.cs`): ein fehlgeschlagener Schreibvorgang wurde als
  Erfolg gemeldet, obwohl die alte DLL bereits nach `.old` verschoben war (die beim nächsten Start
  gelöscht wird), sodass am Ende keine funktionsfähige HostFix-Datei mehr übrig war. Jetzt sorgt
  eine `IsCompletedSuccessfully`-Prüfung nach dem Schreiben, try/catch um den Move der alten Datei,
  ein Rollback auf die alte Datei bei Fehlschlag, das Löschen einer halb geschriebenen Datei ohne
  Vorgänger und ein Null-Guard für ein fehlendes Release-Asset dafür, dass ein misslungenes Update
  wieder ein No-op bleibt statt den Mod lahmzulegen.

### Performance (Audit 2026-09-01)
- **Versionszeile** (`VersionDisplayPatch`): die HUD-Zeile wurde jeden Frame neu formatiert, nur
  um sie mit ihrem Cache zu vergleichen. Jetzt entscheiden die Identität der übersetzten Vorlage
  und der Testversionen-Schalter, wann neu gebaut wird.
- **Collective-HUD-Zeile** (`UnknownsCollective.cs`, in allen fünf Mods gleich): Block gecacht,
  neu nur bei geänderter Mitgliedszeile, Anzahl, Auf-/Zuklappen oder Lobby-/Runden-Wechsel.

## 1.0.16

Patch bump. Host-only plugin; no cross-client wire changes.

### P0 — Crash / correctness
- **P0.1** — `CoShowAnnouncement`: `yield return null;` → `yield break;` and a null-guard on
  `MainMenuManager` before `StartCoroutine(...)` (announcement coroutine + `OnSceneLoaded`),
  preventing `Instantiate(null)` and a NRE.
- **P0.2** — `CoCheckForUpdate`: GitHub deserialize + sort wrapped in try/catch/finally so a
  rate-limit/malformed response can no longer kill the coroutine and wedge `_busy`/`_checkCompleted`
  for the session. `Releases == null` treated as "no update"; all exits reset the flags.
- **P0.4** — `SnitchHostRoomFix` gains `Reset()` (clears the pending re-broadcast). It is called
  from `ResetVariablesPatch.Postfix` (TOR's per-round reset) and whenever `HudManagerSafetyNet`
  sees `GameState != Started`, so a `ShareRoom` scheduled in a meeting that ends within the 0.15 s
  delay (or after a host leave) can no longer fire a bogus re-broadcast into the next game.

### P2 — QoL / hygiene
- **P2.3** — PingTracker version line guarded by its `hostFixCredits` marker against per-frame
  stacking.
- **P2.8** — Updater sends a `User-Agent` header on the GitHub API request.

### Feature support
- **F2** — added `GetReleaseNotes()` to the updater so UsefulTORStuff's Mod Manager can show Host
  Fix's release notes and include it in "Update All". HostFix remains **excluded from the F1
  combined lobby handshake** by design (host-only).

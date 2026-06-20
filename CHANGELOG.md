# Changelog — TOR - Hostfix

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

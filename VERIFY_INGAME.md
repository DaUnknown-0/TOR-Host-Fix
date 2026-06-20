# In-game verification checklist — TOR - Hostfix 1.0.16

## P0.4 — Stale Snitch re-broadcast across games (host)
- [ ] As host with a Snitch in play, start a meeting and **end the game within ~0.15 s** of the
      meeting starting (or have the host leave right after). Start a new game; confirm **no** stray
      `[Fix4] Re-broadcast host room …` fires at round start.
- [ ] Normal Snitch flow still works: the host re-broadcast lands within the reveal window when a
      meeting runs its course (`[Fix4] Re-broadcast host room …` appears as before).
- [ ] Confirm `SnitchHostRoomFix.Reset()` runs at round reset (per-round `resetVariables`) — no
      pending state survives between games.

## Regression sweep
- [ ] Fix 1 (`isRunning` stuck → reset) and Fix 2 (role-assignment finalizer) still behave.
- [ ] Host-only "Hostfix vX" line still shows once in the version block.

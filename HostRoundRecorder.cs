// TOR Host Tools - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.

/*
 * HostRoundRecorder - records every round this client hosts for HostStats, on its own (User 2026-11-13: Host Tools has
 * to work without Forgotten Fixes). The same rules as Forgotten Fixes' DeathTimeHistory, from which it is ported:
 *
 *   round clock   starts when the intro ends (10 s fallback); gameplay time leaves every meeting and exile out
 *   participants  everybody alive and connected at the clock start; meetings survived count at each meeting end
 *   deaths        TOR's GameHistory.deadPlayers, read in an OnGameEnd PREFIX (TOR's statics are still intact there),
 *                 the first entry per player; "killed by somebody else" = Kill/Guess/Bomb/Arson/WitchExile with a
 *                 killer who is not the victim
 *   roles, team   RoleInfo.getRoleInfoForPlayer (main role, modifiers left out), Unknown's Collection's roles included
 *   winners       from the end screen (EndGameManager.SetEverythingUp), after TOR and the mods rewrote them
 *
 * Hide and Seek and Prop Hunt are left out (dying is the game there), as are freeplay and rounds under 30 s.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TheOtherRoles;
using UnityEngine;

namespace HostFixPlugin;

internal static class HostRoundRecorder
{
    internal const int ReasonSurvived = -1;
    private const double MinRoundSeconds = 30;
    private const float IntroFallbackSeconds = 10f;

    internal sealed class Entry
    {
        public string Code = "", Name = "", Role = "", KillerCode = "";
        public byte Team;          // 0 crew, 1 impostor, 2 neutral
        public int Reason = ReasonSurvived, Meetings, Kills;
        public float Share = 1f, DeathSeconds = -1f;
        public bool Won;
    }

    internal sealed class Record
    {
        public DateTime Start, End;
        public float GameplaySeconds;
        public int MapId = -1;
        public string MapName = "";
        public List<Entry> Entries;
    }

    internal static bool IsKillReason(int r) =>
        r == (int)DeadPlayer.CustomDeathReason.Kill || r == (int)DeadPlayer.CustomDeathReason.Guess
        || r == (int)DeadPlayer.CustomDeathReason.Bomb || r == (int)DeadPlayer.CustomDeathReason.Arson
        || r == (int)DeadPlayer.CustomDeathReason.WitchExile;

    private sealed class Participant { public string Code, Name; public int Meetings; }

    private static readonly Dictionary<byte, Participant> participants = new();
    private static readonly List<DateTime[]> meetings = new();
    private static DateTime? roundStart, meetingOpen;
    private static bool roundSeen, introOverSeen;
    private static float roundSeenAt;
    private static DateTime introOverAt;
    private static Record pending;

    private static bool Hosting()
    {
        try
        {
            var c = AmongUsClient.Instance;
            return c != null && c.AmHost && c.NetworkMode != NetworkModes.FreePlay;
        }
        catch { return false; }
    }

    private static bool CountingMode()
    {
        try
        {
            var mode = typeof(CustomOption).Assembly.GetType("TheOtherRoles.TORMapOptions")?.GetField("gameMode")?.GetValue(null)?.ToString();
            return mode == null || (mode != "HideNSeek" && mode != "PropHunt");
        }
        catch { return true; }
    }

    /// <summary>Per frame (HostToolsUI): round clock, meetings.</summary>
    internal static void Tick()
    {
        try
        {
            var client = AmongUsClient.Instance;
            bool inRound = client != null && ShipStatus.Instance != null && client.IsGameStarted;
            if (!inRound) { roundSeen = false; return; }   // the clock survives until OnGameEnd (the state flips to Ended first)
            if (!roundSeen)
            {
                Reset();
                roundSeen = true;
                roundSeenAt = Time.realtimeSinceStartup;
            }
            if (roundStart == null)
            {
                if (introOverSeen) StartRound(introOverAt);
                else if (Time.realtimeSinceStartup - roundSeenAt >= IntroFallbackSeconds) StartRound(DateTime.UtcNow);
                return;
            }
            bool meeting = MeetingHud.Instance != null || ExileController.Instance != null;
            if (meeting && meetingOpen == null) meetingOpen = DateTime.UtcNow;
            else if (!meeting && meetingOpen != null)
            {
                meetings.Add(new[] { meetingOpen.Value, DateTime.UtcNow });
                meetingOpen = null;
                foreach (var kv in participants)
                {
                    var p = Helpers.playerById(kv.Key);
                    if (p != null && p.Data != null && !p.Data.IsDead && !p.Data.Disconnected) kv.Value.Meetings++;
                }
            }
        }
        catch { }
    }

    private static void Reset()
    {
        introOverSeen = false;
        roundStart = null;
        meetingOpen = null;
        meetings.Clear();
        participants.Clear();
    }

    private static void StartRound(DateTime at)
    {
        roundStart = at;
        participants.Clear();
        foreach (var p in PlayerControl.AllPlayerControls.ToArray())
        {
            if (p == null || p.Data == null || p.Data.IsDead || p.Data.Disconnected) continue;
            string code = PlayerIdentity.KeyOf(p);
            if (string.IsNullOrEmpty(code)) continue;
            participants[p.PlayerId] = new Participant { Code = code, Name = p.Data.PlayerName };
        }
    }

    private static double GameplayAt(DateTime t)
    {
        if (roundStart == null || t <= roundStart.Value) return 0;
        DateTime s0 = roundStart.Value;
        double total = (t - s0).TotalSeconds;
        foreach (var m in meetings) total -= Overlap(s0, t, m[0], m[1]);
        if (meetingOpen != null) total -= Overlap(s0, t, meetingOpen.Value, t);
        return Math.Max(0, total);
    }

    private static double Overlap(DateTime a0, DateTime a1, DateTime b0, DateTime b1)
    {
        DateTime s = a0 > b0 ? a0 : b0, e = a1 < b1 ? a1 : b1;
        return e > s ? (e - s).TotalSeconds : 0;
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
    private static class IntroOver
    {
        public static void Prefix()
        {
            if (introOverSeen) return;
            introOverSeen = true;
            introOverAt = DateTime.UtcNow;
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    private static class GameEnd
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            try { Capture(); }
            catch (Exception e) { HostFixPlugin.Logger?.LogWarning($"[HostStats] round capture failed: {e.Message}"); }
        }
    }

    [HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.SetEverythingUp))]
    private static class EndScreen
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            try { Finish(); }
            catch (Exception e) { HostFixPlugin.Logger?.LogWarning($"[HostStats] round end failed: {e.Message}"); }
        }
    }

    private static List<DeadPlayer> TorDeadPlayers()
    {
        try
        {
            return typeof(CustomOption).Assembly.GetType("TheOtherRoles.GameHistory")
                ?.GetField("deadPlayers", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?.GetValue(null) as List<DeadPlayer>;
        }
        catch { return null; }
    }

    private static (byte, string) TeamAndRole(PlayerControl p)
    {
        try
        {
            var info = RoleInfo.getRoleInfoForPlayer(p, false)?.FirstOrDefault(i => i != null && !i.isModifier);
            if (info != null) return (info.isImpostor ? (byte)1 : info.isNeutral ? (byte)2 : (byte)0, info.name ?? "?");
        }
        catch { }
        bool imp = p.Data?.Role != null && p.Data.Role.IsImpostor;
        return (imp ? (byte)1 : (byte)0, imp ? "Impostor" : "Crewmate");
    }

    private static void Capture()
    {
        if (roundStart == null || participants.Count == 0 || !Hosting() || !CountingMode()) { roundStart = null; return; }
        DateTime end = DateTime.UtcNow;
        double length = GameplayAt(end);
        if (length < MinRoundSeconds) { roundStart = null; return; }

        var deaths = new Dictionary<byte, DeadPlayer>();
        foreach (var dp in TorDeadPlayers() ?? new List<DeadPlayer>())
            if (dp?.player != null && !deaths.ContainsKey(dp.player.PlayerId)) deaths[dp.player.PlayerId] = dp;

        var entries = new List<Entry>();
        foreach (var kv in participants)
        {
            var p = Helpers.playerById(kv.Key);
            if (p == null || p.Data == null) continue;
            var (team, role) = TeamAndRole(p);
            var e = new Entry { Code = kv.Value.Code, Name = kv.Value.Name, Team = team, Role = role, Meetings = kv.Value.Meetings };
            foreach (var dp in deaths.Values)
                if (dp.killerIfExisting != null && dp.killerIfExisting.PlayerId == kv.Key && dp.player.PlayerId != kv.Key
                    && IsKillReason((int)dp.deathReason)) e.Kills++;
            if (deaths.TryGetValue(kv.Key, out var mine))
            {
                e.Reason = (int)mine.deathReason;
                if (mine.killerIfExisting != null)
                    e.KillerCode = mine.killerIfExisting.PlayerId == kv.Key ? kv.Value.Code
                        : participants.TryGetValue(mine.killerIfExisting.PlayerId, out var kp) ? kp.Code
                        : PlayerIdentity.KeyOf(mine.killerIfExisting);
                bool dc = mine.deathReason == DeadPlayer.CustomDeathReason.Disconnect;
                e.Share = dc ? -1f : (float)Math.Clamp(GameplayAt(mine.timeOfDeath) / length, 0.0, 1.0);
                if (!dc) e.DeathSeconds = (float)Math.Clamp(GameplayAt(mine.timeOfDeath), 0.0, length);
            }
            else if (p.Data.Disconnected) { e.Reason = (int)DeadPlayer.CustomDeathReason.Disconnect; e.Share = -1f; }
            entries.Add(e);
        }
        var (mapId, mapName) = MapNow();
        pending = new Record { Start = roundStart.Value, End = end, GameplaySeconds = (float)length, MapId = mapId, MapName = mapName, Entries = entries };
        roundStart = null;   // one record per round, even if OnGameEnd fires twice
    }

    private static void Finish()
    {
        if (pending == null) return;
        var r = pending;
        pending = null;
        var winners = new HashSet<string>();
        try
        {
            // Unknown's Collection's Bug: during its two-phase end screen the real sole winner comes through this key
            if (AppDomain.CurrentDomain.GetData("UC.EndGame.SoleWinner") is string sole && sole.Length > 0) winners.Add(sole);
            else foreach (var w in EndGameResult.CachedWinners.ToArray())
                    if (w != null && !string.IsNullOrEmpty(w.PlayerName)) winners.Add(w.PlayerName);
        }
        catch { return; }
        foreach (var e in r.Entries) e.Won = winners.Contains(e.Name);
        HostStats.OnRound(r);
    }

    // the map from the ship (a random map setting overrides the lobby's); Atlas/Submerged: id -1 and the ship's name
    private static (int, string) MapNow()
    {
        try
        {
            var ship = ShipStatus.Instance;
            if (ship == null) return (-1, "");
            string cls = "";
            try { cls = ship.GetIl2CppType().Name; } catch { }
            switch (cls)
            {
                case "SkeldShipStatus": return ship.transform.localScale.x < 0f ? (3, "dlekS") : (0, "The Skeld");
                case "MiraShipStatus": return (1, "MIRA HQ");
                case "PolusShipStatus": return (2, "Polus");
                case "AirshipStatus": return (4, "Airship");
                case "FungleShipStatus": return (5, "The Fungle");
            }
            string n = ship.gameObject.name.Replace("(Clone)", "").Trim();
            return (-1, n.Length > 0 ? n : cls);
        }
        catch { return (-1, ""); }
    }
}

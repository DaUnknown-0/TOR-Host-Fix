// TOR Host Tools - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.

/*
 * HostStats - a long-term statistic of every round this player hosted (User 2026-11-13: "general statistics in Host
 * Tools, it does not have to track everything; early deaths are very important").
 *
 * WHERE THE DATA COMES FROM
 * HostRoundRecorder records every round this client hosts (TOR's GameHistory at the game end, the winners from the
 * end screen; ported from Forgotten Fixes' DeathTimeHistory, so Host Tools needs no other mod). Each round is appended
 * to BepInEx/config/HostTools/stats.tsv for good, one line per player and round. Only hosted rounds count: the
 * statistic belongs to the lobbies one runs.
 *
 * WHAT COUNTS AS AN EARLY DEATH
 *   early death   killed by somebody else (kill, guess, bomb, arson, witch) before the first meeting of the round
 *   first victim  the first player of the round killed by somebody else
 * The early-death rate is taken over the rounds a player was NOT an impostor: impostors almost never die early, and
 * counting their rounds would make a player who is often impostor look safe.
 *
 * Nothing is sent.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HostFixPlugin;

internal static class HostStats
{
    internal sealed class Entry
    {
        public long Start, End;          // unix seconds UTC
        public float Length;             // gameplay seconds of the round
        public int MapId;
        public string Map = "";
        public int Players;
        public string Code = "", Name = "", Role = "", Killer = "";
        public byte Team;                // 0 crew, 1 impostor, 2 neutral
        public bool Won;
        public int Reason;               // HostRoundRecorder.ReasonSurvived or the TOR death reason
        public int Meetings, Kills;
        public float Share = -1f;        // survived share of the gameplay time
        public float DeathAt = -1f;      // gameplay seconds at death
        public bool First;               // first player of the round killed by somebody else

        public bool Survived => Reason == HostRoundRecorder.ReasonSurvived;
        public bool Disconnected => Reason == (int)TheOtherRoles.DeadPlayer.CustomDeathReason.Disconnect;

        public bool KilledBySomeone =>
            !Survived && HostRoundRecorder.IsKillReason(Reason) && Killer.Length > 0 && Killer != Code;

        public bool Early => KilledBySomeone && Meetings == 0;
    }

    internal sealed class PlayerRow
    {
        public string Code = "", Name = "";
        public int Rounds, NonImp, Early, First, Wins, Kills, Deaths, Imp;
        public double ShareSum; public int ShareN;
        public double DeathAtSum; public int DeathAtN;
        public float EarlyRate => NonImp == 0 ? 0f : Early / (float)NonImp;
        public float WinRate => Rounds == 0 ? 0f : Wins / (float)Rounds;
        public float AvgShare => ShareN == 0 ? -1f : (float)(ShareSum / ShareN);
        public float AvgDeathAt => DeathAtN == 0 ? -1f : (float)(DeathAtSum / DeathAtN);
    }

    internal sealed class MapRow
    {
        public string Map = "";
        public int Rounds, NonImpSeats, Early;
        public double LengthSum;
        public float EarlyRate => NonImpSeats == 0 ? 0f : Early / (float)NonImpSeats;
        public float AvgLength => Rounds == 0 ? 0f : (float)(LengthSum / Rounds);
    }

    private static readonly List<Entry> entries = new();
    private static bool loaded, hooked;
    internal static int Version { get; private set; }

    private static string FilePath => HostToolsFiles.PathOf("stats.tsv");

    // ---------------------------------------------------------------- recording
    /// <summary>A finished hosted round (HostRoundRecorder, after the winners are known).</summary>
    internal static void OnRound(HostRoundRecorder.Record r)
    {
        if (r == null || r.Entries == null || r.Entries.Count == 0) return;
        EnsureLoaded();
        long start = new DateTimeOffset(r.Start).ToUnixTimeSeconds(), end = new DateTimeOffset(r.End).ToUnixTimeSeconds();
        if (entries.Any(e => e.End == end)) return;      // the same round twice (end screen set up again)

        var round = r.Entries.Select(x => new Entry
        {
            Start = start, End = end, Length = r.GameplaySeconds, MapId = r.MapId, Map = MapLabel(r.MapId, r.MapName),
            Players = r.Entries.Count, Code = x.Code ?? "", Name = x.Name ?? "", Role = x.Role ?? "", Killer = x.KillerCode ?? "",
            Team = x.Team, Won = x.Won, Reason = x.Reason, Meetings = x.Meetings, Kills = x.Kills, Share = x.Share, DeathAt = x.DeathSeconds
        }).ToList();
        // first victim: the earliest kill by somebody else (by time, else by survived share)
        var first = round.Where(e => e.KilledBySomeone).OrderBy(e => e.DeathAt >= 0 ? e.DeathAt : e.Share * 100000f).FirstOrDefault();
        if (first != null) first.First = true;

        entries.AddRange(round);
        Version++;
        try { File.AppendAllLines(FilePath, round.Select(Format)); }
        catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[HostStats] save failed: {e.Message}"); }
        HostFixPlugin.Logger.LogInfo($"[HostStats] round kept: {round.Count} player(s), map {round[0].Map}, "
                                     + $"{round.Count(e => e.Early)} early death(s), first victim {first?.Name ?? "-"}.");
    }

    internal static string MapLabel(int id, string name) => id switch
    {
        0 => "The Skeld", 1 => "MIRA HQ", 2 => "Polus", 3 => "dlekS", 4 => "Airship", 5 => "The Fungle",
        _ => string.IsNullOrEmpty(name) ? "?" : name,
    };

    // ---------------------------------------------------------------- file
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // start, end, length, map id, map, players, code, name, team, role, won, reason, killer, meetings, kills, share, death at, first
    private static string Format(Entry e) => string.Join("\t",
        e.Start.ToString(Inv), e.End.ToString(Inv), e.Length.ToString("0.#", Inv), e.MapId.ToString(Inv), HostToolsFiles.Field(e.Map),
        e.Players.ToString(Inv), HostToolsFiles.Field(e.Code), HostToolsFiles.Field(e.Name), e.Team.ToString(Inv), HostToolsFiles.Field(e.Role),
        e.Won ? "1" : "0", e.Reason.ToString(Inv), HostToolsFiles.Field(e.Killer), e.Meetings.ToString(Inv), e.Kills.ToString(Inv),
        e.Share.ToString("0.###", Inv), e.DeathAt.ToString("0.#", Inv), e.First ? "1" : "0");

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var f = line.Split('\t');
                if (f.Length < 18 || !long.TryParse(f[0], NumberStyles.Integer, Inv, out long start)) continue;
                entries.Add(new Entry
                {
                    Start = start, End = L(f[1]), Length = F(f[2]), MapId = I(f[3]), Map = f[4], Players = I(f[5]),
                    Code = f[6], Name = f[7], Team = (byte)I(f[8]), Role = f[9], Won = f[10] == "1", Reason = I(f[11]),
                    Killer = f[12], Meetings = I(f[13]), Kills = I(f[14]), Share = F(f[15]), DeathAt = F(f[16]), First = f[17] == "1",
                });
            }
            Version++;
            HostFixPlugin.Logger.LogInfo($"[HostStats] loaded {entries.Select(e => e.End).Distinct().Count()} round(s).");
        }
        catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[HostStats] load failed: {e.Message}"); }
    }

    private static long L(string s) => long.TryParse(s, NumberStyles.Integer, Inv, out var v) ? v : 0;
    private static int I(string s) => int.TryParse(s, NumberStyles.Integer, Inv, out var v) ? v : 0;
    private static float F(string s) => float.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : -1f;

    // ---------------------------------------------------------------- evaluation
    internal enum Period { Today, Week, Month, All }

    internal static List<Entry> In(Period p)
    {
        EnsureLoaded();
        if (p == Period.All) return entries.ToList();
        DateTime from;
        if (p == Period.Today)
        {
            // "today" starts at 6 in the morning: an evening running past midnight stays one evening
            var six = DateTime.Now.Date.AddHours(6);
            from = DateTime.Now >= six ? six : six.AddDays(-1);
        }
        else from = DateTime.Now.AddDays(p == Period.Week ? -7 : -30);
        long cut = new DateTimeOffset(from.ToUniversalTime()).ToUnixTimeSeconds();
        return entries.Where(e => e.End >= cut).ToList();
    }

    internal static int RoundCount(List<Entry> list) => list.Select(e => e.End).Distinct().Count();

    internal static List<PlayerRow> Players(List<Entry> list)
    {
        var rows = new Dictionary<string, PlayerRow>();
        foreach (var e in list.OrderBy(e => e.End))
        {
            if (e.Code.Length == 0) continue;
            if (!rows.TryGetValue(e.Code, out var r)) rows[e.Code] = r = new PlayerRow { Code = e.Code };
            r.Name = e.Name;                                    // the newest name
            r.Rounds++;
            if (e.Team == 1) r.Imp++; else r.NonImp++;
            if (e.Early && e.Team != 1) r.Early++;
            if (e.First) r.First++;
            if (e.Won) r.Wins++;
            r.Kills += e.Kills;
            if (!e.Survived && !e.Disconnected) r.Deaths++;
            if (e.Share >= 0 && !e.Disconnected) { r.ShareSum += e.Share; r.ShareN++; }
            if (e.KilledBySomeone && e.DeathAt >= 0) { r.DeathAtSum += e.DeathAt; r.DeathAtN++; }
        }
        return rows.Values.ToList();
    }

    internal static List<MapRow> Maps(List<Entry> list)
    {
        var rows = new Dictionary<string, MapRow>();
        foreach (var g in list.GroupBy(e => e.End))
        {
            var any = g.First();
            if (!rows.TryGetValue(any.Map, out var r)) rows[any.Map] = r = new MapRow { Map = any.Map };
            r.Rounds++;
            r.LengthSum += Math.Max(0f, any.Length);
            foreach (var e in g)
            {
                if (e.Team == 1) continue;
                r.NonImpSeats++;
                if (e.Early) r.Early++;
            }
        }
        return rows.Values.OrderByDescending(m => m.Rounds).ToList();
    }

    /// <summary>Overall: rounds, early deaths per non-impostor seat, impostor/crew/neutral win shares.</summary>
    internal static (int rounds, float earlyRate, float impWin, float crewWin, float neutralWin, float avgLength) Overall(List<Entry> list)
    {
        int rounds = 0, seats = 0, early = 0, imp = 0, crew = 0, neutral = 0;
        double length = 0;
        foreach (var g in list.GroupBy(e => e.End))
        {
            rounds++;
            length += Math.Max(0f, g.First().Length);
            foreach (var e in g) if (e.Team != 1) { seats++; if (e.Early) early++; }
            // the team that won: the team of most winners (a neutral solo win has one winner)
            var w = g.Where(e => e.Won).ToList();
            if (w.Count == 0) continue;
            int team = w.GroupBy(e => e.Team).OrderByDescending(x => x.Count()).First().Key;
            if (team == 1) imp++; else if (team == 2) neutral++; else crew++;
        }
        float R(int n) => rounds == 0 ? 0f : n / (float)rounds;
        return (rounds, seats == 0 ? 0f : early / (float)seats, R(imp), R(crew), R(neutral), rounds == 0 ? 0f : (float)(length / rounds));
    }

    /// <summary>Autotest: a few made-up rounds, only in memory.</summary>
    internal static void DiagSample()
    {
        EnsureLoaded();
        var rnd = new Random(7);
        string[] names = { "Mo", "Blurple", "Lena", "Tom", "Kai", "Ana", "Jo", "Ben" };
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (int round = 0; round < 12; round++)
        {
            long end = now - round * 900;
            int impA = rnd.Next(names.Length), impB = (impA + 3) % names.Length;
            bool crewWin = rnd.Next(2) == 0;
            for (int i = 0; i < names.Length; i++)
            {
                bool imp = i == impA || i == impB;
                bool dies = !imp && rnd.Next(3) == 0;
                bool early = dies && (i == 2 ? rnd.Next(2) == 0 : rnd.Next(5) == 0);
                entries.Add(new Entry
                {
                    Start = end - 700, End = end, Length = 600, MapId = round % 3, Map = MapLabel(round % 3, ""), Players = names.Length,
                    Code = "diag:" + names[i], Name = names[i], Team = (byte)(imp ? 1 : 0), Won = imp ? !crewWin : crewWin,
                    Reason = dies ? (int)TheOtherRoles.DeadPlayer.CustomDeathReason.Kill : HostRoundRecorder.ReasonSurvived,
                    Killer = dies ? "diag:" + names[impA] : "", Meetings = early ? 0 : dies ? 2 : 3, Kills = imp ? rnd.Next(3) : 0,
                    Share = dies ? (early ? 0.1f : 0.6f) : 1f, DeathAt = dies ? (early ? 45f : 330f) : -1f, First = early && i == 2,
                });
            }
        }
        Version++;
    }
}

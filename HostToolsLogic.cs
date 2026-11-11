// TOR Host Tools - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.

/*
 * HostToolsLogic - the host tools that are new in this mod (User 2026-11-11). The panel is HostToolsUI; this file
 * holds what they do. All of it is host-only and local: files in BepInEx/config/HostTools, lines in the host's own
 * chat, entries in the lobby menu. Nothing is sent to anyone except what a loaded preset sends anyway and the kick
 * of the lobby guard.
 *
 *   StartCheck      one list of everything that stands in the way of a start, collected from the places where the
 *                   single mods report it today (the text above the start button, the mod check, the connection
 *                   lights, players still connecting).
 *   SettingsLog     a snapshot of every option at each round start (the last ten). Shows what changed since the
 *                   last round and puts an older round's settings back.
 *   SettingsRescue  the settings as they were when this player last hosted (round start or leaving the lobby as
 *                   host). When he is host again and the settings differ, he is offered them: after the host was
 *                   lost and the lobby moved on with someone else's, or after a restart.
 *   EveningPlan     an order of steps "this preset for N rounds". After each round the next step is suggested;
 *                   nothing loads by itself.
 *   HostNotes       the host's private notes on players (by PlayerIdentity, so a new name keeps the note), shown
 *                   to him in his chat when the player joins.
 *   LobbyGuard      the host's own block list (joins are removed again) and a hint when somebody has sat in the
 *                   lobby for a while without TOR - Forgotten Fixes.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UsefulTORStuff;

namespace HostFixPlugin;

internal static class HostToolsFiles
{
    internal static string Dir => Path.Combine(BepInEx.Paths.ConfigPath, "HostTools");

    internal static string PathOf(string name)
    {
        Directory.CreateDirectory(Dir);
        return Path.Combine(Dir, name);
    }

    internal static bool HostOnline()
    {
        try
        {
            var c = AmongUsClient.Instance;
            return c != null && c.AmHost && c.NetworkMode != NetworkModes.FreePlay;
        }
        catch { return false; }
    }

    internal static bool HostInLobby() => HostOnline() && LobbyScreen.Exists;

    /// <summary>A line in the host's own chat (nobody else sees it).</summary>
    internal static void Chat(string text)
    {
        try
        {
            var hud = HudManager.Instance;
            if (hud != null && hud.Chat != null && PlayerControl.LocalPlayer != null) hud.Chat.AddChat(PlayerControl.LocalPlayer, text);
        }
        catch { }
    }

    internal static string Field(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim();

    internal static string NameOf(PlayerControl p)
    {
        try { return p != null && p.Data != null && !string.IsNullOrEmpty(p.Data.PlayerName) ? p.Data.PlayerName : "?"; }
        catch { return "?"; }
    }

    internal static string KeyOf(PlayerControl p)
    {
        try
        {
            string k = PlayerIdentity.KeyOf(p);
            if (!string.IsNullOrEmpty(k)) return k;
        }
        catch { }
        string n = NameOf(p);
        return n == "?" ? null : "name:" + n;
    }

    /// <summary>The other players of the lobby that have a character already.</summary>
    internal static List<InnerNet.ClientData> Others()
    {
        var list = new List<InnerNet.ClientData>();
        try
        {
            var c = AmongUsClient.Instance;
            if (c == null || c.allClients == null) return list;
            for (int i = 0; i < c.allClients.Count; i++)
            {
                var cl = c.allClients[i];
                if (cl == null || cl.Id == c.ClientId || cl.Character == null) continue;
                list.Add(cl);
            }
        }
        catch { }
        return list;
    }
}

// ====================================================================================================== start check
internal static class StartCheck
{
    internal enum Kind { Block, Warn, Info }

    internal sealed class Item
    {
        public Kind Kind;
        public string Text;
    }

    private static List<Item> cache = new();
    private static float cachedAt = -10f;

    /// <summary>The list, at most a second old (the lobby menu asks every frame for its label).</summary>
    internal static List<Item> Cached()
    {
        if (Time.realtimeSinceStartup - cachedAt > 1f) { cachedAt = Time.realtimeSinceStartup; cache = Collect(); }
        return cache;
    }

    private static string Plain(string s) => Regex.Replace(s ?? "", "<[^>]*>", "").Trim();

    private static void Add(List<Item> list, Kind kind, string text)
    {
        text = Plain(text);
        if (text.Length == 0 || list.Any(i => i.Text == text)) return;
        list.Add(new Item { Kind = kind, Text = text });
    }

    internal static List<Item> Collect()
    {
        var list = new List<Item>();
        try
        {
            var c = AmongUsClient.Instance;
            if (c == null || c.allClients == null) return list;

            // 1. still connecting: no character yet
            for (int i = 0; i < c.allClients.Count; i++)
            {
                var cl = c.allClients[i];
                if (cl != null && cl.Id != c.ClientId && cl.Character == null)
                    Add(list, Kind.Block, HFLocalization.Tr("hostfix.tools.check_connecting", string.IsNullOrEmpty(cl.PlayerName) ? "#" + cl.Id : cl.PlayerName));
            }

            // 2. what the single mods say above the start button (their own start gates write there)
            var gsm = LobbyScreen.InstanceOrNull();
            bool countdown = false;
            try { countdown = gsm != null && gsm.startState == GameStartManager.StartingStates.Countdown; } catch { }
            if (gsm != null && !countdown)
            {
                try
                {
                    if (gsm.GameStartTextParent != null && gsm.GameStartTextParent.activeInHierarchy && gsm.GameStartText != null)
                        foreach (var line in (gsm.GameStartText.text ?? "").Split('\n'))
                            Add(list, Kind.Block, line);
                }
                catch { }
                try
                {
                    var col = gsm.StartButton != null ? gsm.StartButton.GetComponent<Collider2D>() : null;
                    if (col != null && !col.enabled && !LobbyPanelGuard.Blocking())
                        Add(list, Kind.Block, HFLocalization.Tr("hostfix.tools.check_button_locked"));
                }
                catch { }
            }

            // 3. the mod check of Forgotten Fixes: who lacks it or has another version
            try
            {
                foreach (var cl in HostToolsFiles.Others())
                    if (!UsefulVersionHandshake.playerVersions.ContainsKey(cl.Id))
                        Add(list, Kind.Warn, HFLocalization.Tr("hostfix.tools.check_no_mod", HostToolsFiles.NameOf(cl.Character)));
                string mismatch = UsefulVersionHandshake.BuildMismatchMessage();
                if (!string.IsNullOrWhiteSpace(mismatch))
                    foreach (var line in mismatch.Split('\n')) Add(list, Kind.Warn, line);
            }
            catch { }

            // 4. connections
            try
            {
                foreach (var r in ConnectionLight.Rows())
                    if (r.Level == ConnectionLight.Level.Red) Add(list, Kind.Warn, HFLocalization.Tr("hostfix.tools.check_connection", r.Name));
            }
            catch { }

            // 5. hints of the other tools
            try
            {
                int n = HostToolsFiles.Others().Count + 1;
                var fit = SettingsPresets.FitFor(n);
                if (fit != null)
                {
                    int d = SettingsPresets.Differences(fit);
                    if (d > 0) Add(list, Kind.Info, HFLocalization.Tr("hostfix.tools.check_preset", fit.Name, n, d));
                }
                if (EveningPlan.Offer != null) Add(list, Kind.Info, HFLocalization.Tr("hostfix.tools.check_plan", EveningPlan.Offer));
            }
            catch { }
        }
        catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[StartCheck] {e.Message}"); }
        return list.OrderBy(i => (int)i.Kind).ToList();
    }
}

// ====================================================================================================== settings log
internal static class SettingsLog
{
    private const int Keep = 10;
    private static string Folder => Path.Combine(HostToolsFiles.Dir, "rounds");
    private static int roundNo;

    /// <summary>The snapshots of the last rounds, the newest first.</summary>
    internal static List<SettingsPresets.Preset> Rounds()
    {
        var list = new List<SettingsPresets.Preset>();
        try
        {
            if (!Directory.Exists(Folder)) return list;
            foreach (var f in Directory.GetFiles(Folder, "round-*.preset"))
            {
                var p = SettingsPresets.ReadPath(f);
                if (p != null) list.Add(p);
            }
        }
        catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[SettingsLog] list: {e.Message}"); }
        return list.OrderByDescending(p => p.Saved).ToList();
    }

    private static void Snapshot()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            roundNo++;
            string name = HFLocalization.Tr("hostfix.tools.log_round_name", roundNo, DateTime.Now.ToString("HH:mm"));
            SettingsPresets.SaveSnapshot(Path.Combine(Folder, $"round-{DateTime.Now:yyyyMMdd-HHmmss}.preset"), name);
            SettingsRescue.Remember();
            foreach (var old in new DirectoryInfo(Folder).GetFiles("round-*.preset").OrderByDescending(f => f.Name).Skip(Keep))
                try { old.Delete(); } catch { }
        }
        catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[SettingsLog] snapshot failed: {e.Message}"); }
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start))]
    private static class RoundStartPatch
    {
        public static void Postfix()
        {
            if (HostToolsFiles.HostOnline()) Snapshot();
        }
    }
}

// ====================================================================================================== settings rescue
internal static class SettingsRescue
{
    private static string FilePath => HostToolsFiles.PathOf("last-hosted.preset");

    /// <summary>The settings this player last hosted with, waiting to be loaded; null when nothing is offered.</summary>
    internal static SettingsPresets.Preset Offer { get; private set; }
    internal static int OfferDiffers { get; private set; }

    private static bool wasHost;
    private static float hostSince;

    /// <summary>Stores the current settings as "what I last hosted with".</summary>
    internal static void Remember()
    {
        try { SettingsPresets.SaveSnapshot(FilePath, "last hosted"); }
        catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[SettingsRescue] remember failed: {e.Message}"); }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.ExitGame))]
    private static class LeavePatch
    {
        public static void Prefix()
        {
            // leaving the own lobby: what is set now is what the host wants to find again
            try { if (HostToolsFiles.HostInLobby() && wasHost && Offer == null) Remember(); } catch { }
            wasHost = false;
            Offer = null;
        }
    }

    /// <summary>From the panel's driver, a few times a second.</summary>
    internal static void Tick()
    {
        bool host = HostToolsFiles.HostInLobby();
        if (!host) { if (!HostToolsFiles.HostOnline()) { wasHost = false; Offer = null; } return; }
        if (!wasHost) { wasHost = true; hostSince = Time.realtimeSinceStartup; return; }
        if (hostSince < 0f || Time.realtimeSinceStartup - hostSince < 6f) return;   // let the options of the lobby settle first
        hostSince = -1f;                                                             // one check per time as host
        try
        {
            var p = SettingsPresets.ReadPath(FilePath);
            if (p == null) return;
            int d = SettingsPresets.Differences(p);
            if (d <= 0) return;
            Offer = p; OfferDiffers = d;
            HostToolsFiles.Chat(HFLocalization.Tr("hostfix.tools.rescue_chat", d, p.Saved.ToString("dd.MM. HH:mm")));
            HostFixPlugin.Logger.LogInfo($"[SettingsRescue] host again, {d} option(s) differ from the last hosted settings ({p.Saved:yyyy-MM-dd HH:mm}): offered.");
        }
        catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[SettingsRescue] check failed: {e.Message}"); }
    }

    internal static void LoadOffer()
    {
        var p = Offer;
        Offer = null;
        if (p == null) return;
        var r = SettingsPresets.Load(p);
        HostToolsFiles.Chat(r.Error != null ? HFLocalization.Tr("hostfix.presets.load_failed", r.Error)
                                            : HFLocalization.Tr("hostfix.tools.rescue_loaded", r.Changed, r.Packets));
    }

    internal static void Dismiss() => Offer = null;
}

// ====================================================================================================== evening plan
internal static class EveningPlan
{
    internal sealed class Step
    {
        public string Preset = "";
        public int Rounds = 3;
    }

    private static string FilePath => HostToolsFiles.PathOf("evening-plan.txt");
    private static List<Step> steps;

    internal static bool Running { get; private set; }
    internal static int Index { get; private set; }
    internal static int RoundsDone { get; private set; }
    /// <summary>Name of the preset the plan wants loaded now; null when nothing is pending.</summary>
    internal static string Offer { get; private set; }
    private static string pendingChat;

    internal static List<Step> Steps
    {
        get
        {
            if (steps != null) return steps;
            steps = new List<Step>();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        var f = line.Split('\t');
                        if (f.Length < 2 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) continue;
                        steps.Add(new Step { Rounds = Mathf.Clamp(n, 1, 20), Preset = f[1].Trim() });
                    }
            }
            catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[EveningPlan] read: {e.Message}"); }
            return steps;
        }
    }

    internal static void Save()
    {
        try
        {
            File.WriteAllLines(FilePath, Steps.Select(s => s.Rounds.ToString(CultureInfo.InvariantCulture) + "\t" + HostToolsFiles.Field(s.Preset)),
                               new UTF8Encoding(false));
        }
        catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[EveningPlan] write: {e.Message}"); }
    }

    private static void OfferStep()
    {
        Offer = null;
        if (!Running || Index >= Steps.Count) return;
        var s = Steps[Index];
        var p = SettingsPresets.List().FirstOrDefault(x => string.Equals(x.Name, s.Preset, StringComparison.OrdinalIgnoreCase));
        if (p == null) { pendingChat = HFLocalization.Tr("hostfix.tools.plan_missing", Index + 1, s.Preset); return; }
        if (SettingsPresets.Differences(p) > 0) Offer = p.Name;
        pendingChat = HFLocalization.Tr(Offer != null ? "hostfix.tools.plan_next_chat" : "hostfix.tools.plan_next_same_chat", Index + 1, s.Preset, s.Rounds);
    }

    internal static void Start()
    {
        if (Steps.Count == 0) return;
        Running = true; Index = 0; RoundsDone = 0;
        OfferStep();
    }

    internal static void Stop() { Running = false; Offer = null; pendingChat = null; }

    internal static void LoadOffer()
    {
        string name = Offer;
        Offer = null;
        if (name == null) return;
        var p = SettingsPresets.List().FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (p == null) return;
        var r = SettingsPresets.Load(p);
        HostToolsFiles.Chat(r.Error != null ? HFLocalization.Tr("hostfix.presets.load_failed", r.Error)
                            : r.Changed == 0 ? HFLocalization.Tr("hostfix.presets.loaded_same", p.Name)
                            : HFLocalization.Tr("hostfix.presets.loaded", p.Name, r.Changed, r.Packets));
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    private static class RoundEndPatch
    {
        public static void Postfix()
        {
            try
            {
                if (!Running || !HostToolsFiles.HostOnline()) return;
                RoundsDone++;
                if (Index < Steps.Count && RoundsDone < Steps[Index].Rounds) return;
                Index++; RoundsDone = 0;
                if (Index >= Steps.Count) { Running = false; Offer = null; pendingChat = HFLocalization.Tr("hostfix.tools.plan_done_chat"); return; }
                OfferStep();
            }
            catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[EveningPlan] round end: {e.Message}"); }
        }
    }

    /// <summary>From the panel's driver: the chat line waits until the host is back in the lobby.</summary>
    internal static void Tick()
    {
        if (pendingChat == null || !HostToolsFiles.HostInLobby()) return;
        HostToolsFiles.Chat(pendingChat);
        pendingChat = null;
    }
}

// ====================================================================================================== notes
internal static class HostNotes
{
    private static ConfigEntry<bool> showOnJoin;
    private static Dictionary<string, (string Name, string Note)> notes;
    private static readonly HashSet<string> announced = new();
    private static string FilePath => HostToolsFiles.PathOf("notes.txt");

    internal static void Bind(ConfigFile cfg)
    {
        showOnJoin = cfg.Bind("HostTools", "Show Notes On Join", true,
            "Show your private note on a player in your own chat when that player joins your lobby (host only, nobody else sees it).");
    }

    private static Dictionary<string, (string Name, string Note)> All
    {
        get
        {
            if (notes != null) return notes;
            notes = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        var f = line.Split('\t');
                        if (f.Length >= 3 && f[0].Length > 0 && f[2].Trim().Length > 0) notes[f[0]] = (f[1], f[2].Trim());
                    }
            }
            catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[HostNotes] read: {e.Message}"); }
            return notes;
        }
    }

    internal static string Get(string key) => key != null && All.TryGetValue(key, out var n) ? n.Note : "";

    internal static void Set(string key, string name, string note)
    {
        if (key == null) return;
        note = HostToolsFiles.Field(note);
        if (note.Length == 0) All.Remove(key);
        else All[key] = (HostToolsFiles.Field(name), note);
        announced.Add(key);
        try
        {
            File.WriteAllLines(FilePath, All.Select(kv => kv.Key + "\t" + kv.Value.Name + "\t" + kv.Value.Note), new UTF8Encoding(false));
        }
        catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[HostNotes] write: {e.Message}"); }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    private static class JoinedPatch
    {
        public static void Postfix() => announced.Clear();
    }

    internal static void Tick()
    {
        if (showOnJoin == null || !showOnJoin.Value || !HostToolsFiles.HostInLobby() || All.Count == 0) return;
        foreach (var cl in HostToolsFiles.Others())
        {
            string key = HostToolsFiles.KeyOf(cl.Character);
            if (key == null || !announced.Add(key)) continue;
            string note = Get(key);
            if (note.Length > 0) HostToolsFiles.Chat(HFLocalization.Tr("hostfix.tools.note_chat", HostToolsFiles.NameOf(cl.Character), note));
        }
    }
}

// ====================================================================================================== lobby guard
internal static class LobbyGuard
{
    private static ConfigEntry<bool> warnMissingMod;
    private static Dictionary<string, string> blocked;                  // key -> name when blocked
    private static readonly Dictionary<int, float> seenSince = new();    // client id -> first seen in this lobby
    private static readonly HashSet<int> warned = new(), kicked = new();
    private const float ModGrace = 15f;
    private static string FilePath => HostToolsFiles.PathOf("blocklist.txt");

    internal static void Bind(ConfigFile cfg)
    {
        warnMissingMod = cfg.Bind("HostTools", "Warn About Players Without Forgotten Fixes", true,
            "Tell you in your own chat when a player has been in your lobby for 15 seconds without TOR - Forgotten Fixes, instead of only at the start.");
    }

    internal static Dictionary<string, string> Blocked
    {
        get
        {
            if (blocked != null) return blocked;
            blocked = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        var f = line.Split('\t');
                        if (f.Length >= 1 && f[0].Trim().Length > 0) blocked[f[0].Trim()] = f.Length > 1 ? f[1] : "";
                    }
            }
            catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[LobbyGuard] read: {e.Message}"); }
            return blocked;
        }
    }

    private static void Save()
    {
        try { File.WriteAllLines(FilePath, Blocked.Select(kv => kv.Key + "\t" + kv.Value), new UTF8Encoding(false)); }
        catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[LobbyGuard] write: {e.Message}"); }
    }

    internal static void Block(InnerNet.ClientData cl)
    {
        string key = HostToolsFiles.KeyOf(cl.Character);
        if (key == null) return;
        Blocked[key] = HostToolsFiles.Field(HostToolsFiles.NameOf(cl.Character));
        Save();
        Kick(cl);
    }

    internal static void Unblock(string key)
    {
        if (Blocked.Remove(key)) Save();
    }

    private static void Kick(InnerNet.ClientData cl)
    {
        try
        {
            if (!kicked.Add(cl.Id)) return;
            string name = HostToolsFiles.NameOf(cl.Character);
            AmongUsClient.Instance.KickPlayer(cl.Id, false);
            HostToolsFiles.Chat(HFLocalization.Tr("hostfix.tools.guard_kicked_chat", name));
            HostFixPlugin.Logger.LogInfo($"[LobbyGuard] {name} is on the host's block list and was removed from the lobby.");
        }
        catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[LobbyGuard] kick failed: {e.Message}"); }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    private static class JoinedPatch
    {
        public static void Postfix() { seenSince.Clear(); warned.Clear(); kicked.Clear(); }
    }

    internal static void Tick()
    {
        if (!HostToolsFiles.HostInLobby()) return;
        float now = Time.realtimeSinceStartup;
        foreach (var cl in HostToolsFiles.Others())
        {
            if (!seenSince.ContainsKey(cl.Id)) seenSince[cl.Id] = now;
            if (Blocked.Count > 0)
            {
                string key = HostToolsFiles.KeyOf(cl.Character);
                if (key != null && Blocked.ContainsKey(key)) { Kick(cl); continue; }
            }
            if (warnMissingMod != null && warnMissingMod.Value && now - seenSince[cl.Id] >= ModGrace && !warned.Contains(cl.Id))
            {
                warned.Add(cl.Id);
                bool has = true;
                try { has = UsefulVersionHandshake.playerVersions.ContainsKey(cl.Id); } catch { }
                if (!has) HostToolsFiles.Chat(HFLocalization.Tr("hostfix.tools.guard_no_mod_chat", HostToolsFiles.NameOf(cl.Character)));
            }
        }
    }
}

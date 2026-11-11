// TOR Host Tools - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.

/*
 * HostToolsUI - the host's panel for the new tools (lobby menu entry "Host Tools", host only), four tabs:
 *   Start check    everything that stands in the way of a start, the blocking things first
 *   Settings log   what changed since the last round started, and the last rounds with "put back"
 *   Evening plan   the steps (preset, rounds), start and stop, where the plan stands
 *   Players        every player of the lobby with the host's note (NOTE types into the field at the top, Enter saves)
 *                  and BLOCK; the block list with UNBLOCK behind the button at the bottom
 * The component also drives the tools that have no screen of their own (notes on join, lobby guard, settings rescue,
 * the chat lines of the evening plan) and adds the lobby menu entries that load an offered preset.
 * VanillaUI look like the other lobby panels (the building blocks come from TOR - Forgotten Fixes).
 */

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UsefulTORStuff;

namespace HostFixPlugin;

public class HostToolsUI : MonoBehaviour
{
    public static HostToolsUI Instance { get; private set; }

    public HostToolsUI(IntPtr ptr) : base(ptr) { }

    private enum Tab { Check, Log, Plan, Players }

    private GameObject root;
    private Tab tab = Tab.Check;
    private float nextRefresh, nextDrive;
    private string confirmKey;
    private float confirmUntil;
    private string status = "";
    private bool showBlocked;
    private bool frozeMove;

    // note editing
    private string editKey, editName, buffer = "";
    private TMPro.TextMeshProUGUI fieldText;

    private const float Width = 1300f, Height = 900f, Inset = 34f, RowH = 46f, RowStep = 52f;
    private const int MaxNote = 90;

    public void Awake()
    {
        if (Instance) Destroy(Instance);
        Instance = this;
        LobbyMenu.Add("hosttools.panel", 5, ShouldShow, MenuLabel, () => Instance?.Toggle(), VanillaUI.Amber);
        LobbyMenu.Add("hosttools.rescue", 6, () => SettingsRescue.Offer != null && ShouldShow(),
                      () => HFLocalization.Tr("hostfix.tools.rescue_button", SettingsRescue.OfferDiffers), SettingsRescue.LoadOffer, VanillaUI.Green);
        LobbyMenu.Add("hosttools.plan", 7, () => EveningPlan.Offer != null && ShouldShow(),
                      () => HFLocalization.Tr("hostfix.tools.plan_button", EveningPlan.Offer ?? ""), EveningPlan.LoadOffer, VanillaUI.Green);
    }

    private static bool ShouldShow() => HostToolsFiles.HostInLobby();

    private static string MenuLabel()
    {
        string s = HFLocalization.Tr("hostfix.tools.button");
        try
        {
            int n = StartCheck.Cached().Count(i => i.Kind == StartCheck.Kind.Block);
            if (n > 0) s += " " + HFLocalization.Tr("hostfix.tools.button_blocked", n);
        }
        catch { }
        return s;
    }

    public void Update()
    {
        float now = Time.realtimeSinceStartup;
        if (now >= nextDrive)
        {
            nextDrive = now + 0.5f;
            try { SettingsRescue.Tick(); EveningPlan.Tick(); HostNotes.Tick(); LobbyGuard.Tick(); }
            catch (Exception e) { HostFixPlugin.Logger.LogWarning($"[HostTools] driver: {e.Message}"); }
        }
        if (root == null) { Unfreeze(); return; }
        if (!ShouldShow()) { Close(); return; }
        Freeze();
        if (confirmKey != null && now > confirmUntil) { confirmKey = null; Rebuild(); return; }
        if (editKey != null) { if (HandleKeys()) return; }
        else if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        if (now < nextRefresh) return;
        if (editKey != null) { nextRefresh = now + 0.5f; UpdateField(); return; }
        if (tab == Tab.Check || tab == Tab.Players) Rebuild();
        else nextRefresh = now + 2f;
    }

    [HideFromIl2Cpp]
    public void Toggle()
    {
        if (root != null) Close();
        else { confirmKey = null; editKey = null; status = ""; Build(); }
    }

    [HideFromIl2Cpp]
    public void Close()
    {
        Teardown();
        Unfreeze();
        editKey = null; confirmKey = null;
    }

    private void Teardown()
    {
        if (root != null) { Destroy(root); root = null; LobbyPanelGuard.Closed(); }
        fieldText = null;
    }

    private void Rebuild()
    {
        if (root == null) return;
        Teardown();
        Build();
    }

    private void Freeze()
    {
        try
        {
            var lp = PlayerControl.LocalPlayer;
            if (lp != null && lp.moveable) { lp.moveable = false; frozeMove = true; }
        }
        catch { }
    }

    private void Unfreeze()
    {
        if (!frozeMove) return;
        frozeMove = false;
        try { if (PlayerControl.LocalPlayer != null) PlayerControl.LocalPlayer.moveable = true; } catch { }
    }

    [HideFromIl2Cpp]
    private bool Confirmed(string key)
    {
        if (confirmKey == key) { confirmKey = null; return true; }
        confirmKey = key; confirmUntil = Time.realtimeSinceStartup + 3f;
        Rebuild();
        return false;
    }

    // ---------------------------------------------------------------- note field
    [HideFromIl2Cpp]
    private bool HandleKeys()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { editKey = null; buffer = ""; Rebuild(); return true; }
        string typed = Input.inputString;
        if (string.IsNullOrEmpty(typed)) return false;
        foreach (char c in typed)
        {
            if (c == '\b') { if (buffer.Length > 0) buffer = buffer.Substring(0, buffer.Length - 1); }
            else if (c == '\n' || c == '\r')
            {
                HostNotes.Set(editKey, editName, buffer);
                status = HFLocalization.Tr(buffer.Trim().Length > 0 ? "hostfix.tools.note_saved" : "hostfix.tools.note_removed", editName);
                editKey = null; buffer = "";
                Rebuild();
                return true;
            }
            else if (!char.IsControl(c) && buffer.Length < MaxNote) buffer += c;
        }
        UpdateField();
        return true;
    }

    [HideFromIl2Cpp]
    private void UpdateField()
    {
        if (fieldText == null) return;
        bool caret = Mathf.Repeat(Time.realtimeSinceStartup, 1f) < 0.55f;
        string shown = buffer.Length == 0 ? $"<color=#8C99A8>{HFLocalization.Tr("hostfix.tools.note_placeholder", editName)}</color>"
                                          : buffer.Replace("<", "<noparse><</noparse>");
        VanillaUI.SetText(fieldText, shown + (caret ? "<color=#5FE0D2>|</color>" : " "));
    }

    // ---------------------------------------------------------------- panel
    private static readonly Vector2 TL = new Vector2(0, 1);
    private static readonly Vector2 RightMid = new Vector2(1, 0.5f);

    [HideFromIl2Cpp]
    private void Build()
    {
        try
        {
            nextRefresh = Time.realtimeSinceStartup + 1.5f;
            root = VanillaUI.Canvas("HostToolsUI", 9500, true);
            LobbyPanelGuard.Track(root);
            VanillaUI.Backdrop(root, Close);
            var panel = VanillaUI.CenterPanel(root, new Vector2(Width, Height));
            VanillaUI.Title(panel, HFLocalization.Tr("hostfix.tools.title"), 16f, 34f, VanillaUI.Amber);
            VanillaUI.CloseButton(panel, "", Close);

            // tabs
            float x = Inset + 5f;
            foreach (var (t, key) in new[] { (Tab.Check, "tab_check"), (Tab.Log, "tab_log"), (Tab.Plan, "tab_plan"), (Tab.Players, "tab_players") })
            {
                var mine = t;
                VanillaUI.Button(panel, HFLocalization.Tr("hostfix.tools." + key), new Vector2(x, -104f), new Vector2(250f, 50f),
                                 tab == t ? VanillaUI.TealBright : VanillaUI.Grey,
                                 () => { tab = mine; confirmKey = null; editKey = null; showBlocked = false; status = ""; Rebuild(); },
                                 anchorMin: TL, anchorMax: TL, pivot: TL);
                x += 260f;
            }

            float y = -176f;
            switch (tab)
            {
                case Tab.Check: BuildCheck(panel, y); break;
                case Tab.Log: BuildLog(panel, y); break;
                case Tab.Plan: BuildPlan(panel, y); break;
                case Tab.Players: BuildPlayers(panel, y); break;
            }

            VanillaUI.Label(panel, status, 17f, VanillaUI.Good, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0),
                            new Vector2(Inset + 10f, 26f), new Vector2(-2 * Inset - 20f, 50f), TMPro.TextAlignmentOptions.Left);
        }
        catch (Exception ex)
        {
            HostFixPlugin.Logger.LogError($"[HostTools] panel failed: {ex}");
            Teardown();
        }
    }

    [HideFromIl2Cpp]
    private static TMPro.TextMeshProUGUI RowText(GameObject row, string text, float x, float width, float size, Color color, bool bold = false)
    {
        var t = VanillaUI.Label(row, text, size, color, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f),
                                new Vector2(x, 0f), new Vector2(width, 0f), TMPro.TextAlignmentOptions.Left,
                                bold ? TMPro.FontStyles.Bold : TMPro.FontStyles.Normal);
        t.enableWordWrapping = false;
        t.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        return t;
    }

    [HideFromIl2Cpp]
    private void Hint(GameObject panel, string text, float y) =>
        VanillaUI.Text(panel, text, 17f, VanillaUI.Muted, new Vector2(Inset + 10f, y), new Vector2(Width - 2 * Inset - 20f, 60f), TMPro.FontStyles.Italic);

    // ---------------------------------------------------------------- start check
    [HideFromIl2Cpp]
    private void BuildCheck(GameObject panel, float y)
    {
        var items = StartCheck.Collect();
        if (items.Count == 0)
        {
            var row = VanillaUI.Row(panel, y, RowH, Inset);
            VanillaUI.Box(row, new Vector2(14f, -(RowH - 22f) / 2f), new Vector2(22f, 22f), VanillaUI.Green, 11);
            RowText(row, HFLocalization.Tr("hostfix.tools.check_ready"), 50f, 1100f, 19f, Color.white, true);
            return;
        }
        foreach (var it in items.Take(12))
        {
            var row = VanillaUI.Row(panel, y, RowH, Inset);
            Color c = it.Kind == StartCheck.Kind.Block ? VanillaUI.Red : it.Kind == StartCheck.Kind.Warn ? VanillaUI.Yellow : VanillaUI.Blue;
            VanillaUI.Box(row, new Vector2(14f, -(RowH - 22f) / 2f), new Vector2(22f, 22f), c, 11);
            RowText(row, it.Text, 50f, 1140f, 17f, Color.white);
            y -= RowStep;
        }
        if (items.Count > 12) Hint(panel, HFLocalization.Tr("hostfix.tools.more", items.Count - 12), y - 4f);
        Hint(panel, HFLocalization.Tr("hostfix.tools.check_legend"), -Height + 150f);
    }

    // ---------------------------------------------------------------- settings log
    [HideFromIl2Cpp]
    private void BuildLog(GameObject panel, float y)
    {
        var rounds = SettingsLog.Rounds();
        if (rounds.Count == 0) { Hint(panel, HFLocalization.Tr("hostfix.tools.log_none"), y - 6f); return; }

        var last = rounds[0];
        var lines = SettingsPresets.DiffLines(last, 8);
        VanillaUI.Text(panel, HFLocalization.Tr(lines.Count == 0 ? "hostfix.tools.log_same" : "hostfix.tools.log_changed", last.Name),
                       19f, lines.Count == 0 ? VanillaUI.Good : Color.white, new Vector2(Inset + 10f, y), new Vector2(Width - 2 * Inset - 20f, 30f),
                       TMPro.FontStyles.Bold);
        y -= 34f;
        foreach (var l in lines)
        {
            var t = VanillaUI.Text(panel, l, 16f, VanillaUI.Muted, new Vector2(Inset + 30f, y), new Vector2(Width - 2 * Inset - 60f, 26f));
            t.enableWordWrapping = false;
            t.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            y -= 26f;
        }
        y -= 18f;
        foreach (var p in rounds.Take(6))
        {
            var row = VanillaUI.Row(panel, y, RowH, Inset);
            int d = SettingsPresets.Differences(p);
            RowText(row, p.Name, 18f, 420f, 19f, Color.white, true);
            RowText(row, d == 0 ? HFLocalization.Tr("hostfix.presets.matches") : HFLocalization.Tr("hostfix.presets.differ", d),
                    450f, 420f, 15f, d == 0 ? VanillaUI.Good : VanillaUI.Muted);
            string key = "restore:" + p.Path;
            var mine = p;
            VanillaUI.Button(row, HFLocalization.Tr(confirmKey == key ? "hostfix.presets.sure" : "hostfix.tools.log_restore"), new Vector2(-10f, 0),
                             new Vector2(210f, RowH - 12f), d == 0 ? VanillaUI.Grey : confirmKey == key ? VanillaUI.Red : VanillaUI.Green,
                             () =>
                             {
                                 if (!Confirmed(key)) return;
                                 var r = SettingsPresets.Load(mine);
                                 status = r.Error != null ? HFLocalization.Tr("hostfix.presets.load_failed", r.Error)
                                                          : HFLocalization.Tr("hostfix.tools.log_restored", mine.Name, r.Changed, r.Packets);
                                 Rebuild();
                             }, anchorMin: RightMid, anchorMax: RightMid, pivot: RightMid);
            y -= RowStep;
        }
    }

    // ---------------------------------------------------------------- evening plan
    [HideFromIl2Cpp]
    private void BuildPlan(GameObject panel, float y)
    {
        var steps = EveningPlan.Steps;
        var names = SettingsPresets.List().Select(p => p.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        string where = !EveningPlan.Running ? HFLocalization.Tr("hostfix.tools.plan_idle")
            : HFLocalization.Tr("hostfix.tools.plan_where", EveningPlan.Index + 1, steps.Count, EveningPlan.RoundsDone + 1,
                                EveningPlan.Index < steps.Count ? steps[EveningPlan.Index].Rounds : 0);
        VanillaUI.Text(panel, where, 19f, EveningPlan.Running ? VanillaUI.Good : VanillaUI.Muted, new Vector2(Inset + 10f, y),
                       new Vector2(Width - 2 * Inset - 20f, 30f), TMPro.FontStyles.Bold);
        y -= 40f;
        if (names.Count == 0) { Hint(panel, HFLocalization.Tr("hostfix.tools.plan_no_presets"), y - 4f); return; }

        for (int i = 0; i < steps.Count && i < 9; i++)
        {
            int idx = i;
            var s = steps[i];
            bool now = EveningPlan.Running && EveningPlan.Index == i;
            var row = VanillaUI.Row(panel, y, RowH, Inset, now ? VanillaUI.FieldLight : (Color?)null);
            RowText(row, HFLocalization.Tr("hostfix.tools.plan_step", i + 1), 18f, 130f, 19f, Color.white, true);
            bool known = names.Any(n => string.Equals(n, s.Preset, StringComparison.OrdinalIgnoreCase));
            VanillaUI.Button(row, s.Preset.Length > 0 ? s.Preset : "?", new Vector2(160f, 0), new Vector2(520f, RowH - 12f), known ? VanillaUI.Teal : VanillaUI.Red,
                             () =>
                             {
                                 int at = names.FindIndex(n => string.Equals(n, s.Preset, StringComparison.OrdinalIgnoreCase));
                                 s.Preset = names[(at + 1) % names.Count];
                                 EveningPlan.Save(); Rebuild();
                             }, anchorMin: new Vector2(0, 0.5f), anchorMax: new Vector2(0, 0.5f), pivot: new Vector2(0, 0.5f));
            VanillaUI.Button(row, "X", new Vector2(-10f, 0), new Vector2(60f, RowH - 12f), VanillaUI.Red,
                             () => { steps.RemoveAt(idx); EveningPlan.Stop(); EveningPlan.Save(); Rebuild(); },
                             anchorMin: RightMid, anchorMax: RightMid, pivot: RightMid);
            VanillaUI.Button(row, "+", new Vector2(-80f, 0), new Vector2(60f, RowH - 12f), VanillaUI.Blue,
                             () => { s.Rounds = Mathf.Min(20, s.Rounds + 1); EveningPlan.Save(); Rebuild(); },
                             anchorMin: RightMid, anchorMax: RightMid, pivot: RightMid);
            VanillaUI.Label(row, HFLocalization.Tr("hostfix.tools.plan_rounds", s.Rounds), 18f, Color.white, RightMid, RightMid, RightMid,
                            new Vector2(-150f, 0f), new Vector2(170f, RowH), TMPro.TextAlignmentOptions.Center, TMPro.FontStyles.Bold);
            VanillaUI.Button(row, "-", new Vector2(-330f, 0), new Vector2(60f, RowH - 12f), VanillaUI.Blue,
                             () => { s.Rounds = Mathf.Max(1, s.Rounds - 1); EveningPlan.Save(); Rebuild(); },
                             anchorMin: RightMid, anchorMax: RightMid, pivot: RightMid);
            y -= RowStep;
        }

        y -= 10f;
        if (steps.Count < 9)
            VanillaUI.Button(panel, HFLocalization.Tr("hostfix.tools.plan_add"), new Vector2(Inset + 5f, y), new Vector2(300f, 52f), VanillaUI.Blue,
                             () => { steps.Add(new EveningPlan.Step { Preset = names[0], Rounds = 3 }); EveningPlan.Save(); Rebuild(); },
                             anchorMin: TL, anchorMax: TL, pivot: TL);
        if (steps.Count > 0)
            VanillaUI.Button(panel, HFLocalization.Tr(EveningPlan.Running ? "hostfix.tools.plan_stop" : "hostfix.tools.plan_start"),
                             new Vector2(Inset + 320f, y), new Vector2(300f, 52f), EveningPlan.Running ? VanillaUI.Red : VanillaUI.Green,
                             () =>
                             {
                                 if (EveningPlan.Running) EveningPlan.Stop(); else EveningPlan.Start();
                                 Rebuild();
                             }, anchorMin: TL, anchorMax: TL, pivot: TL);
        Hint(panel, HFLocalization.Tr("hostfix.tools.plan_legend"), -Height + 150f);
    }

    // ---------------------------------------------------------------- players
    [HideFromIl2Cpp]
    private void BuildPlayers(GameObject panel, float y)
    {
        if (editKey != null)
        {
            var field = VanillaUI.Box(panel, new Vector2(Inset + 5f, y), new Vector2(Width - 2 * Inset - 10f, 54f), VanillaUI.FieldLight);
            fieldText = VanillaUI.Label(field, "", 20f, Color.white, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                                        new Vector2(8f, 0f), new Vector2(-40f, 0f), TMPro.TextAlignmentOptions.Left, TMPro.FontStyles.Bold);
            fieldText.enableWordWrapping = false;
            fieldText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            UpdateField();
            y -= 70f;
        }

        if (showBlocked)
        {
            var blocked = LobbyGuard.Blocked.ToList();
            if (blocked.Count == 0) Hint(panel, HFLocalization.Tr("hostfix.tools.guard_none"), y - 4f);
            foreach (var kv in blocked.Take(10))
            {
                var row = VanillaUI.Row(panel, y, RowH, Inset);
                RowText(row, kv.Value.Length > 0 ? kv.Value : kv.Key, 18f, 700f, 19f, Color.white, true);
                string key = kv.Key;
                VanillaUI.Button(row, HFLocalization.Tr("hostfix.tools.guard_unblock"), new Vector2(-10f, 0), new Vector2(220f, RowH - 12f), VanillaUI.Blue,
                                 () => { LobbyGuard.Unblock(key); Rebuild(); }, anchorMin: RightMid, anchorMax: RightMid, pivot: RightMid);
                y -= RowStep;
            }
        }
        else
        {
            var others = HostToolsFiles.Others();
            if (others.Count == 0) Hint(panel, HFLocalization.Tr("hostfix.connections.alone"), y - 4f);
            foreach (var cl in others.Take(editKey != null ? 10 : 11))
            {
                var mine = cl;
                string key = HostToolsFiles.KeyOf(cl.Character), name = HostToolsFiles.NameOf(cl.Character);
                string note = HostNotes.Get(key);
                var row = VanillaUI.Row(panel, y, RowH, Inset, editKey != null && editKey == key ? VanillaUI.FieldLight : (Color?)null);
                RowText(row, name, 18f, 270f, 19f, Color.white, true);
                RowText(row, note.Length > 0 ? note : HFLocalization.Tr("hostfix.tools.note_empty"), 300f, 520f, 15f, note.Length > 0 ? Color.white : VanillaUI.Muted);
                string blockKey = "block:" + cl.Id;
                VanillaUI.Button(row, HFLocalization.Tr(confirmKey == blockKey ? "hostfix.presets.sure" : "hostfix.tools.guard_block"), new Vector2(-10f, 0),
                                 new Vector2(170f, RowH - 12f), VanillaUI.Red,
                                 () =>
                                 {
                                     if (!Confirmed(blockKey)) return;
                                     LobbyGuard.Block(mine);
                                     status = HFLocalization.Tr("hostfix.tools.guard_blocked", name);
                                     Rebuild();
                                 }, anchorMin: RightMid, anchorMax: RightMid, pivot: RightMid);
                VanillaUI.Button(row, HFLocalization.Tr("hostfix.tools.note_button"), new Vector2(-190f, 0), new Vector2(170f, RowH - 12f), VanillaUI.Blue,
                                 () => { editKey = key; editName = name; buffer = HostNotes.Get(key); confirmKey = null; Rebuild(); },
                                 anchorMin: RightMid, anchorMax: RightMid, pivot: RightMid);
                y -= RowStep;
            }
        }

        var bl = new Vector2(0, 0);
        VanillaUI.Button(panel, showBlocked ? HFLocalization.Tr("hostfix.tools.guard_back") : HFLocalization.Tr("hostfix.tools.guard_list", LobbyGuard.Blocked.Count),
                         new Vector2(Inset + 5f, 84f), new Vector2(360f, 50f), VanillaUI.Grey,
                         () => { showBlocked = !showBlocked; editKey = null; confirmKey = null; Rebuild(); }, anchorMin: bl, anchorMax: bl, pivot: bl);
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    private static class LobbyResetPatch
    {
        public static void Postfix() => Instance?.Close();
    }

    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
    private static class BeginGamePatch
    {
        public static void Postfix() => Instance?.Close();
    }
}

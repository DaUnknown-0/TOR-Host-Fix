// TOR Host Tools - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.

/*
 * SettingsPresetsUI - the host's panel for SettingsPresets, entry "Presets" in the lobby menu (host only).
 *
 * Top: a name field (the keyboard types into it while the panel is open; Enter saves, Ctrl+V pastes) and SAVE.
 * Below: every saved preset, newest first, eight per page: name, when it was saved, how many options it holds and
 * how many of them differ from the settings right now ("matches" when none does), with LOAD, UPDATE (put the current
 * settings into it), RENAME (the name field takes its name, Enter renames) and DELETE. UPDATE and DELETE ask once
 * more on the same button. A line at the bottom tells what the last action did.
 * PLAYERS (the small button showing the range) sets the lobby sizes a preset is meant for: with "10-15" typed into the
 * name field it takes that range, otherwise each press steps to the next fixed range and at the end back to none.
 * While the panel is closed this component also watches the lobby size and suggests the fitting preset to the host.
 *
 * The player does not walk while the panel is open (the keyboard belongs to the name field). VanillaUI look like
 * every other lobby panel; the game's collider buttons underneath are switched off while it is open (as in the
 * music panel).
 */

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;


namespace HostFixPlugin {

    public class SettingsPresetsUI : MonoBehaviour {
        public static SettingsPresetsUI Instance { get; private set; }

        public SettingsPresetsUI(IntPtr ptr) : base(ptr) { }

        internal static bool DiagActive;

        private GameObject root;
        private TMPro.TextMeshProUGUI fieldText;
        private string buffer = "";
        private SettingsPresets.Preset renaming;
        private List<SettingsPresets.Preset> presets = new List<SettingsPresets.Preset>();
        private readonly Dictionary<string, int> diffs = new Dictionary<string, int>();
        private int page;
        private string confirmKey;
        private float confirmUntil;
        private string status = "";
        private Color statusColor = VanillaUI.Muted;
        private bool frozeMove;
        private float nextPoll;

        private const float Width = 1300f, PlayersW = 110f, Inset = 34f, RowH = 66f, RowStep = 74f, ButtonW = 138f, Gap = 10f;
        private const int PageSize = 8;

        public void Awake() {
            if (Instance) Destroy(Instance);
            Instance = this;
            LobbyMenu.Add("uts.presets", 15, ShouldShow, () => HFLocalization.Tr("hostfix.presets.button"), () => Instance?.Toggle(),
                          VanillaUI.Teal);
            LobbyMenu.Add("hostfix.presets.offer", 14, () => offered != null && ShouldShow(),
                          () => offered != null ? HFLocalization.Tr("hostfix.presets.offer_button", offered.Name, offeredFor) : "",
                          LoadOffered, VanillaUI.Green);
        }

        private static bool ShouldShow() {
            try { return LobbyScreen.Exists && AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost; }
            catch { return false; }
        }

        public void Update() {
            if (root == null) { Unfreeze(); if (blocked.Count > 0) UnblockBackground(); SuggestTick(); return; }
            Freeze();
            if (confirmKey != null && Time.realtimeSinceStartup > confirmUntil) { confirmKey = null; Rebuild(); return; }
            if (HandleKeys()) return;
            if (Time.realtimeSinceStartup < nextPoll) return;
            nextPoll = Time.realtimeSinceStartup + 0.5f;
            if (!DiagActive && !ShouldShow()) Close();
            else UpdateField();
        }

        [HideFromIl2Cpp]
        public void Toggle() {
            if (root != null) Close();
            else Open();
        }

        [HideFromIl2Cpp]
        public void Open() {
            Uts.ClosePanel("NewcomerShieldUI");      // Forgotten Fixes' shield panels, when it is installed
            Uts.ClosePanel("EarlyDeathShieldUI");
            buffer = ""; renaming = null; confirmKey = null; page = 0;
            Reload();
            Build();
            BlockBackground();
        }

        [HideFromIl2Cpp]
        public void Close() {
            Teardown();
            UnblockBackground();
            Unfreeze();
            renaming = null; confirmKey = null;
        }

        private void Teardown() {
            if (root != null) { Destroy(root); root = null; LobbyPanelGuard.Closed(); }
            fieldText = null;
        }

        [HideFromIl2Cpp]
        private void Rebuild() {
            if (root == null) return;
            Teardown();
            Build();
        }

        [HideFromIl2Cpp]
        private void Reload() {
            presets = SettingsPresets.List();
            diffs.Clear();
            foreach (var p in presets) {
                try { diffs[p.Path] = SettingsPresets.Differences(p); } catch { }
            }
        }

        // ---- the keyboard belongs to the name field ----
        [HideFromIl2Cpp]
        private bool HandleKeys() {
            if (Input.GetKeyDown(KeyCode.Escape)) {
                if (renaming != null || buffer.Length > 0) { renaming = null; buffer = ""; Rebuild(); }
                else Close();
                return true;
            }
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.V)) {
                foreach (char c in GUIUtility.systemCopyBuffer ?? "") if (!char.IsControl(c)) buffer += c;
                buffer = Clip(buffer);
                UpdateField();
                return true;
            }
            string typed = Input.inputString;
            if (string.IsNullOrEmpty(typed)) return false;
            foreach (char c in typed) {
                if (c == '\b') { if (buffer.Length > 0) buffer = buffer.Substring(0, buffer.Length - 1); }
                else if (c == '\n' || c == '\r') { Commit(); return true; }
                else if (!char.IsControl(c)) buffer = Clip(buffer + c);
            }
            UpdateField();
            return true;
        }

        private static string Clip(string s) => s.Length > SettingsPresets.MaxName ? s.Substring(0, SettingsPresets.MaxName) : s;

        [HideFromIl2Cpp]
        private void UpdateField() {
            if (fieldText == null) return;
            bool caret = Mathf.Repeat(Time.realtimeSinceStartup, 1f) < 0.55f;
            string shown = buffer.Length == 0
                ? $"<color=#8C99A8>{HFLocalization.Tr(renaming != null ? "hostfix.presets.rename_placeholder" : "hostfix.presets.name_placeholder")}</color>"
                : TMPEscape(buffer);
            VanillaUI.SetText(fieldText, shown + (caret ? "<color=#5FE0D2>|</color>" : " "));
        }

        private static string TMPEscape(string s) => s.Replace("<", "<noparse><</noparse>");

        // Enter or the SAVE / RENAME button
        [HideFromIl2Cpp]
        private void Commit() {
            string name = SettingsPresets.CleanName(buffer);
            if (renaming != null) {
                if (name.Length == 0) { SetStatus(HFLocalization.Tr("hostfix.presets.need_name"), VanillaUI.Warn); return; }
                if (SettingsPresets.NameTaken(name, renaming)) { SetStatus(HFLocalization.Tr("hostfix.presets.name_taken", name), VanillaUI.Warn); return; }
                string old = renaming.Name;
                bool ok = SettingsPresets.Rename(renaming, name);
                SetStatus(ok ? HFLocalization.Tr("hostfix.presets.renamed", old, name) : HFLocalization.Tr("hostfix.presets.failed"),
                          ok ? VanillaUI.Good : VanillaUI.Bad);
                renaming = null; buffer = "";
                Reload(); Rebuild();
                return;
            }
            if (name.Length == 0) name = FreeName();
            var existing = presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) {
                // the same name again: the second press overwrites that preset
                string key = "save:" + existing.Path;
                if (confirmKey != key) { Confirm(key); SetStatus(HFLocalization.Tr("hostfix.presets.name_exists", name), VanillaUI.Warn); return; }
                confirmKey = null;
                bool done = SettingsPresets.Overwrite(existing);
                SetStatus(done ? HFLocalization.Tr("hostfix.presets.updated", existing.Name, existing.Options.Count) : HFLocalization.Tr("hostfix.presets.failed"),
                          done ? VanillaUI.Good : VanillaUI.Bad);
            } else {
                var p = SettingsPresets.SaveNew(name);
                SetStatus(p != null ? HFLocalization.Tr("hostfix.presets.saved", p.Name, p.Options.Count) : HFLocalization.Tr("hostfix.presets.failed"),
                          p != null ? VanillaUI.Good : VanillaUI.Bad);
            }
            buffer = "";
            page = 0;
            Reload(); Rebuild();
        }

        [HideFromIl2Cpp]
        private string FreeName() {
            string stem = HFLocalization.Tr("hostfix.presets.default_name");
            for (int i = 1; ; i++) {
                string n = $"{stem} {i}";
                if (!presets.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))) return n;
            }
        }

        private void Confirm(string key) { confirmKey = key; confirmUntil = Time.realtimeSinceStartup + 3f; Rebuild(); }

        private void SetStatus(string s, Color c) { status = s; statusColor = c; Rebuild(); }

        // ---- the preset for this lobby size ----
        // The size has to hold for a while first: during the join wave it changes every few seconds, and a hint per
        // newcomer would be noise. One hint per fitting preset; it comes again only after another preset (or none)
        // was the fitting one in between.
        private const float StableSeconds = 15f;
        private static float nextSuggestPoll, countSince;
        private static int seenCount = -1;
        private static string fitName;                    // the preset that fitted at the last stable size ("" = none)
        private static SettingsPresets.Preset offered;    // waits in the lobby menu until loaded or outgrown
        private static int offeredFor;

        private static int LobbyPlayers() {
            int n = 0;
            foreach (var p in PlayerControl.AllPlayerControls)
                if (p != null && p.Data != null && !p.Data.Disconnected) n++;
            return n;
        }

        [HideFromIl2Cpp]
        private static void SuggestTick() {
            float now = Time.realtimeSinceStartup;
            if (now < nextSuggestPoll) return;
            nextSuggestPoll = now + 2f;
            try {
                if (!ShouldShow()) { seenCount = -1; fitName = null; offered = null; return; }
                int n = LobbyPlayers();
                if (n != seenCount) { seenCount = n; countSince = now; return; }
                if (now - countSince < StableSeconds) return;
                if (offered != null && (n < offered.MinPlayers || n > offered.MaxPlayers)) offered = null;
                var fit = SettingsPresets.FitFor(n);
                string name = fit?.Name ?? "";
                if (name == fitName) return;
                fitName = name;
                if (fit == null) return;
                int d = SettingsPresets.Differences(fit);
                if (d <= 0) { offered = null; return; }      // the settings already are that preset
                offered = fit; offeredFor = n;
                var hud = HudManager.Instance;
                if (hud != null && hud.Chat != null && PlayerControl.LocalPlayer != null)
                    hud.Chat.AddChat(PlayerControl.LocalPlayer, HFLocalization.Tr("hostfix.presets.suggest_chat", n, fit.Name, d));
                HostFixPlugin.Logger?.LogInfo($"[Presets] {n} players: '{fit.Name}' ({fit.MinPlayers}-{fit.MaxPlayers}) suggested, {d} option(s) differ");
            } catch (Exception e) { HostFixPlugin.Logger?.LogWarning($"[Presets] suggestion failed: {e.Message}"); }
        }

        [HideFromIl2Cpp]
        private static void LoadOffered() {
            var p = offered;
            offered = null;
            if (p == null) return;
            try {
                var r = SettingsPresets.Load(p);
                string s = r.Error != null ? HFLocalization.Tr("hostfix.presets.load_failed", r.Error)
                    : r.Changed == 0 ? HFLocalization.Tr("hostfix.presets.loaded_same", p.Name)
                    : HFLocalization.Tr("hostfix.presets.loaded", p.Name, r.Changed, r.Packets);
                var hud = HudManager.Instance;
                if (hud != null && hud.Chat != null && PlayerControl.LocalPlayer != null) hud.Chat.AddChat(PlayerControl.LocalPlayer, s);
            } catch (Exception e) { HostFixPlugin.Logger?.LogWarning($"[Presets] offered load failed: {e.Message}"); }
        }

        [HideFromIl2Cpp]
        private void CyclePlayers(SettingsPresets.Preset p) {
            int min, max;
            bool typed = SettingsPresets.TryParsePlayers(buffer, out min, out max);
            if (!typed) {
                var bands = SettingsPresets.PlayerBands;
                int at = Array.FindIndex(bands, b => b.Min == p.MinPlayers && b.Max == p.MaxPlayers);
                // none -> first range; a range of the list -> the next one, after the last back to none; a typed range -> none
                if (!p.HasPlayers) { min = bands[0].Min; max = bands[0].Max; }
                else if (at >= 0 && at < bands.Length - 1) { min = bands[at + 1].Min; max = bands[at + 1].Max; }
                else { min = 0; max = 0; }
            }
            bool ok = SettingsPresets.SetPlayers(p, min, max);
            if (typed) buffer = "";
            fitName = null;                                  // the fitting preset may be another one now
            Reload();
            SetStatus(!ok ? HFLocalization.Tr("hostfix.presets.failed")
                      : min > 0 ? HFLocalization.Tr("hostfix.presets.players_set", p.Name, min, max)
                      : HFLocalization.Tr("hostfix.presets.players_cleared", p.Name),
                      ok ? VanillaUI.Good : VanillaUI.Bad);
        }

        // ---- actions of a row ----
        [HideFromIl2Cpp]
        private void LoadPreset(SettingsPresets.Preset p) {
            var r = SettingsPresets.Load(p);
            if (r.Error != null) { SetStatus(HFLocalization.Tr("hostfix.presets.load_failed", r.Error), VanillaUI.Bad); return; }
            string s = r.Changed == 0
                ? HFLocalization.Tr("hostfix.presets.loaded_same", p.Name)
                : HFLocalization.Tr("hostfix.presets.loaded", p.Name, r.Changed, r.Packets);
            if (r.Unknown > 0) s += " " + HFLocalization.Tr("hostfix.presets.unknown", r.Unknown);
            Reload();
            SetStatus(s, VanillaUI.Good);
        }

        [HideFromIl2Cpp]
        private void UpdatePreset(SettingsPresets.Preset p) {
            string key = "upd:" + p.Path;
            if (confirmKey != key) { Confirm(key); return; }
            confirmKey = null;
            bool ok = SettingsPresets.Overwrite(p);
            Reload();
            SetStatus(ok ? HFLocalization.Tr("hostfix.presets.updated", p.Name, p.Options.Count) : HFLocalization.Tr("hostfix.presets.failed"),
                      ok ? VanillaUI.Good : VanillaUI.Bad);
        }

        [HideFromIl2Cpp]
        private void DeletePreset(SettingsPresets.Preset p) {
            string key = "del:" + p.Path;
            if (confirmKey != key) { Confirm(key); return; }
            confirmKey = null;
            bool ok = SettingsPresets.Delete(p);
            if (renaming == p) { renaming = null; buffer = ""; }
            Reload();
            SetStatus(ok ? HFLocalization.Tr("hostfix.presets.deleted", p.Name) : HFLocalization.Tr("hostfix.presets.failed"),
                      ok ? VanillaUI.Good : VanillaUI.Bad);
        }

        [HideFromIl2Cpp]
        private void StartRename(SettingsPresets.Preset p) {
            renaming = p;
            buffer = p.Name;
            confirmKey = null;
            Rebuild();
        }

        // ====================================================================
        // Panel
        // ====================================================================
        [HideFromIl2Cpp]
        private void Build() {
            try {
                int pages = Mathf.Max(1, (presets.Count + PageSize - 1) / PageSize);
                page = Mathf.Clamp(page, 0, pages - 1);
                var shown = presets.Skip(page * PageSize).Take(PageSize).ToList();

                root = VanillaUI.Canvas("UTSSettingsPresetsUI", 9500, true);
                LobbyPanelGuard.Track(root);
                VanillaUI.Backdrop(root, Close);

                float height = 262f + PageSize * RowStep + 120f;
                var panel = VanillaUI.CenterPanel(root, new Vector2(Width, height));
                float y = VanillaUI.Title(panel, HFLocalization.Tr("hostfix.presets.title"), 16f, 34f, VanillaUI.Teal);
                VanillaUI.Subtitle(panel, HFLocalization.Tr("hostfix.presets.subtitle"), y - 6f, 40f);
                VanillaUI.CloseButton(panel, "", Close);

                // name field + SAVE / RENAME
                const float fieldTop = -168f, fieldH = 58f;
                var field = VanillaUI.Box(panel, new Vector2(Inset + 5f, fieldTop), new Vector2(Width - 2 * Inset - 10f - 2 * (ButtonW + 40f) - Gap, fieldH),
                                          VanillaUI.FieldLight);
                fieldText = VanillaUI.Label(field, "", 22f, Color.white, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                                            new Vector2(8f, 0f), new Vector2(-40f, 0f), TMPro.TextAlignmentOptions.Left, TMPro.FontStyles.Bold);
                fieldText.enableWordWrapping = false;
                fieldText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
                UpdateField();
                var tr = new Vector2(1, 1);
                bool saveConfirm = confirmKey != null && confirmKey.StartsWith("save:");
                VanillaUI.Button(panel, HFLocalization.Tr(renaming != null ? "hostfix.presets.rename_do" : saveConfirm ? "hostfix.presets.sure" : "hostfix.presets.save"),
                                 new Vector2(-Inset - 5f - (renaming != null ? ButtonW + 40f + Gap : 0f), fieldTop - 2f), new Vector2(ButtonW + 40f, fieldH - 4f),
                                 renaming != null ? VanillaUI.Amber : saveConfirm ? VanillaUI.Red : VanillaUI.Green, Commit,
                                 anchorMin: tr, anchorMax: tr, pivot: tr);
                if (renaming != null)
                    VanillaUI.Button(panel, HFLocalization.Tr("hostfix.presets.cancel"), new Vector2(-Inset - 5f, fieldTop - 2f), new Vector2(ButtonW + 40f, fieldH - 4f),
                                     VanillaUI.Grey, () => { renaming = null; buffer = ""; Rebuild(); }, anchorMin: tr, anchorMax: tr, pivot: tr);
                else
                    VanillaUI.Button(panel, HFLocalization.Tr("hostfix.presets.folder"), new Vector2(-Inset - 5f - ButtonW - 40f - Gap, fieldTop - 2f),
                                     new Vector2(ButtonW + 40f, fieldH - 4f), VanillaUI.Blue, SettingsPresets.OpenFolder, anchorMin: tr, anchorMax: tr, pivot: tr);

                // the presets
                y = fieldTop - fieldH - 26f;
                if (presets.Count == 0)
                    VanillaUI.Text(panel, HFLocalization.Tr("hostfix.presets.none"), 18f, VanillaUI.Muted, new Vector2(Inset + 10f, y - 10f),
                                   new Vector2(Width - 2 * Inset - 20f, 80f), TMPro.FontStyles.Italic);
                foreach (var p in shown) { BuildRow(panel, p, y); y -= RowStep; }

                // footer: status line, pages
                VanillaUI.Label(panel, status, 17f, statusColor, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0),
                                new Vector2(Inset + 10f, 30f), new Vector2(-2 * Inset - 20f - (pages > 1 ? 260f : 0f), 56f),
                                TMPro.TextAlignmentOptions.Left);
                if (pages > 1) {
                    var br = new Vector2(1, 0);
                    VanillaUI.Button(panel, "<", new Vector2(-Inset - 170f, 30f), new Vector2(60f, 50f), page > 0 ? VanillaUI.Blue : VanillaUI.Grey,
                                     () => { if (page > 0) { page--; Rebuild(); } }, anchorMin: br, anchorMax: br, pivot: br);
                    VanillaUI.Label(panel, $"{page + 1} / {pages}", 18f, Color.white, br, br, br, new Vector2(-Inset - 70f, 30f), new Vector2(100f, 50f),
                                    TMPro.TextAlignmentOptions.Center, TMPro.FontStyles.Bold);
                    VanillaUI.Button(panel, ">", new Vector2(-Inset, 30f), new Vector2(60f, 50f), page < pages - 1 ? VanillaUI.Blue : VanillaUI.Grey,
                                     () => { if (page < pages - 1) { page++; Rebuild(); } }, anchorMin: br, anchorMax: br, pivot: br);
                }
            } catch (Exception ex) {
                HostFixPlugin.Logger?.LogError($"[Presets] panel failed: {ex}");
                Teardown();
            }
        }

        [HideFromIl2Cpp]
        private void BuildRow(GameObject panel, SettingsPresets.Preset p, float y) {
            bool last = SettingsPresets.LastName != null && string.Equals(SettingsPresets.LastName, p.Name, StringComparison.OrdinalIgnoreCase);
            bool editing = renaming != null && renaming.Path == p.Path;
            var row = VanillaUI.Row(panel, y, RowH, Inset, last || editing ? VanillaUI.FieldLight : (Color?)null);

            VanillaUI.Label(row, p.Name + (last ? $"  <color=#5FE0D2><size=70%>{HFLocalization.Tr("hostfix.presets.current_tag")}</size></color>" : ""),
                            20f, Color.white, new Vector2(0, 0.5f), new Vector2(0, 1), new Vector2(0, 0.5f),
                            new Vector2(18f, -2f), new Vector2(560f, 0f), TMPro.TextAlignmentOptions.Left, TMPro.FontStyles.Bold);
            int d = diffs.TryGetValue(p.Path, out var n) ? n : -1;
            string diff = d == 0 ? HFLocalization.Tr("hostfix.presets.matches") : d > 0 ? HFLocalization.Tr("hostfix.presets.differ", d) : "";
            string meta = HFLocalization.Tr("hostfix.presets.meta", p.Saved.ToString("dd.MM.yyyy  HH:mm"), p.Options.Count)
                          + (p.Vanilla != null ? "" : " " + HFLocalization.Tr("hostfix.presets.no_vanilla")) + (diff.Length > 0 ? "  ·  " + diff : "");
            VanillaUI.Label(row, meta, 14f, d == 0 ? VanillaUI.Good : VanillaUI.Muted, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                            new Vector2(18f, 2f), new Vector2(560f, 0f), TMPro.TextAlignmentOptions.Left);

            float bh = RowH - 18f, x = -10f;
            var a = new Vector2(1, 0.5f);
            bool delConfirm = confirmKey == "del:" + p.Path, updConfirm = confirmKey == "upd:" + p.Path;
            VanillaUI.Button(row, HFLocalization.Tr(delConfirm ? "hostfix.presets.sure" : "hostfix.presets.delete"), new Vector2(x, 0), new Vector2(ButtonW, bh),
                             VanillaUI.Red, () => DeletePreset(p), anchorMin: a, anchorMax: a, pivot: a);
            x -= ButtonW + Gap;
            VanillaUI.Button(row, HFLocalization.Tr("hostfix.presets.rename"), new Vector2(x, 0), new Vector2(ButtonW, bh),
                             editing ? VanillaUI.Amber : VanillaUI.Grey, () => StartRename(p), anchorMin: a, anchorMax: a, pivot: a);
            x -= ButtonW + Gap;
            VanillaUI.Button(row, HFLocalization.Tr(updConfirm ? "hostfix.presets.sure" : "hostfix.presets.update"), new Vector2(x, 0), new Vector2(ButtonW, bh),
                             updConfirm ? VanillaUI.Red : VanillaUI.Blue, () => UpdatePreset(p), anchorMin: a, anchorMax: a, pivot: a);
            x -= ButtonW + Gap;
            VanillaUI.Button(row, HFLocalization.Tr("hostfix.presets.load"), new Vector2(x, 0), new Vector2(ButtonW, bh),
                             d == 0 ? VanillaUI.Grey : VanillaUI.Green, () => LoadPreset(p), anchorMin: a, anchorMax: a, pivot: a);
            x -= ButtonW + Gap;
            VanillaUI.Button(row, p.HasPlayers ? HFLocalization.Tr("hostfix.presets.players_range", p.MinPlayers, p.MaxPlayers) : HFLocalization.Tr("hostfix.presets.players_none"),
                             new Vector2(x, 0), new Vector2(PlayersW, bh), p.HasPlayers ? VanillaUI.Teal : VanillaUI.Grey, () => CyclePlayers(p),
                             anchorMin: a, anchorMax: a, pivot: a);
        }

        // ---- the bean stays put while the keyboard types the name (LobbyPasswordGate's rule: restore once) ----
        private void Freeze() {
            try {
                var lp = PlayerControl.LocalPlayer;
                if (lp == null) return;
                if (lp.moveable) { lp.moveable = false; frozeMove = true; }
            } catch { }
        }

        private void Unfreeze() {
            if (!frozeMove) return;
            frozeMove = false;
            try { if (PlayerControl.LocalPlayer != null) PlayerControl.LocalPlayer.moveable = true; } catch { }
        }

        // ---- the game's collider buttons under the panel (same remedy as DraftMusicUI) ----
        private static readonly List<PassiveButton> blocked = new List<PassiveButton>();

        [HideFromIl2Cpp]
        private void BlockBackground() {
            if (blocked.Count > 0 || root == null) return;
            try {
                foreach (var b in FindObjectsOfType<PassiveButton>())
                    if (b != null && b.enabled && !b.transform.IsChildOf(root.transform)) { b.enabled = false; blocked.Add(b); }
            } catch (Exception e) { HostFixPlugin.Logger?.LogWarning($"[Presets] background block: {e.Message}"); }
        }

        [HideFromIl2Cpp]
        private static void UnblockBackground() {
            foreach (var b in blocked) try { if (b != null) b.enabled = true; } catch { }
            blocked.Clear();
        }

        // ---- autotest ----
        [HideFromIl2Cpp]
        internal void DiagType(string name) { buffer = name; UpdateField(); }

        [HideFromIl2Cpp]
        internal void DiagRefresh() { Reload(); Rebuild(); }

        [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
        static class LobbyResetPatch {
            public static void Postfix() => Instance?.Close();
        }
    }
}

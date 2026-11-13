// TOR Host Tools - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.

/*
 * ConnectionLightUI - the host's panel for the connection traffic light (lobby menu entry "Connections").
 * The measuring itself is part of TOR - Forgotten Fixes (ConnectionLight): every client has to answer the host's
 * ping, and only that mod is installed for everyone. This panel only reads ConnectionLight.Rows().
 */

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace HostFixPlugin {

    /// <summary>The host's panel, entry "Connections" in the lobby menu.</summary>
    public class ConnectionLightUI : MonoBehaviour {
        public static ConnectionLightUI Instance { get; private set; }

        public ConnectionLightUI(IntPtr ptr) : base(ptr) { }

        private GameObject root;
        private float nextRefresh;

        private const float Width = 980f, Inset = 30f, RowH = 40f, RowStep = 44f;
        private const int MaxRows = 16;

        public void Awake() {
            if (Instance) Destroy(Instance);
            Instance = this;
            LobbyMenu.Add("uts.connections", 45, ShouldShow, MenuLabel, () => Instance?.Toggle(), VanillaUI.Blue);
        }

        private static bool ShouldShow() {
            try {
                var c = AmongUsClient.Instance;
                // the measuring is Forgotten Fixes' (every client answers its ping): without it there is nothing to show
                return ConnectionLight.Available && LobbyScreen.Exists && c != null && c.AmHost && c.NetworkMode != NetworkModes.FreePlay;
            } catch { return false; }
        }

        private static string MenuLabel() {
            string s = HFLocalization.Tr("hostfix.connections.button");
            try {
                var rows = ConnectionLight.Rows();
                int red = rows.Count(r => r.Level == ConnectionLight.Level.Red), yellow = rows.Count(r => r.Level == ConnectionLight.Level.Yellow);
                if (red > 0 || yellow > 0) s += " " + HFLocalization.Tr("hostfix.connections.button_counts", red, yellow);
            } catch { }
            return s;
        }

        public void Update() {
            WatchForRed();
            if (root == null) return;
            if (Input.GetKeyDown(KeyCode.Escape) || !ShouldShow()) { Close(); return; }
            if (Time.realtimeSinceStartup < nextRefresh) return;
            Teardown();
            Build();
        }

        // ---- warning: a player turns red (User 2026-11-13: "warn the host before somebody drops") ----
        // Every 5 s while hosting online, lobby or round: a player whose light turned red since the last look
        // gets one line in the game's own notification popup, at most once per 3 minutes per player.
        private long nextWatch;
        private readonly HashSet<string> redNow = new HashSet<string>();
        private readonly Dictionary<string, long> warnedAt = new Dictionary<string, long>();

        [HideFromIl2Cpp]
        private void WatchForRed() {
            long now = Environment.TickCount64;
            if (now < nextWatch) return;
            nextWatch = now + 5000;
            try {
                var c = AmongUsClient.Instance;
                if (!ConnectionLight.Available || c == null || !c.AmHost || c.NetworkMode == NetworkModes.FreePlay || c.allClients == null || c.allClients.Count < 2) {
                    redNow.Clear();
                    return;
                }
                var red = new HashSet<string>();
                foreach (var r in ConnectionLight.Rows()) {
                    if (r.Level != ConnectionLight.Level.Red) continue;
                    red.Add(r.Name);
                    if (redNow.Contains(r.Name)) continue;
                    if (warnedAt.TryGetValue(r.Name, out long at) && now - at < 180000) continue;
                    warnedAt[r.Name] = now;
                    var hud = HudManager.Instance;
                    if (hud != null && hud.Notifier != null)
                        hud.Notifier.AddDisconnectMessage(HFLocalization.Tr("hostfix.connections.warn_red", r.Name.Replace("<", "").Replace(">", ""), Detail(r)));
                    HostFixPlugin.Logger?.LogInfo($"[Connections] {r.Name} turned red: {Detail(r)}");
                }
                redNow.Clear();
                redNow.UnionWith(red);
            } catch (Exception e) {
                HostFixPlugin.Logger?.LogWarning($"[Connections] watch failed: {e.Message}");
            }
        }

        [HideFromIl2Cpp]
        public void Toggle() {
            if (root != null) Close();
            else Build();
        }

        [HideFromIl2Cpp]
        public void Close() => Teardown();

        private void Teardown() {
            if (root != null) { Destroy(root); root = null; LobbyPanelGuard.Closed(); }
        }

        [HideFromIl2Cpp]
        private static string Detail(ConnectionLight.Row r) {
            var parts = new List<string>();
            if (r.Median >= 0) parts.Add(HFLocalization.Tr("hostfix.connections.times", r.Median, r.Worst));
            else if (r.Asked > 0) parts.Add(HFLocalization.Tr("hostfix.connections.no_answer"));
            else parts.Add(HFLocalization.Tr("hostfix.connections.measuring"));
            if (r.Lost > 0 && r.Median >= 0) parts.Add(HFLocalization.Tr("hostfix.connections.lost", r.Lost, r.Asked));
            if (r.Drops > 0) parts.Add(HFLocalization.Tr("hostfix.connections.drops", r.Drops));
            return string.Join("  ·  ", parts);
        }

        [HideFromIl2Cpp]
        private void Build() {
            try {
                nextRefresh = Time.realtimeSinceStartup + 2f;
                var rows = ConnectionLight.Rows();
                var shown = rows.Take(MaxRows).ToList();

                root = VanillaUI.Canvas("UTSConnectionLightUI", 9500, true);
                LobbyPanelGuard.Track(root);
                VanillaUI.Backdrop(root, Close);

                float height = 190f + Mathf.Max(1, shown.Count) * RowStep + (rows.Count > shown.Count ? 40f : 0f) + 50f;
                var panel = VanillaUI.CenterPanel(root, new Vector2(Width, height));
                float y = VanillaUI.Title(panel, HFLocalization.Tr("hostfix.connections.title"), 16f, 34f, VanillaUI.Blue);
                VanillaUI.Subtitle(panel, HFLocalization.Tr("hostfix.connections.subtitle"), y - 6f, 40f);
                VanillaUI.CloseButton(panel, "", Close);

                y = -150f;
                if (rows.Count == 0)
                    VanillaUI.Text(panel, HFLocalization.Tr("hostfix.connections.alone"), 18f, VanillaUI.Muted, new Vector2(Inset + 10f, y - 6f),
                                   new Vector2(Width - 2 * Inset - 20f, 60f), TMPro.FontStyles.Italic);
                foreach (var r in shown) {
                    var row = VanillaUI.Row(panel, y, RowH, Inset);
                    VanillaUI.Box(row, new Vector2(14f, -(RowH - 22f) / 2f), new Vector2(22f, 22f), ConnectionLight.ColorOf(r.Level), 11);
                    var name = VanillaUI.Label(row, r.Name, 19f, Color.white, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f),
                                               new Vector2(50f, 0f), new Vector2(260f, 0f), TMPro.TextAlignmentOptions.Left, TMPro.FontStyles.Bold);
                    name.enableWordWrapping = false;
                    name.overflowMode = TMPro.TextOverflowModes.Ellipsis;
                    VanillaUI.Label(row, Detail(r), 15f, r.Level == ConnectionLight.Level.Green ? VanillaUI.Muted : Color.white,
                                    new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 0.5f),
                                    new Vector2(320f, 0f), new Vector2(-340f, 0f), TMPro.TextAlignmentOptions.Left);
                    y -= RowStep;
                }
                if (rows.Count > shown.Count)
                    VanillaUI.Text(panel, HFLocalization.Tr("hostfix.connections.more", rows.Count - shown.Count), 15f, VanillaUI.Muted,
                                   new Vector2(Inset + 10f, y - 4f), new Vector2(Width - 2 * Inset - 20f, 30f), TMPro.FontStyles.Italic);
            } catch (Exception ex) {
                HostFixPlugin.Logger?.LogError($"[ConnectionLight] panel failed: {ex}");
                Teardown();
            }
        }

        // the panel must not outlive the lobby
        [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
        private static class BeginGamePatch {
            public static void Postfix() => Instance?.Close();
        }
    }
}

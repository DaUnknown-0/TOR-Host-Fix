// TOR Host Tools - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.

/*
 * HostMenuUI - Host Tools' own lobby menu, for a lobby without TOR - Forgotten Fixes (User 2026-11-13: Host Tools has
 * to work without it). With Forgotten Fixes the entries go into its lobby menu (LobbyMenu.HandOver, tried every
 * second because that menu may publish itself after this plugin started) and nothing of this shows.
 *
 * Without it: the corner tile bottom left (the game's own tile sprite with three bars, like Forgotten Fixes' menu),
 * a click opens the list of the visible entries as buttons; a click on an entry runs it and closes the list.
 */

using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace HostFixPlugin;

public class HostMenuUI : MonoBehaviour
{
    public HostMenuUI(IntPtr ptr) : base(ptr) { }

    private static HostMenuUI instance;
    public void Awake() => instance = this;

    private GameObject tileRoot, listRoot;
    private float nextHandOver, lobbySince = -1f;

    public void Update()
    {
        try
        {
            float now = Time.realtimeSinceStartup;
            if (!LobbyMenu.UtsMenu && now >= nextHandOver) { nextHandOver = now + 1f; LobbyMenu.HandOver(); }
            HostRoundRecorder.Tick();

            bool lobby = LobbyScreen.Exists && AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;
            if (!lobby) { lobbySince = -1f; Hide(); return; }
            if (lobbySince < 0f) lobbySince = now;
            // Forgotten Fixes' menu took the entries, or it may still come (3 s after the lobby appeared)
            bool own = !LobbyMenu.UtsMenu && now - lobbySince > 3f && LobbyMenu.Entries.Any(Visible);
            if (!own) { Hide(); return; }
            if (tileRoot == null) BuildTile();
            if (!tileRoot.activeSelf) tileRoot.SetActive(true);
            if (listRoot != null && Input.GetKeyDown(KeyCode.Escape)) CloseList();
        }
        catch (Exception e) { HostFixPlugin.Logger?.LogWarning($"[HostMenu] {e.Message}"); }
    }

    private static bool Visible(LobbyMenu.Entry e)
    {
        try { return e.Visible == null || e.Visible(); } catch { return false; }
    }

    private void Hide()
    {
        if (tileRoot != null && tileRoot.activeSelf) tileRoot.SetActive(false);
        CloseList();
    }

    [HideFromIl2Cpp]
    private void BuildTile()
    {
        tileRoot = VanillaUI.Canvas("HostToolsMenuTile", 9000, false);
        var tile = VanillaUI.Tile(tileRoot, 76f, VanillaUI.GlyphMenu(), Color.white, ToggleList);
        tile.GetComponent<RectTransform>().anchoredPosition = new Vector2(20f, 20f);
    }

    [HideFromIl2Cpp]
    private void ToggleList()
    {
        if (listRoot != null) { CloseList(); return; }
        var entries = LobbyMenu.Entries.Where(Visible).OrderBy(e => e.Order).ToList();
        if (entries.Count == 0) return;
        listRoot = VanillaUI.Canvas("HostToolsMenu", 9400, true);
        LobbyPanelGuard.Track(listRoot);
        VanillaUI.Backdrop(listRoot, CloseList);
        const float W = 460f, BtnH = 54f, Gap = 10f;
        float h = 120f + entries.Count * (BtnH + Gap);
        var panel = VanillaUI.Panel(listRoot, new Vector2(W, h), Vector2.zero, Vector2.zero, new Vector2(20f, 110f));
        float y = VanillaUI.Title(panel, HFLocalization.Tr("hostfix.tools.title"), 12f, 28f);
        y -= 8f;
        var tl = new Vector2(0, 1);
        foreach (var e in entries)
        {
            var entry = e;
            string label;
            try { label = entry.Label?.Invoke() ?? entry.Id; } catch { label = entry.Id; }
            VanillaUI.Button(panel, label, new Vector2(24f, y), new Vector2(W - 48f, BtnH), entry.Color,
                             () => { CloseList(); try { entry.Click?.Invoke(); } catch (Exception ex) { HostFixPlugin.Logger?.LogWarning($"[HostMenu] {entry.Id}: {ex.Message}"); } },
                             anchorMin: tl, anchorMax: tl, pivot: tl);
            y -= BtnH + Gap;
        }
    }

    [HideFromIl2Cpp]
    private void CloseList()
    {
        if (listRoot == null) return;
        Destroy(listRoot);
        listRoot = null;
        LobbyPanelGuard.Closed();
    }

    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
    private static class BeginGamePatch
    {
        public static void Postfix() { try { instance?.CloseList(); } catch { } }
    }
}

// TOR Host Tools - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.

/*
 * HostTools - the tools half of this mod (since 2026-11-11 it is "TOR - Host Tools", before that "TOR - Hostfix").
 *
 * Since 2026-11-13 the tools need nothing but The Other Roles either (User: "Host Tools has to work without UC/UTS"):
 * the UI building blocks are copies (Shared*.cs: VanillaUI, GameUI, ModalGuard, LobbyPanelGuard), the rest stands in
 * through HostShared.cs, and the statistic records its rounds itself (HostRoundRecorder). With TOR - Forgotten Fixes
 * installed its lobby menu, paced option sync, translations and connection measuring are used through reflection.
 * One case keeps the tools off: a Forgotten Fixes older than the move (it still carries presets and the web page).
 *
 * What lives here and came from Forgotten Fixes: the settings presets (SettingsPresets, SettingsPresetsUI), the
 * settings export and import (SettingsShare), the local web settings page (WebConfig), the lobby size above 15
 * (LobbySizeLimit, "/size") and the panel of the
 * connection traffic light (ConnectionLightUI; the measuring stays in Forgotten Fixes, every client answers it, so the
 * panel is there only with that mod).
 * New here: the start check, the settings log, the evening plan, the player notes, the settings rescue after a lost
 * lobby and the lobby guard.
 *
 * Patches: this plugin patches class by class (PatchAll(Type) does not descend into nested classes), so every
 * tool is patched through PatchTree, which walks the nested classes.
 */

using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace HostFixPlugin;

internal static class HostTools
{
    private const string ForgottenFixesGuid = "com.tormod.usefultorstuff";

    /// <summary>The tools are running (Forgotten Fixes found and new enough).</summary>
    internal static bool Active { get; private set; }

    internal static ConfigEntry<bool> WebConfigEnabled;
    internal static ConfigEntry<int> WebConfigPort;

    internal static void TryBoot(HostFixPlugin plugin, Harmony harmony)
    {
        try
        {
            // An older Forgotten Fixes still carries the presets and the web page itself; starting them here as well
            // would run both twice. Without Forgotten Fixes, or with a current one, the tools start.
            if (IL2CPPChainloader.Instance.Plugins.TryGetValue(ForgottenFixesGuid, out var info) && info?.Instance != null)
            {
                var asm = info.Instance.GetType().Assembly;
                if (asm.GetType("UsefulTORStuff.SettingsPresets") != null || asm.GetType("UsefulTORStuff.WebConfig") != null)
                {
                    HostFixPlugin.Logger.LogWarning("[HostTools] TOR - Forgotten Fixes is older than this mod expects: the tools stay off (that version still has the presets and the web page itself). Update Forgotten Fixes.");
                    return;
                }
            }
            Boot(plugin, harmony);
            Active = true;
            HostFixPlugin.Logger.LogInfo($"[HostTools] tools started ({(Uts.Loaded ? "with" : "without")} TOR - Forgotten Fixes).");
        }
        catch (Exception e)
        {
            HostFixPlugin.Logger.LogError($"[HostTools] start failed, the fixes keep running: {e}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Boot(HostFixPlugin plugin, Harmony harmony)
    {
        WebConfigEnabled = plugin.Config.Bind("WebConfig", "Enabled", true,
            "Serve a local browser page (http://127.0.0.1:<port>/) for editing all lobby settings. "
            + "Loopback-only (never exposed to the network); only the host can change values.");
        WebConfigPort = plugin.Config.Bind("WebConfig", "Port", 32200,
            "TCP port for the local settings web page. If busy, the next few ports are tried.");
        LobbySizeLimit.CreateOptions();
        HostNotes.Bind(plugin.Config);
        LobbyGuard.Bind(plugin.Config);

        foreach (var t in new[]
                 {
                     typeof(SettingsPresets), typeof(SettingsPresetsUI), typeof(SettingsShare), typeof(WebConfig), typeof(LobbySizeLimit),
                     typeof(ConnectionLightUI), typeof(StartCheck), typeof(SettingsLog), typeof(EveningPlan),
                     typeof(HostNotes), typeof(SettingsRescue), typeof(LobbyGuard), typeof(HostToolsUI),
                     typeof(GameUI), typeof(ModalGuard), typeof(LobbyPanelGuard), typeof(HostRoundRecorder), typeof(HostMenuUI),
                 })
            PatchTree(harmony, t);

        plugin.AddComponent<SettingsPresetsUI>();
        plugin.AddComponent<ConnectionLightUI>();
        plugin.AddComponent<HostToolsUI>();
        plugin.AddComponent<HostModalGuardTicker>();
        plugin.AddComponent<HostMenuUI>();

        if (WebConfigEnabled.Value)
        {
            try { WebConfig.Start(WebConfigPort.Value); }
            catch (Exception ex) { HostFixPlugin.Logger.LogError($"[HostTools] web settings page did not start: {ex}"); }
        }
    }

    /// <summary>Applies the Harmony patch classes of a type and of everything nested in it.</summary>
    private static void PatchTree(Harmony harmony, Type type)
    {
        try
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
                harmony.CreateClassProcessor(type).Patch();
        }
        catch (Exception e) { HostFixPlugin.Logger.LogError($"[HostTools] patch {type.FullName} failed: {e.Message}"); }
        foreach (var nested in type.GetNestedTypes(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            PatchTree(harmony, nested);
    }
}

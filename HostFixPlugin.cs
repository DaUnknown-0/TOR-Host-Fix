// TOR Host Fix - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

/*
 * HostFixPlugin - External fix for TOR 4.8.0 host bugs
 *
 * Fixes these issues without modifying TOR source:
 *   1. Host cooldowns freezing (RoleDraft.isRunning stuck at true)
 *   2. Any host-only role/modifier/target assignment exception in the draft
 *      coroutine (covers Guesser gamemode, Lawyer target, and modifiers)
 *   3. Snitch reveal missing an evil HOST (TOR resets the room map mid-prefix,
 *      dropping the host's early ShareRoom — the host re-broadcasts its room on a
 *      short delay so it lands after the reset, inside the reveal window)
 *
 * Strategy: minimal, defensive patches. Don't replace TOR methods — just
 * guard them with try-catch and reset stuck state. This way, if TOR updates
 * its internals, the worst that happens is our patches become no-ops.
 *
 * Note: all fixes run host-only. Fix 4 works host-only because the re-broadcast is
 * a normal ShareRoom RPC that TOR's own handler processes on the Snitch's client.
 */

global using Il2CppInterop.Runtime;
global using Il2CppInterop.Runtime.Attributes;
global using Il2CppInterop.Runtime.InteropTypes;
global using Il2CppInterop.Runtime.InteropTypes.Arrays;
global using Il2CppInterop.Runtime.Injection;

using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Hazel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace HostFixPlugin;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("Among Us.exe")]
[BepInDependency("me.eisbison.theotherroles", BepInDependency.DependencyFlags.HardDependency)]
public class HostFixPlugin : BasePlugin
{
    public const string PluginGuid = "com.trackerteam.hostfix";
    public const string PluginName = "TOR Host Fix";
    public const string PluginVersion = "1.0.0";
    public static readonly System.Version Version = System.Version.Parse(PluginVersion);

    public static ManualLogSource Logger { get; private set; }

    // Cached reflection handles — resolved once at startup
    internal static FieldInfo RoleDraftIsRunningField;
    internal static FieldInfo RoleDraftPickOrderField;
    internal static FieldInfo SnitchSnitchField;
    internal static Assembly TORAssembly;

    public override void Load()
    {
        Logger = Log;
        Logger.LogInfo($"{PluginName} v{PluginVersion} loading...");

        if (!ResolveTORTypes())
        {
            Logger.LogError("Failed to resolve TOR types — plugin disabled.");
            return;
        }

        var harmony = new Harmony(PluginGuid);

        // --- Fix 1: Reset RoleDraft.isRunning in resetVariables() ---
        PatchResetVariables(harmony);

        // --- Fix 2: Wrap host-only role-assignment calls in try-catch ---
        // All three run on the host-only path in RoleDraft.CoSelectRoles (lines 308-310).
        // Any uncaught exception kills the coroutine before isRunning = false (line 323).
        PatchHostOnlyAssignment(harmony, "TheOtherRoles.Patches.RoleManagerSelectRolesPatch", "assignRoleTargets");
        PatchHostOnlyAssignment(harmony, "TheOtherRoles.Patches.RoleManagerSelectRolesPatch", "assignGuesserGamemode");
        PatchHostOnlyAssignment(harmony, "TheOtherRoles.Patches.RoleManagerSelectRolesPatch", "assignModifiers");

        // --- Fix 3: Safety net via HudManager.Update (attribute-based for IL2CPP) ---
        // Applied automatically via [HarmonyPatch] attribute below
        harmony.PatchAll(typeof(HudManagerSafetyNet));

        // --- Fix 4: Snitch reveal misses an evil host (host re-broadcasts its room after TOR's reset) ---
        if (SnitchSnitchField != null)
        {
            harmony.PatchAll(typeof(SnitchHostRoomFix));
            Logger.LogInfo("Patched StartMeeting — Snitch host-room fix applied.");
        }
        else
        {
            Logger.LogWarning("Snitch host-room fix skipped (Snitch.snitch unresolved).");
        }

        // Version display in the top-corner PingTracker (host only).
        harmony.PatchAll(typeof(VersionDisplayPatch));

        // Self-updater: checks GitHub releases and offers an in-game update button.
        AddComponent<HostFixUpdater>();

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded — all patches applied.");
    }

    /// <summary>
    /// Locate TOR types and fields via reflection.
    /// TOR is a managed BepInEx plugin, so standard reflection works on its types.
    /// </summary>
    private bool ResolveTORTypes()
    {
        try
        {
            TORAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "TheOtherRoles");

            if (TORAssembly == null)
            {
                Logger.LogError("TheOtherRoles assembly not found.");
                return false;
            }

            var roleDraftType = TORAssembly.GetType("TheOtherRoles.Modules.RoleDraft");
            if (roleDraftType == null)
            {
                Logger.LogError("RoleDraft type not found.");
                return false;
            }

            RoleDraftIsRunningField = roleDraftType.GetField("isRunning",
                BindingFlags.Public | BindingFlags.Static);
            RoleDraftPickOrderField = roleDraftType.GetField("pickOrder",
                BindingFlags.Public | BindingFlags.Static);

            if (RoleDraftIsRunningField == null)
            {
                Logger.LogError("RoleDraft.isRunning field not found.");
                return false;
            }

            // Snitch.snitch is nested in the TheOtherRoles.TheOtherRoles class. Best-effort:
            // a missing field only disables the Snitch host-room fix, not the whole plugin.
            var snitchType = TORAssembly.GetType("TheOtherRoles.TheOtherRoles+Snitch");
            SnitchSnitchField = snitchType?.GetField("snitch", BindingFlags.Public | BindingFlags.Static);
            if (SnitchSnitchField == null)
                Logger.LogWarning("Snitch.snitch field not found — Snitch host-room fix will be skipped.");

            Logger.LogInfo("TOR types resolved successfully.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error resolving TOR types: {ex}");
            return false;
        }
    }

    // ========================================================================
    // Fix 1: Reset isRunning in resetVariables()
    //
    // RPCProcedure.resetVariables() is a managed method in TOR's assembly.
    // We add a Postfix that forces isRunning = false.
    // This is the most important fix: it breaks the cross-round persistence.
    // ========================================================================

    private void PatchResetVariables(Harmony harmony)
    {
        try
        {
            var rpcProcedureType = TORAssembly.GetType("TheOtherRoles.RPCProcedure");
            var resetMethod = rpcProcedureType?.GetMethod("resetVariables",
                BindingFlags.Public | BindingFlags.Static);

            if (resetMethod == null)
            {
                Logger.LogWarning("RPCProcedure.resetVariables not found — skipping patch.");
                return;
            }

            harmony.Patch(resetMethod,
                postfix: new HarmonyMethod(typeof(ResetVariablesPatch),
                    nameof(ResetVariablesPatch.Postfix)));

            Logger.LogInfo("Patched resetVariables() — isRunning will be reset each round.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to patch resetVariables: {ex}");
        }
    }

    public static class ResetVariablesPatch
    {
        public static void Postfix()
        {
            try
            {
                bool was = (bool)RoleDraftIsRunningField.GetValue(null);
                RoleDraftIsRunningField.SetValue(null, false);
                if (was)
                    Logger.LogWarning("[Fix1] isRunning was stuck — reset to false.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Fix1] Error: {ex.Message}");
            }
        }
    }

    // ========================================================================
    // Fix 2: Wrap host-only role-assignment calls in a Finalizer
    //
    // Instead of replacing the methods, we use a Harmony Finalizer.
    // If the original method throws (e.g. NullRef from disconnected player
    // or ArgumentOutOfRange from FindIndex == -1), the Finalizer catches it,
    // logs it, and prevents the exception from propagating up and killing
    // the RoleDraft coroutine (which would leave isRunning = true forever).
    //
    // Covers: assignRoleTargets, assignGuesserGamemode, assignModifiers
    // (RoleDraft.cs lines 308-310, all on the host-only path).
    // ========================================================================

    private void PatchHostOnlyAssignment(Harmony harmony, string typeName, string methodName)
    {
        try
        {
            var patchType = TORAssembly.GetType(typeName);
            var method = patchType?.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (method == null)
            {
                Logger.LogWarning($"{methodName} not found — skipping patch.");
                return;
            }

            harmony.Patch(method,
                finalizer: new HarmonyMethod(typeof(HostOnlyAssignmentFinalizer),
                    nameof(HostOnlyAssignmentFinalizer.Finalizer)));

            Logger.LogInfo($"Patched {methodName}() — exceptions will be caught.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to patch {methodName}: {ex}");
        }
    }

    public static class HostOnlyAssignmentFinalizer
    {
        /// <summary>
        /// Harmony Finalizer: runs after the method, receives any thrown exception.
        /// Returning null swallows the exception. This prevents a crash from
        /// killing the RoleDraft coroutine and leaving isRunning stuck.
        ///
        /// __originalMethod is injected by Harmony so one finalizer can serve
        /// all three patched methods while still logging which one threw.
        /// </summary>
        public static Exception Finalizer(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception != null)
            {
                string name = __originalMethod?.Name ?? "unknown";
                Logger.LogError($"[Fix2] {name} crashed: {__exception.Message}");
                Logger.LogWarning("[Fix2] Exception swallowed to protect RoleDraft coroutine.");
            }
            return null; // Swallow exception
        }
    }

    // ========================================================================
    // Fix 3: Safety net — detect stuck isRunning via HudManager.Update
    //
    // Uses attribute-based patching (works reliably with IL2CPP).
    // Monitors RoleDraft.isRunning during active gameplay:
    //   - If pickOrder is empty but isRunning is true → draft is stuck → reset
    //   - If pickOrder has entries but current picker is disconnected → remove
    // ========================================================================

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    public static class HudManagerSafetyNet
    {
        private static float _stuckTimer;
        private static float _disconnectCheckTimer;
        private static bool _loggedReset;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)] // Run after TOR's own HudManager patch
        public static void Postfix()
        {
            try
            {
                if (RoleDraftIsRunningField == null) return;

                // Only check on the host, only during active games
                if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost) return;
                if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Started)
                {
                    _stuckTimer = 0f;
                    _loggedReset = false;
                    return;
                }

                // Fix 4's delayed Snitch re-broadcast. Runs regardless of the draft state below
                // (the draft is idle during meetings), so flush it before the isRunning early-out.
                SnitchHostRoomFix.Tick();

                bool isRunning = (bool)RoleDraftIsRunningField.GetValue(null);
                if (!isRunning)
                {
                    _stuckTimer = 0f;
                    _loggedReset = false;
                    return;
                }

                // isRunning is true during gameplay — is the draft actually still going?
                var pickOrder = RoleDraftPickOrderField?.GetValue(null) as List<byte>;
                if (pickOrder == null) return;

                if (pickOrder.Count == 0)
                {
                    // Draft should be done (no more picks), but isRunning is still true.
                    // Give a grace period for the post-draft fade + assignment code.
                    _stuckTimer += Time.deltaTime;

                    if (_stuckTimer > 15f)
                    {
                        RoleDraftIsRunningField.SetValue(null, false);
                        if (!_loggedReset)
                        {
                            Logger.LogWarning(
                                $"[Fix3] Safety net: isRunning stuck for {_stuckTimer:F0}s " +
                                "with empty pickOrder — forced reset.");
                            _loggedReset = true;
                        }
                        _stuckTimer = 0f;
                    }
                }
                else
                {
                    // Draft has picks remaining — check for disconnected current picker
                    _stuckTimer = 0f;
                    _disconnectCheckTimer += Time.deltaTime;
                    if (_disconnectCheckTimer >= 3f)
                    {
                        _disconnectCheckTimer = 0f;
                        RemoveDisconnectedPicker(pickOrder);
                    }
                }
            }
            catch
            {
                // Never let the safety net crash the game
            }
        }

        private static void RemoveDisconnectedPicker(List<byte> pickOrder)
        {
            if (pickOrder.Count == 0) return;

            byte currentPickerId = pickOrder[0];
            PlayerControl picker = null;

            foreach (var pc in PlayerControl.AllPlayerControls)
            {
                if (pc != null && pc.PlayerId == currentPickerId)
                {
                    picker = pc;
                    break;
                }
            }

            if (picker == null || picker.Data == null || picker.Data.Disconnected)
            {
                pickOrder.RemoveAt(0);
                Logger.LogWarning(
                    $"[Fix3] Removed disconnected picker (ID {currentPickerId}) from draft.");
            }
        }
    }

    // ========================================================================
    // Fix 4: Snitch reveal misses an evil HOST
    //
    // TOR's StartMeeting prefix has every client broadcast its room via the
    // ShareRoom RPC, then resets Snitch.playerRoomMap at the END of that same
    // prefix. The host initiates the meeting, so its ShareRoom (StartRpcImmediately)
    // reaches the Snitch BEFORE the host's own (batched) RpcStartMeeting — i.e.
    // before the Snitch's prefix runs the reset, which then wipes the host's entry.
    // Non-host players send after receiving RpcStartMeeting, so their entries
    // survive. The Chat-mode reveal skips anyone missing from playerRoomMap, so an
    // evil host is systematically left out.
    //
    // Fix: the host re-broadcasts its room on a short delay. Host-only by design —
    // TOR's own ShareRoom handler (present on every client via TOR) writes it into
    // the Snitch's playerRoomMap, so the Snitch needs no extra plugin. The delay
    // makes the re-send leave the host AFTER its batched RpcStartMeeting, so it lands
    // in the Snitch's freshly-reset map and still arrives inside the ~0.4s reveal
    // window. (Map mode uses live positions and is unaffected.) Gated only on a Snitch
    // being in play, read via reflection.
    //
    // This host-side re-broadcast is ALWAYS on (when a Snitch is in play). The host is
    // the only player whose entry the race drops, and re-sending from the host is the
    // one mechanism the host fully controls and that fixes the reveal for EVERY Snitch,
    // modded or not. Useful TOR Stuff also carries a client-side restore that runs on the
    // Snitch's own machine; if both run they write the same host entry — idempotent. We do
    // NOT stand down for it: that client-side restore only helps a modded Snitch and proved
    // unreliable, so disabling the host re-broadcast when "everyone has the mod" removed the
    // only dependable fix exactly when it was assumed unnecessary.
    // ========================================================================

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.StartMeeting))]
    public static class SnitchHostRoomFix
    {
        // TOR's CustomRPC.ShareRoom value (enum is internal to TOR; stable in 4.8.0).
        private const byte ShareRoomRpcId = 167;

        // Delay before the host re-broadcasts. Must clear the host's batched RpcStartMeeting (a frame)
        // yet stay inside the Snitch's ~0.4s reveal lerp. 0.15s leaves ~0.25s of window for jitter.
        private const float RebroadcastDelay = 0.15f;

        private static bool _pending;
        private static float _sendAt;

        [HarmonyPostfix]
        public static void Postfix()
        {
            // The host always initiates the meeting, so only its room loses the race — schedule the
            // delayed re-send on the host only, and only when a Snitch is actually in play.
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            if (SnitchSnitchField == null || SnitchSnitchField.GetValue(null) == null) return;
            _pending = true;
            _sendAt = Time.realtimeSinceStartup + RebroadcastDelay;
        }

        // Flushed once per frame from HudManagerSafetyNet (a host-only HudManager.Update postfix).
        public static void Tick()
        {
            if (!_pending || Time.realtimeSinceStartup < _sendAt) return;
            _pending = false;
            try
            {
                var hud = HudManager.Instance;
                var roomTracker = hud != null ? hud.roomTracker : null;
                byte roomId = roomTracker != null && roomTracker.LastRoom != null
                    ? (byte)roomTracker.LastRoom.RoomId : byte.MinValue;

                MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(
                    PlayerControl.LocalPlayer.NetId, ShareRoomRpcId, SendOption.Reliable, -1);
                writer.Write(PlayerControl.LocalPlayer.PlayerId);
                writer.Write(roomId);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
                Logger.LogInfo($"[Fix4] Re-broadcast host room {roomId} for player {PlayerControl.LocalPlayer.PlayerId}.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Fix4] Snitch host-room re-send failed: {ex.Message}");
            }
        }
    }

    // ========================================================================
    // Version display: show "Host Fix vX.Y.Z" in the top-corner PingTracker,
    // but ONLY for the host (this plugin only needs to run on the host, so the
    // version line is just for the host to confirm it's loaded).
    // ========================================================================

    [HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]
    [HarmonyPriority(Priority.Low)] // run after TOR's own PingTracker postfix
    public static class VersionDisplayPatch
    {
        // Credit toggle is shared across all three of our mods via a process-wide AppDomain flag
        // (no cross-assembly references) — clicking any mod name flips the same flag, so clicking
        // another hides it again. Keep this key string identical in the other mods.
        private const string CreditKey = "TORMods.DaUnknownCreditVisible";

        private static bool CreditVisible() =>
            AppDomain.CurrentDomain.GetData(CreditKey) is bool b && b;

        public static void Postfix(PingTracker __instance)
        {
            if (__instance == null || __instance.text == null) return;
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;

            string text = __instance.text.text;
            if (string.IsNullOrEmpty(text)) return;

            // Click the mod name to toggle the shared credit line. PingTracker.text is a world-space
            // TextMeshPro (no canvas), so the link raycast needs the rendering camera.
            if (Input.GetMouseButtonDown(0))
            {
                Camera cam = Camera.main;
                var canvas = __instance.text.canvas;
                if (canvas != null)
                    cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null
                        : (canvas.worldCamera != null ? canvas.worldCamera : Camera.main);
                int link = TMPro.TMP_TextUtilities.FindIntersectingLink(__instance.text, Input.mousePosition, cam);
                if (link != -1 && __instance.text.textInfo.linkInfo[link].GetLinkID() == "hostFixCredits")
                    AppDomain.CurrentDomain.SetData(CreditKey, !CreditVisible());
            }

            // Clickable mod name, inserted just below the "TheOtherRoles vX" line.
            string line = $"<link=\"hostFixCredits\"><color=#1FA8FF>Host Fix</color> v{PluginVersion}</link>";
            int nl = text.IndexOf('\n');
            text = nl >= 0
                ? text.Substring(0, nl + 1) + line + "\n" + text.Substring(nl + 1)
                : text + "\n" + line;

            // Insert the shared credit under TOR's "Design by Bavari" line — but only if no other
            // mod already added it this frame, so "Modded by DaUnknown" appears at most once.
            if (CreditVisible() && !text.Contains("DaUnknown"))
            {
                string credit = "\n<size=70%>Modded by <color=#FCCE03FF>DaUnknown</color></size>";
                int anchor = text.IndexOf("Bavari");
                if (anchor >= 0)
                {
                    int lineEnd = text.IndexOf('\n', anchor);
                    text = lineEnd >= 0
                        ? text.Substring(0, lineEnd) + credit + text.Substring(lineEnd)
                        : text + credit;
                }
                else
                {
                    text += credit;
                }
            }

            __instance.text.text = text;
        }
    }
}

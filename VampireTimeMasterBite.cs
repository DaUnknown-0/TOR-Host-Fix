// TOR Host Tools - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

/*
 * Vampire bite on a Time Master: the Time Master died at the next meeting start.
 *
 * Cause (TOR 4.8.0): the Vampire bites the Time Master while the time shield is off, the Time
 * Master switches the shield on within the bite delay. When the delay runs out, the Vampire's
 * client calls Helpers.checkMurderAttemptAndKill (Buttons.cs vampireKillButton). The shield
 * blocks it: checkMuderAttempt sends TimeMasterRewindTime (shieldActive = false) and returns
 * SuppressKill. The coroutine only resets Vampire.bitten after a PerformKill, so the bite stays.
 * At the next report, Helpers.handleVampireBiteOnBodyReport resolves the bite again with
 * blockRewind = true, the shield is already gone, and the Time Master dies.
 *
 * TOR - Forgotten Fixes clears the bite on the Vampire's own client (TorRoundLogicFixes, TOR-M6),
 * but only when the Vampire has that mod and its patch on the hot checkMurderAttemptAndKill is
 * still alive (tiered compilation, see DetourWatchdog in Forgotten Fixes).
 *
 * Fix (host only, clients need nothing but TOR): TOR sends TimeMasterRewindTime over the
 * attacker's NetId, so on the host PlayerControl.HandleRpc runs on the attacker. When that is the
 * Vampire and the Vampire's bite currently sits on the Time Master, the shield has just absorbed
 * that bite, and the host sends TOR's own reset (VampireSetBitten 255/255) to everyone. When the
 * host itself is the Vampire there is no incoming RPC: TOR calls RPCProcedure.timeMasterRewindTime
 * locally, which this file also catches (attacker = local player).
 *
 * A rewind caused by somebody else (for example an Impostor hitting the shield while the bite is
 * still pending) leaves the bite alone, so the original timing stays as it was.
 */

using System;
using BepInEx.Configuration;
using HarmonyLib;
using Hazel;
using UnityEngine;
using TorVampire = global::TheOtherRoles.Vampire;
using TorTimeMaster = global::TheOtherRoles.TimeMaster;

namespace HostFixPlugin;

internal static class VampireTimeMasterBite
{
    internal static ConfigEntry<bool> Enabled;

    static byte rewindRpc;
    static byte bittenRpc;
    static bool ready;

    // Attacker of the TimeMasterRewindTime RPC TOR is handling right now (set in the HandleRpc prefix).
    static PlayerControl rpcAttacker;
    static int rpcAttackerFrame = -1;

    internal static void Init(ConfigFile config, Harmony harmony)
    {
        Enabled = config.Bind("Fixes", "VampireTimeMasterBite", true,
            "Clear the Vampire's bite when the Time Master's shield blocks it, so the Time Master does not die at the next meeting.");
        try
        {
            var rpcEnum = HostFixPlugin.TORAssembly.GetType("TheOtherRoles.CustomRPC");
            if (rpcEnum == null || !Enum.IsDefined(rpcEnum, "TimeMasterRewindTime") || !Enum.IsDefined(rpcEnum, "VampireSetBitten"))
            {
                HostFixPlugin.Logger.LogWarning("[VampireTimeMasterBite] TOR's TimeMasterRewindTime/VampireSetBitten not found, fix disabled.");
                return;
            }
            rewindRpc = Convert.ToByte(Enum.Parse(rpcEnum, "TimeMasterRewindTime"));
            bittenRpc = Convert.ToByte(Enum.Parse(rpcEnum, "VampireSetBitten"));
            harmony.PatchAll(typeof(HandleRpcPatch));
            harmony.PatchAll(typeof(RewindPatch));
            ready = true;
            HostFixPlugin.Logger.LogInfo($"[VampireTimeMasterBite] active (rewind rpc {rewindRpc}, bitten rpc {bittenRpc}).");
        }
        catch (Exception e)
        {
            HostFixPlugin.Logger.LogError($"[VampireTimeMasterBite] init failed: {e}");
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
    static class HandleRpcPatch
    {
        // First, so the attacker is known before TOR's own postfix calls RPCProcedure.timeMasterRewindTime.
        [HarmonyPriority(Priority.First)]
        static void Prefix(PlayerControl __instance, [HarmonyArgument(0)] byte callId)
        {
            if (!ready || callId != rewindRpc) return;
            rpcAttacker = __instance;
            rpcAttackerFrame = Time.frameCount;
        }

        // Last, so TOR's postfix (where the rewind is handled) has already run.
        [HarmonyPriority(Priority.Last)]
        static void Postfix([HarmonyArgument(0)] byte callId)
        {
            if (callId != rewindRpc) return;
            rpcAttacker = null;
            rpcAttackerFrame = -1;
        }
    }

    // RPCProcedure.timeMasterRewindTime runs once per blocked attack, so tiered compilation never
    // promotes it (it stays a cold method and the detour holds).
    [HarmonyPatch(typeof(TheOtherRoles.RPCProcedure), nameof(TheOtherRoles.RPCProcedure.timeMasterRewindTime))]
    static class RewindPatch
    {
        static void Prefix()
        {
            try
            {
                if (!ready || !Enabled.Value) return;
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                var attacker = rpcAttacker != null && rpcAttackerFrame == Time.frameCount ? rpcAttacker : PlayerControl.LocalPlayer;
                Check(attacker);
            }
            catch (Exception e)
            {
                HostFixPlugin.Logger.LogError($"[VampireTimeMasterBite] check failed: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    static void Check(PlayerControl attacker)
    {
        var vampire = TorVampire.vampire;
        var bitten = TorVampire.bitten;
        var timeMaster = TorTimeMaster.timeMaster;
        if (attacker == null || vampire == null || attacker.PlayerId != vampire.PlayerId) return;
        if (bitten == null || timeMaster == null || bitten.PlayerId != timeMaster.PlayerId) return;

        var w = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, bittenRpc, SendOption.Reliable, -1);
        w.Write(byte.MaxValue);
        w.Write(byte.MaxValue);
        AmongUsClient.Instance.FinishRpcImmediately(w);
        TheOtherRoles.RPCProcedure.vampireSetBitten(byte.MaxValue, byte.MaxValue);
        HostFixPlugin.Logger.LogInfo($"[VampireTimeMasterBite] time shield blocked the bite on {timeMaster.Data?.PlayerName}, bite cleared.");
    }
}

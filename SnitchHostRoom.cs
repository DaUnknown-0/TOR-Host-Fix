// TOR Host Tools - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

/*
 * Snitch: the evil host was missing from the Snitch's meeting list ("Bad alive roles in game").
 *
 * Cause (TOR 4.8.0, MeetingPatch.cs StartMeetingPatch.Prefix): every client sends its own room
 * (CustomRPC.ShareRoom) at the start of PlayerControl.StartMeeting and, at the end of the same
 * prefix, clears Snitch.playerRoomMap. The Snitch reads that map 0.4 s later.
 * Vanilla PlayerControl.RpcStartMeeting runs StartMeeting locally FIRST and only then sends the
 * StartMeeting RPC. So the host's ShareRoom reaches every other client before their own
 * StartMeeting, and their own reset wipes it. Every other player sends after the meeting started,
 * so only the host is lost.
 *
 * Fix (host only, clients need nothing but TOR): the host sends its ShareRoom again after the
 * StartMeeting RPC is out: once on the next frame, once more 0.15 s later in case the two packets
 * arrive out of order. Both land after the clients' reset and well inside the 0.4 s window.
 * A duplicate entry is harmless: RPCProcedure.shareRoom just overwrites the room.
 */

using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Hazel;
using UnityEngine;

namespace HostFixPlugin;

internal static class SnitchHostRoom
{
    internal static ConfigEntry<bool> Enabled;

    static FieldInfo snitchField;      // TheOtherRoles.Snitch.snitch
    static byte shareRoomRpc;
    static bool ready;

    // pending re-sends of one meeting
    static bool pending;
    static byte pendingRoom;
    static int firstFrame;
    static float secondTime;
    static bool firstDone;

    internal static void Init(ConfigFile config, Harmony harmony)
    {
        Enabled = config.Bind("Fixes", "SnitchHostRoom", true,
            "Re-send the host's room after a meeting starts, so an evil host appears in the Snitch's meeting list.");
        try
        {
            var tor = HostFixPlugin.TORAssembly;
            snitchField = tor.GetType("TheOtherRoles.Snitch")?.GetField("snitch", BindingFlags.Public | BindingFlags.Static);
            var rpcEnum = tor.GetType("TheOtherRoles.CustomRPC");
            if (snitchField == null || rpcEnum == null || !Enum.IsDefined(rpcEnum, "ShareRoom"))
            {
                HostFixPlugin.Logger.LogWarning("[SnitchHostRoom] TOR's Snitch/ShareRoom not found, fix disabled.");
                return;
            }
            shareRoomRpc = Convert.ToByte(Enum.Parse(rpcEnum, "ShareRoom"));
            harmony.PatchAll(typeof(StartMeetingPatch));
            harmony.PatchAll(typeof(HudUpdatePatch));
            ready = true;
            HostFixPlugin.Logger.LogInfo($"[SnitchHostRoom] active (ShareRoom rpc {shareRoomRpc}).");
        }
        catch (Exception e)
        {
            HostFixPlugin.Logger.LogError($"[SnitchHostRoom] init failed: {e}");
        }
    }

    // Same condition and room value as TOR's own send in StartMeetingPatch.Prefix.
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.StartMeeting))]
    static class StartMeetingPatch
    {
        static void Prefix()
        {
            try
            {
                if (!ready || !Enabled.Value) return;
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || PlayerControl.LocalPlayer == null) return;
                if (snitchField.GetValue(null) as PlayerControl == null) return;
                var tracker = HudManager.Instance ? HudManager.Instance.roomTracker : null;
                if (tracker == null) return;
                pendingRoom = tracker.LastRoom != null ? (byte)tracker.LastRoom.RoomId : byte.MinValue;
                firstFrame = Time.frameCount + 1;
                secondTime = Time.realtimeSinceStartup + 0.15f;
                firstDone = false;
                pending = true;
            }
            catch (Exception e) { HostFixPlugin.Logger.LogError($"[SnitchHostRoom] {e.Message}"); }
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    static class HudUpdatePatch
    {
        static void Postfix()
        {
            if (!pending) return;
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || PlayerControl.LocalPlayer == null) { pending = false; return; }
                if (!firstDone && Time.frameCount >= firstFrame) { Send(); firstDone = true; }
                if (firstDone && Time.realtimeSinceStartup >= secondTime) { Send(); pending = false; }
            }
            catch (Exception e) { pending = false; HostFixPlugin.Logger.LogError($"[SnitchHostRoom] {e.Message}"); }
        }
    }

    static void Send()
    {
        var w = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, shareRoomRpc, SendOption.Reliable, -1);
        w.Write(PlayerControl.LocalPlayer.PlayerId);
        w.Write(pendingRoom);
        AmongUsClient.Instance.FinishRpcImmediately(w);
    }
}

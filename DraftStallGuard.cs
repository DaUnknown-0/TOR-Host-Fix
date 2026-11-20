// TOR - Host Tools - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.

/*
 * DraftStallGuard (20.11.2026, User: "weil die wahl hing").
 *
 * In TOR's role draft only the client of the current picker (pickOrder[0]) sends the pick: by click or, when its own timer runs
 * out, with a random role. Every other client just waits. If that client never sends (its draft coroutine died, the message was
 * lost, the game froze), the whole lobby waits forever. Fix 3 only handles a picker who has DISCONNECTED.
 *
 * Here the host steps in for a picker who is still connected: when the same player has been at the head of pickOrder for longer
 * than "Time For Selection" plus a grace period, the host sends TOR's own DraftModePick RPC for that player (TOR reads the player
 * id from the payload) and applies it locally, exactly like RoleDraft.sendPick. The stand-in role is plain Crewmate, or for an
 * impostor plain Impostor (TOR's own fallback when nothing else is left), else the first impostor role nobody has picked yet.
 * Host only, at most once per stuck head, TOR source unchanged.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hazel;
using UnityEngine;

namespace HostFixPlugin;

internal static class DraftStallGuard
{
    private const float GraceSeconds = 6f;

    private static byte _head = byte.MaxValue;
    private static float _since;
    private static bool _forced;

    private static MethodInfo _receivePick;
    private static byte _pickRpc = byte.MaxValue;   // CustomRPC.DraftModePick (internal enum, resolved by name)
    private static FieldInfo _alreadyPicked;
    private static bool _resolved;

    internal static void Reset() { _head = byte.MaxValue; _since = 0f; _forced = false; }

    /// <summary>Called from the host safety net every poll while the draft runs with picks left.</summary>
    internal static void Check(List<byte> pickOrder, float elapsed)
    {
        if (pickOrder == null || pickOrder.Count == 0) { Reset(); return; }
        byte head = pickOrder[0];
        if (head != _head) { _head = head; _since = 0f; _forced = false; return; }
        _since += elapsed;
        if (_forced) return;

        float limit = 5f;
        try { limit = TheOtherRoles.CustomOptionHolder.draftModeTimeToChoose.getFloat(); } catch { }
        if (_since < limit + GraceSeconds) return;

        var player = TheOtherRoles.Helpers.playerById(head);
        if (player == null || player.Data == null || player.Data.Disconnected) return;   // Fix 3 removes those
        _forced = true;
        ForcePick(player);
    }

    private static void ForcePick(PlayerControl player)
    {
        try
        {
            if (!_resolved)
            {
                _resolved = true;
                var draft = HostFixPlugin.TORAssembly?.GetType("TheOtherRoles.Modules.RoleDraft");
                _receivePick = draft?.GetMethod("receivePick", BindingFlags.Public | BindingFlags.Static);
                _alreadyPicked = draft?.GetField("alreadyPicked", BindingFlags.Public | BindingFlags.Static);
                var rpcEnum = HostFixPlugin.TORAssembly?.GetType("TheOtherRoles.CustomRPC");
                if (rpcEnum != null) _pickRpc = Convert.ToByte(Enum.Parse(rpcEnum, "DraftModePick"));
            }
            if (_pickRpc == byte.MaxValue) { HostFixPlugin.Logger.LogWarning("[DraftStallGuard] CustomRPC.DraftModePick not found."); return; }
            if (_receivePick == null) { HostFixPlugin.Logger.LogWarning("[DraftStallGuard] RoleDraft.receivePick not found."); return; }

            var picked = _alreadyPicked?.GetValue(null) as List<byte> ?? new List<byte>();
            byte role = (byte)TheOtherRoles.RoleId.Crewmate;
            if (player.Data.Role != null && player.Data.Role.IsImpostor)
            {
                role = (byte)TheOtherRoles.RoleId.Impostor;
                if (picked.Contains(role))
                {
                    var free = TheOtherRoles.RoleInfo.allRoleInfos.FirstOrDefault(r => r != null && r.isImpostor && !r.isModifier
                        && r.roleId != TheOtherRoles.RoleId.Impostor && !picked.Contains((byte)r.roleId));
                    if (free != null) role = (byte)free.roleId;
                }
            }

            var writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId,
                _pickRpc, SendOption.Reliable, -1);
            writer.Write(player.PlayerId);
            writer.Write(role);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
            _receivePick.Invoke(null, new object[] { player.PlayerId, role });

            HostFixPlugin.Logger.LogWarning($"[DraftStallGuard] {player.Data.PlayerName} (id {player.PlayerId}) did not pick for "
                + $"{_since:F0}s - host picked role {(TheOtherRoles.RoleId)role} for them so the draft can go on.");
        }
        catch (Exception ex)
        {
            HostFixPlugin.Logger.LogWarning($"[DraftStallGuard] stand-in pick failed: {ex.Message}");
        }
    }
}

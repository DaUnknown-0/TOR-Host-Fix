// TOR - Host Tools - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.

/*
 * GuesserForceGuard (20.11.2026, Fehlerbericht "[Fix2] assignGuesserGamemode crashed: Index was out of range").
 *
 * TOR RoleManagerSelectRolesPatch.assignGuesserGamemodeToPlayers: mit "Force Jackal/Thief Guesser" sucht TOR den Jackal bzw.
 * Thief per FindIndex in der Neutralen-Liste. Gibt es den Spieler, steht er aber nicht in der Liste (Helpers.isNeutral liefert
 * über die erste RoleInfo false, z. B. weil eine Erweiterung eine andere Rolle davor einträgt, oder er ist nicht mehr da),
 * wird der Index -1 und playerList[-1] wirft. Fix2 fängt das ab, dann bekommt aber in dieser Runde niemand den Guesser.
 *
 * Der Prefix schaltet die Zwangswahl in genau diesem Fall ab (die Neutralen bekommen ihre Guesser dann zufällig, wie ohne die
 * Option) und loggt einmal, wer betroffen war, damit die Ursache sichtbar wird. Nur Host, der TOR-Quelltext bleibt unverändert.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace HostFixPlugin;

internal static class GuesserForceGuard
{
    private static FieldInfo jackalField, thiefField;

    internal static void Init(Harmony harmony, Assembly tor)
    {
        try
        {
            var patchType = tor.GetType("TheOtherRoles.Patches.RoleManagerSelectRolesPatch");
            var method = patchType?.GetMethod("assignGuesserGamemodeToPlayers", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            jackalField = tor.GetType("TheOtherRoles.TheOtherRoles+Jackal")?.GetField("jackal", BindingFlags.Public | BindingFlags.Static);
            thiefField = tor.GetType("TheOtherRoles.TheOtherRoles+Thief")?.GetField("thief", BindingFlags.Public | BindingFlags.Static);
            if (method == null || jackalField == null || thiefField == null)
            {
                HostFixPlugin.Logger.LogWarning("[GuesserForceGuard] TOR members not found - guard skipped.");
                return;
            }
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(GuesserForceGuard), nameof(Prefix)));
            HostFixPlugin.Logger.LogInfo("[GuesserForceGuard] patched assignGuesserGamemodeToPlayers.");
        }
        catch (Exception ex)
        {
            HostFixPlugin.Logger.LogWarning($"[GuesserForceGuard] patch failed: {ex.Message}");
        }
    }

    private static void Prefix(List<PlayerControl> playerList, ref bool forceJackal, ref bool forceThief)
    {
        try
        {
            if (playerList == null) return;
            if (forceJackal && Missing(jackalField, playerList, "Jackal")) forceJackal = false;
            if (forceThief && Missing(thiefField, playerList, "Thief")) forceThief = false;
        }
        catch (Exception ex)
        {
            HostFixPlugin.Logger.LogWarning($"[GuesserForceGuard] check failed: {ex.Message}");
        }
    }

    // true = Spieler existiert, steht aber nicht in der Liste (TOR würde mit Index -1 abstürzen)
    private static bool Missing(FieldInfo field, List<PlayerControl> list, string role)
    {
        var p = field.GetValue(null) as PlayerControl;
        if (p == null) return false;
        if (list.Any(x => x != null && x.PlayerId == p.PlayerId)) return false;
        string first = "?";
        try { first = TheOtherRoles.RoleInfo.getRoleInfoForPlayer(p, false).FirstOrDefault()?.name ?? "none"; } catch { }
        HostFixPlugin.Logger.LogWarning($"[GuesserForceGuard] forced {role} guesser skipped: {role} (player {p.PlayerId}, "
            + $"disconnected={p.Data?.Disconnected}, first role info '{first}') is not in TOR's neutral list.");
        return true;
    }
}

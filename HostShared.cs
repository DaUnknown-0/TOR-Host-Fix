// TOR Host Tools - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.

/*
 * HostShared - what Host Tools used to take from TOR - Forgotten Fixes, now its own (User 2026-11-13: "Host Tools has to
 * work without UC/UTS as well"). No compile-time reference to Forgotten Fixes any more: where it is installed, its
 * pieces are used through reflection (one sync queue, one lobby menu, its translations, its connection measuring);
 * where it is not, these stand in:
 *
 *   LobbyScreen        the lobby screen exists (DestroyableSingleton backing field, never constructs one)
 *   Uts                is Forgotten Fixes loaded, call into it
 *   PacedOptionSync    host: send changed options; Forgotten Fixes' paced queue, else TOR's own full sync
 *   LobbyMenu          an entry in Forgotten Fixes' lobby menu (AppDomain "UTS.LobbyMenu.Add"), else in Host Tools'
 *                      own corner menu (HostMenuUI)
 *   PlayerIdentity     the player key in Forgotten Fixes' format (puid:..., friend code, name:...), so notes and the
 *                      block list match with and without it
 *   ConnectionLight    reads Forgotten Fixes' connection measuring; without it there is nothing to read (every client
 *                      has to answer the host's ping, and only that mod answers)
 *   OptionTitle        a translated option title through Forgotten Fixes' localization when it is there
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TheOtherRoles;
using UnityEngine;

namespace HostFixPlugin;

internal static class LobbyScreen
{
    internal static bool Exists
    {
        get
        {
            try { if (AppDomain.CurrentDomain.GetData("UTS.UIGallery.Active") is bool g && g) return true; } catch { }
            try { return DestroyableSingleton<GameStartManager>.InstanceExists; }
            catch { return false; }
        }
    }

    internal static GameStartManager InstanceOrNull()
    {
        try { return Exists ? DestroyableSingleton<GameStartManager>._instance : null; }
        catch { return null; }
    }
}

/// <summary>TOR - Forgotten Fixes, if it is loaded (reflection only).</summary>
internal static class Uts
{
    private static Assembly asm;
    private static bool looked;

    internal static Assembly Assembly
    {
        get
        {
            if (looked) return asm;
            looked = true;
            asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "UsefulTORStuff");
            return asm;
        }
    }

    internal static bool Loaded => Assembly != null;

    internal static Type Type(string name) => Assembly?.GetType("UsefulTORStuff." + name);

    internal static object Call(string type, string method, params object[] args)
    {
        var t = Type(type);
        var m = t?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                  .FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
        return m?.Invoke(null, args);
    }

    /// <summary>Has this client answered Forgotten Fixes' version handshake? (true without Forgotten Fixes: nothing to say)</summary>
    internal static bool HasMod(int clientId)
    {
        try
        {
            if (!Loaded) return true;
            var f = Type("UsefulVersionHandshake")?.GetField("playerVersions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return f?.GetValue(null) is not IDictionary d || d.Contains(clientId);
        }
        catch { return true; }
    }

    /// <summary>Forgotten Fixes' mismatch text of the version handshake, "" without it.</summary>
    internal static string VersionMismatch()
    {
        try { return Loaded ? Call("UsefulVersionHandshake", "BuildMismatchMessage") as string ?? "" : ""; }
        catch { return ""; }
    }

    /// <summary>Closes one of Forgotten Fixes' panels (static Instance + Close()), if it is there.</summary>
    internal static void ClosePanel(string type)
    {
        try
        {
            var t = Type(type);
            var inst = t?.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            if (inst != null) t.GetMethod("Close", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, System.Type.EmptyTypes, null)?.Invoke(inst, null);
        }
        catch { }
    }
}

internal static class PacedOptionSync
{
    /// <summary>Host: send these options. Forgotten Fixes' paced queue when installed, else TOR's full sync.</summary>
    internal static int SendPaced(IEnumerable<CustomOption> options)
    {
        var list = options?.Where(o => o != null && o.id != 0).Distinct().ToList() ?? new List<CustomOption>();
        if (list.Count == 0) return 0;
        try
        {
            if (Uts.Loaded)
            {
                var r = Uts.Call("PacedOptionSync", "SendPaced", (IEnumerable<CustomOption>)list);
                if (r is int n) return n;
            }
        }
        catch (Exception e) { HostFixPlugin.Logger?.LogWarning($"[HostShared] Forgotten Fixes sync failed, TOR's own one instead: {e.Message}"); }
        try { CustomOption.ShareOptionSelections(); } catch { }
        return 1;
    }
}

internal static class PlayerIdentity
{
    internal const string PuidPrefix = "puid:", NamePrefix = "name:";

    internal static string KeyOf(PlayerControl p)
    {
        try
        {
            if (Uts.Loaded && Uts.Call("PlayerIdentity", "KeyOf", p) is string k) return k;
        }
        catch { }
        string puid = null, code = null, name = null;
        try { puid = p?.Data?.Puid; } catch { }
        try { code = p?.Data?.FriendCode; } catch { }
        try { name = p?.Data?.PlayerName; } catch { }
        if (!string.IsNullOrEmpty(puid)) return PuidPrefix + NoControl(puid);
        if (!string.IsNullOrEmpty(code)) return NoControl(code);
        return string.IsNullOrEmpty(name) ? "" : NamePrefix + NoControl(name);
    }

    private static string NoControl(string s) => new string((s ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}

internal static class OptionTitle
{
    /// <summary>The option's title through Forgotten Fixes' localization (keeps up with language changes); else unchanged.</summary>
    internal static void Bind(CustomOption option, string utsKey)
    {
        try { if (Uts.Loaded) Uts.Call("UTSLocalization", "BindOptionTitle", option, utsKey); } catch { }
    }
}

/// <summary>Forgotten Fixes' connection measuring, read through reflection.</summary>
internal static class ConnectionLight
{
    internal enum Level { Grey, Green, Yellow, Red }

    internal sealed class Row
    {
        public string Name = "?";
        public Level Level;
        public int Median = -1, Worst = -1, Lost, Asked, Drops;
    }

    /// <summary>Forgotten Fixes measures (it is installed).</summary>
    internal static bool Available => Uts.Type("ConnectionLight") != null;

    internal static List<Row> Rows()
    {
        var list = new List<Row>();
        try
        {
            if (Uts.Call("ConnectionLight", "Rows") is not IEnumerable rows) return list;
            foreach (var r in rows)
            {
                var t = r.GetType();
                object F(string n) => t.GetField(n)?.GetValue(r);
                list.Add(new Row
                {
                    Name = F("Name") as string ?? "?",
                    Level = Enum.TryParse(F("Level")?.ToString(), out Level l) ? l : Level.Grey,
                    Median = F("Median") is int m ? m : -1, Worst = F("Worst") is int w ? w : -1,
                    Lost = F("Lost") is int lo ? lo : 0, Asked = F("Asked") is int a ? a : 0, Drops = F("Drops") is int d ? d : 0,
                });
            }
        }
        catch { }
        return list;
    }

    internal static Color ColorOf(Level l) =>
        l == Level.Red ? VanillaUI.Red : l == Level.Yellow ? VanillaUI.Yellow : l == Level.Green ? VanillaUI.Green : VanillaUI.Grey;
}

/// <summary>
/// Lobby menu entries: handed to Forgotten Fixes' lobby menu when it is installed (AppDomain "UTS.LobbyMenu.Add"),
/// else shown in Host Tools' own corner menu (HostMenuUI).
/// </summary>
internal static class LobbyMenu
{
    private const string AddKey = "UTS.LobbyMenu.Add";

    internal sealed class Entry
    {
        public string Id;
        public int Order;
        public Func<bool> Visible;
        public Func<string> Label;
        public Action Click;
        public Color Color;
        public bool Handed;
    }

    internal static readonly List<Entry> Entries = new();

    internal static void Add(string id, int order, Func<bool> visible, Func<string> label, Action click, Color color)
    {
        Entries.RemoveAll(e => e.Id == id);
        Entries.Add(new Entry { Id = id, Order = order, Visible = visible, Label = label, Click = click, Color = color });
        HandOver();
    }

    /// <summary>Forgotten Fixes' menu is there and has every entry: our own corner menu stays away.</summary>
    internal static bool UtsMenu { get; private set; }

    /// <summary>Hands the entries to Forgotten Fixes' menu once it published its contract (it may load after us).</summary>
    internal static void HandOver()
    {
        if (AppDomain.CurrentDomain.GetData(AddKey) is not Action<string, int, Func<bool>, Func<string>, Action, Color> add) return;
        foreach (var e in Entries.Where(e => !e.Handed))
        {
            try { add(e.Id, e.Order, e.Visible, e.Label, e.Click, e.Color); e.Handed = true; }
            catch (Exception ex) { HostFixPlugin.Logger?.LogWarning($"[HostShared] lobby menu entry {e.Id}: {ex.Message}"); e.Handed = true; }
        }
        UtsMenu = true;
    }
}

// TOR Host Fix - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Based on The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

/*
 * HFLocalization - HostFix side of the shared localization system.
 *
 * Same duplicate-the-helper convention as VersionDisplay: HostFix ships its own tiny
 * string tables (hostfix.* keys, embedded as
 * "HostFixPlugin.Resources.Localization.<code>.json") plus a copy of the flat-map JSON
 * loader, and follows the language UsefulTORStuff publishes via AppDomain
 * ("UTS.Loc.ActiveCode" / "UTS.Loc.Epoch"). Without UTS it falls back to the vanilla
 * language setting. Change detection: throttled poll in HudManager.Update +
 * MainMenuManager.Start (attribute patches, picked up by PatchAll).
 *
 * HostFix has no CustomOptions and no RoleInfos - only Tr() for the updater popups and
 * the PingTracker credit line (rendered per frame, so it follows a language switch
 * automatically). Community overrides: hostfix.* keys in
 * BepInEx/config/UTSLocalization/<code>.json (the same file UTS/UC read).
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;

namespace HostFixPlugin {
    public static class HFLocalization {
        private const string ActiveCodeKey = "UTS.Loc.ActiveCode";
        private const string EpochKey = "UTS.Loc.Epoch";

        private static readonly string[] KnownCodes = {
            "en", "latam", "brazilian", "portuguese", "korean", "russian", "dutch",
            "filipino", "french", "german", "italian", "japanese", "spanish",
            "schinese", "tchinese", "irish",
            "tr", "pl", "cs", "hu", "ro", "sv", "fi", "uk", "id", "vi"
        };

        private static readonly Dictionary<string, string> english = new();
        private static readonly Dictionary<string, string> active = new();
        public static string ActiveCode { get; private set; } = "en";
        public static event Action LanguageApplied;
        private static int lastEpoch = -1;
        private static float nextPoll;

        public static void Initialize() {
            LoadTable("en", english);
            Apply("initial load");
        }

        public static string Tr(string key) {
            if (active.TryGetValue(key, out var t) && t.Length > 0) return t;
            if (english.TryGetValue(key, out var e) && e.Length > 0) return e;
            return key;
        }

        public static string Tr(string key, params object[] args) {
            var t = Tr(key);
            try { return string.Format(t, args); }
            catch (FormatException) { return t; }
        }

        private static void Apply(string reason) {
            var code = ResolveCode();
            LoadTable(code, active);
            ActiveCode = code;
            try { LanguageApplied?.Invoke(); } catch { }
            HostFixPlugin.Logger?.LogInfo($"[HFLoc] language \"{code}\" applied ({active.Count} keys, {reason})");
        }

        private static string ResolveCode() {
            try {
                if (AppDomain.CurrentDomain.GetData(ActiveCodeKey) is string s
                    && Array.IndexOf(KnownCodes, s) >= 0) return s;
            } catch { }
            try {
                var code = AmongUs.Data.DataManager.Settings.Language.CurrentLanguage
                    .ToString().ToLowerInvariant();
                if (code == "english") return "en";
                return Array.IndexOf(KnownCodes, code) >= 0 ? code : "en";
            } catch { return "en"; }
        }

        [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
        private static class PollPatch {
            public static void Postfix() {
                if (UnityEngine.Time.unscaledTime < nextPoll) return;
                nextPoll = UnityEngine.Time.unscaledTime + 0.5f;
                CheckForChange();
            }
        }

        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
        private static class MenuPatch {
            public static void Postfix() => CheckForChange();
        }

        private static void CheckForChange() {
            try {
                int epoch = AppDomain.CurrentDomain.GetData(EpochKey) is int i ? i : -1;
                var code = ResolveCode();
                if (epoch == lastEpoch && code == ActiveCode) return;
                string reason = code != ActiveCode ? "language change" : "epoch change";
                lastEpoch = epoch;
                Apply(reason);
            } catch { }
        }

        // ---------- table loading (same minimal flat-map JSON as UTSLocalization) ----------

        private static void LoadTable(string code, Dictionary<string, string> into) {
            into.Clear();
            try {
                var asm = Assembly.GetExecutingAssembly();
                using var s = asm.GetManifestResourceStream(
                    $"HostFixPlugin.Resources.Localization.{code}.json");
                if (s != null) {
                    using var r = new StreamReader(s, Encoding.UTF8);
                    ParseFlatJson(r.ReadToEnd(), into);
                }
            } catch (Exception e) {
                HostFixPlugin.Logger?.LogWarning($"[HFLoc] embedded table {code} failed: {e.Message}");
            }
            try {
                var path = Path.Combine(Paths.ConfigPath, "UTSLocalization", code + ".json");
                if (File.Exists(path)) {
                    var all = new Dictionary<string, string>();
                    ParseFlatJson(File.ReadAllText(path, Encoding.UTF8), all);
                    foreach (var kv in all)
                        if (kv.Key.StartsWith("hostfix.", StringComparison.Ordinal)) into[kv.Key] = kv.Value;
                }
            } catch (Exception e) {
                HostFixPlugin.Logger?.LogWarning($"[HFLoc] override table {code} failed: {e.Message}");
            }
        }

        private static void ParseFlatJson(string json, Dictionary<string, string> into) {
            int i = 0, n = json.Length;
            void SkipWs() { while (i < n && (json[i] == ' ' || json[i] == '\t' || json[i] == '\r' || json[i] == '\n')) i++; }
            string ParseString() {
                var sb = new StringBuilder();
                i++;
                while (i < n) {
                    char c = json[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= n) break;
                    char e = json[i++];
                    switch (e) {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 <= n && ushort.TryParse(json.Substring(i, 4),
                                    System.Globalization.NumberStyles.HexNumber, null, out var cp)) {
                                sb.Append((char)cp);
                                i += 4;
                            }
                            break;
                    }
                }
                return sb.ToString();
            }
            if (n > 0 && json[0] == '﻿') i = 1;
            SkipWs();
            if (i >= n || json[i] != '{') return;
            i++;
            while (true) {
                SkipWs();
                if (i >= n) return;
                if (json[i] == '}') return;
                if (json[i] == ',') { i++; continue; }
                if (json[i] != '"') return;
                var key = ParseString();
                SkipWs();
                if (i >= n || json[i] != ':') return;
                i++;
                SkipWs();
                if (i < n && json[i] == '"') {
                    into[key] = ParseString();
                } else {
                    int depth = 0;
                    while (i < n) {
                        char c = json[i];
                        if (c == '{' || c == '[') depth++;
                        else if (c == '}' || c == ']') { if (depth == 0) break; depth--; }
                        else if (c == ',' && depth == 0) break;
                        i++;
                    }
                }
            }
        }
    }
}

// MUTED PLAYERS (TASKS.md E46; native 43397af main.cpp keptMute, refreshMutes, toggleMute, loadMutes, saveMutes, 0.3.23;
// the owner, 2026-11-20: "block player features aren't necessary if your player mute can be persistent and room ban is per
// instance so that's enough"): every REPORT PLAYER > MUTE is kept on this headset (mutes.cfg) and holds whenever you meet
// that person again: by the community server's code for them when both are known (a new name doesn't undo it), else by
// their name. SETTINGS > VOICE CHAT > MUTED PLAYERS unmutes. Nothing is sent but the codes asked for (Net.AskMuteCodes)
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ProjectArena
{
    public class Mutes
    {
        public class Entry { public ulong code; public string name = ""; }
        public readonly List<Entry> list = new List<Entry>();
        readonly List<uint> now = new List<uint>();   // the people here now that a kept mute covers (worked out twice a second)
        double nowAt = -1;
        string folder;

        public bool NowContains(uint id) => now.Contains(id);

        // whether a kept mute covers this person (the server's code for them, when both are known; else their name), and
        // its place in the list (-1: none)
        public int Kept(ulong code, string name)
        {
            for (int i = 0; i < list.Count; ++i)
            {
                Entry m = list[i];
                if (code != 0 && m.code != 0 ? m.code == code : m.name == name) return i;
            }
            return -1;
        }

        // mutes.cfg: "mute=CODE NAME" a line (CODE 16 hex digits, 0 when muted by name only)
        public void Load(string dir)
        {
            folder = dir;
            list.Clear();
            string path = Path.Combine(dir ?? "", "mutes.cfg");
            try
            {
                if (!File.Exists(path)) return;
                foreach (string raw in File.ReadAllLines(path))
                {
                    if (list.Count >= 200) break;
                    string ln = raw.TrimEnd('\r');
                    if (!ln.StartsWith("mute=", StringComparison.Ordinal) || ln.Length < 6) continue;
                    string rest = ln.Substring(5);
                    int sp = rest.IndexOf(' ');
                    string hex = sp < 0 ? rest : rest.Substring(0, sp);
                    if (hex.Length > 16) hex = hex.Substring(0, 16);
                    if (!ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out ulong code)) continue;
                    string name = sp < 0 ? "" : rest.Substring(sp + 1);
                    if (name.Length > 16) name = name.Substring(0, 16);
                    if (code != 0 || name.Length > 0) list.Add(new Entry { code = code, name = name });
                }
                Debug.Log($"[Voice] {list.Count} players muted (kept)");
            }
            catch (Exception e) { Debug.LogWarning("[Voice] could not read mutes.cfg: " + e.Message); }
        }

        public void Save()
        {
            if (folder == null) return;
            try
            {
                Directory.CreateDirectory(folder);
                var t = new System.Text.StringBuilder();
                foreach (Entry m in list) t.Append("mute=").Append(m.code.ToString("x16")).Append(' ').Append(m.name).Append('\n');
                File.WriteAllText(Path.Combine(folder, "mutes.cfg"), t.ToString());
            }
            catch (Exception e) { Debug.LogWarning("[Voice] could not save mutes.cfg: " + e.Message); }
        }

        // twice a second: who here a kept mute covers; the server asked for everyone's code; a mute made by name takes the
        // code once it is known, and the list keeps the person's name as it is now
        public void Refresh(Online.Net net, double t)
        {
            if (net == null || (t - nowAt < 0.5 && nowAt >= 0)) return;
            nowAt = t;
            now.Clear();
            var ids = new List<uint>();
            bool changed = false;
            foreach (Online.Net.Person p in net.PeopleHere())
            {
                ids.Add(p.id);
                ulong code = net.MuteCode(p.id);
                int k = Kept(code, p.name);
                if (k < 0) continue;
                now.Add(p.id);
                Entry m = list[k];
                if (code != 0 && m.code == 0) { m.code = code; changed = true; }
                if (code != 0 && m.name != p.name) { m.name = p.name; changed = true; }
            }
            net.AskMuteCodes(ids);
            if (changed) Save();
        }

        // MUTE / UNMUTE on REPORT PLAYER: for now (this session) and kept
        public void Toggle(uint id, string name, Online.Net net, List<uint> mutedIds)
        {
            if (id == 0) return;
            ulong code = net != null ? net.MuteCode(id) : 0;
            if (mutedIds.Contains(id) || now.Contains(id))
            {
                mutedIds.RemoveAll(x => x == id);
                now.RemoveAll(x => x == id);
                for (int k; (k = Kept(code, name)) >= 0;) list.RemoveAt(k);
            }
            else
            {
                mutedIds.Add(id);
                if (Kept(code, name) < 0) list.Add(new Entry { code = code, name = name ?? "" });
            }
            Save();
        }

        // MUTED PLAYERS > UNMUTE: the kept mute goes, and whoever here it covered is heard again
        public void Unmute(int i, Online.Net net, List<uint> mutedIds)
        {
            if (i < 0 || i >= list.Count) return;
            Debug.Log($"[Voice] {list[i].name} unmuted (MUTED PLAYERS)");
            list.RemoveAt(i);
            Save();
            var keep = new List<uint>();
            if (net != null)
                foreach (Online.Net.Person p in net.PeopleHere())
                    if (mutedIds.Contains(p.id) && Kept(net.MuteCode(p.id), p.name) >= 0) keep.Add(p.id);
            mutedIds.Clear();
            mutedIds.AddRange(keep);
            now.Clear();
            nowAt = -1;
        }
    }
}

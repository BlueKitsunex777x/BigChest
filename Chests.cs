using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Blocksters.MathLib;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using BlockStoryCore;
using UnityEngine;

namespace BlockStoryMod
{
    public class Chest
    {
        public int X, Y, Z;
        public int Face;
        public Chest Mate;
        public GameObject View;
        public Transform Lid;
        public float LidAngle;
        // world writes go through a queue, right after one the block still reads as whatever was there before
        public float Settle;
        // online: when the block was first seen as something else, a chunk can be mid-update so it gets a moment
        public float GoneSince;
        // online: placed here a moment ago, the server's list may not have it yet
        public float Fresh;

        public Vector3i Pos => new Vector3i(X, Y, Z);
        // a pair is drawn once, by whichever half has the lower position
        public bool Draws => Mate == null || X < Mate.X || (X == Mate.X && Z < Mate.Z);
    }

    // the block is a plain plank, a new block number would move whenever the game adds blocks and wreck saved worlds.
    // this list is what makes it a chest. contents sit in the game's own chest file, one file per half
    public static class Chests
    {
        public const string Item = "Big Chest", Host = "Plank";
        public const int Cols = 8, Rows = 8, Slots = Cols * Rows;

        public static bool Enabled = PlayerPrefs.GetInt("BigChest_Enabled", 1) != 0;
        public static void SetEnabled(bool on) { Enabled = on; PlayerPrefs.SetInt("BigChest_Enabled", on ? 1 : 0); PlayerPrefs.Save(); L.Info("chest enabled=" + on); }

        public static readonly Vector3i[] Dirs = { new Vector3i(0, 0, 1), new Vector3i(1, 0, 0), new Vector3i(0, 0, -1), new Vector3i(-1, 0, 0) };

        public static readonly List<Chest> All = new List<Chest>();
        private static readonly Dictionary<long, Chest> _at = new Dictionary<long, Chest>();
        private static string _world = "";
        private static bool _dirty;
        private static float _nextSave;

        // online it needs a Block Story Server (our app) to keep the chests, the game's server doesn't know about them
        public static bool Usable => Enabled && BSWorld.InWorld && !BSWorld.InRealm && (!BSWorld.Online || BSServerStore.Ready);
        public static bool Online => BSWorld.Online;

        private static long Key(int x, int y, int z) => ((long)(x + 1048576) << 42) | ((long)(y + 1048576) << 21) | (long)(z + 1048576);
        public static Chest At(int x, int y, int z) => _at.TryGetValue(Key(x, y, z), out Chest c) ? c : null;

        public static void Add(Vector3i p)
        {
            var c = new Chest { X = p.X, Y = p.Y, Z = p.Z, Face = FaceFromLook(-1), Settle = Time.time + 2f, Fresh = Time.time + 10f };
            _at[Key(p.X, p.Y, p.Z)] = c;
            All.Add(c);
            FindMate(c);
            _dirty = true;
            if (Online) { Publish(c); if (c.Mate != null) Publish(c.Mate); }
            L.Info("chest placed at " + p.X + "," + p.Y + "," + p.Z + (c.Mate != null ? " joined a neighbour" : ""));
        }

        private static void Remove(Chest c)
        {
            All.Remove(c);
            _at.Remove(Key(c.X, c.Y, c.Z));
            ChestView.Kill(c);
            Chest mate = c.Mate;
            c.Mate = null;
            if (mate != null)
            {
                mate.Mate = null;
                ChestView.Kill(mate);
                // the half left behind is a single chest again and can pair with another neighbour, same as minecraft
                FindMate(mate);
                if (Online) { Publish(mate); if (mate.Mate != null) Publish(mate.Mate); }
            }
            _dirty = true;
            if (Online) BSServerStore.Delete(Mod, "at:" + KeyOf(c));
            L.Info("chest removed at " + c.X + "," + c.Y + "," + c.Z);
        }

        private static void FindMate(Chest c)
        {
            foreach (Vector3i d in Dirs)
            {
                Chest n = At(c.X + d.X, c.Y, c.Z + d.Z);
                if (n == null || n.Mate != null || n == c) continue;
                Join(c, n, d);
                return;
            }
        }

        // the front has to run along the long side of the pair
        private static void Join(Chest placed, Chest other, Vector3i d)
        {
            int axis = Array.IndexOf(Dirs, d);
            bool Sideways(int f) => (f - axis) % 2 != 0;
            int face = Sideways(other.Face) ? other.Face : Sideways(placed.Face) ? placed.Face : FaceFromLook(axis);
            placed.Face = other.Face = face;
            placed.Mate = other;
            other.Mate = placed;
            ChestView.Kill(placed);
            ChestView.Kill(other);
        }

        // front looks back at you. with avoid set, only the two directions that aren't along that axis count
        private static int FaceFromLook(int avoid)
        {
            Camera cam = Camera.main;
            if (cam == null) return avoid >= 0 ? (avoid + 1) % 4 : 0;
            Vector3 v = -cam.transform.forward;
            int best = 0;
            float top = -2f;
            for (int i = 0; i < Dirs.Length; i++)
            {
                if (avoid >= 0 && (i - avoid) % 2 == 0) continue;
                float dot = v.x * Dirs[i].X + v.z * Dirs[i].Z;
                if (dot > top) { top = dot; best = i; }
            }
            return best;
        }

        // the half on your left as you look at the front, so the left half of the screen is the left half of the chest
        public static Chest LeftOf(Chest c)
        {
            if (c.Mate == null) return c;
            Vector3i f = Dirs[c.Face];
            // turning the front direction a quarter gives the viewer's left
            int leftX = f.Z, leftZ = -f.X;
            int a = c.X * leftX + c.Z * leftZ, b = c.Mate.X * leftX + c.Mate.Z * leftZ;
            return a > b ? c : c.Mate;
        }

        // ---------- what's inside ----------

        public static bool IsBlank(LootItem i) => i.count <= 0 || string.IsNullOrEmpty(i.name);

        // every slot in order, gaps included, so an item stays in the slot you put it in
        public static List<LootItem> Read(Chest c)
        {
            List<LootItem> list = Online
                ? (_contents.TryGetValue(KeyOf(c), out List<LootItem> got) ? new List<LootItem>(got) : new List<LootItem>())
                : BSWorld.World()?.GetChestContent(c.Pos) ?? new List<LootItem>();
            for (int i = 0; i < list.Count; i++) if (IsBlank(list[i])) list[i] = new LootItem { name = "" };
            return list;
        }

        public static void Write(Chest c, List<LootItem> items)
        {
            while (items.Count > 0 && IsBlank(items[items.Count - 1])) items.RemoveAt(items.Count - 1);
            if (Online)
            {
                string k = KeyOf(c);
                if (!_held.Contains(k)) { L.Warn("write to a chest this player doesn't hold, skipped"); return; }
                _contents[k] = new List<LootItem>(items);
                BSServerStore.Put(Mod, "in:" + k, Pack(items), r => { if (r.Busy) L.Warn("chest not saved, " + r.Holder + " has it"); });
                return;
            }
            BSWorld.World()?.SetChestContent(c.Pos, items);
        }

        // gems go on your character not in a bag slot, the game won't keep them in a chest either
        public static bool Refuses(string name) => name == "Diamond" || name == "Cheat Diamond";

        // a broken half only spills its own side, the other half keeps what it holds
        public static void Spill(Chest c)
        {
            InventoryCollector col = TerrainLoader.instance?.digger?.inventoryCollector;
            if (col == null) return;
            var at = new Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f);
            if (Online)
            {
                // every player nearby sees the chest go, Take makes sure only one of them drops what was inside
                string k = KeyOf(c);
                _contents.Remove(k);
                BSServerStore.Take(Mod, "in:" + k, r =>
                {
                    if (!r.Ok || r.Value == null) return;
                    int dropped = Drop(col, at, Unpack(r.Value));
                    if (dropped > 0) L.Info("chest spilled " + dropped + " stacks");
                });
                return;
            }
            int n = 0;
            foreach (LootItem it in Read(c))
            {
                if (IsBlank(it) || InvDatabase.FindByName(it.name) == null) continue;
                col.DropLoot(at, it.name, it.data, it.count, true, 1f, it.paintData);
                n++;
            }
            BSWorld.World()?.DeleteChest(c.Pos);
            if (n > 0) L.Info("chest spilled " + n + " stacks");
        }

        // ---------- ticking ----------

        public static void Tick()
        {
            Sync();
            if (_world.Length == 0) return;
            if (Online) TickOnline();
            else if (_dirty && Time.unscaledTime >= _nextSave) { _dirty = false; _nextSave = Time.unscaledTime + 2f; Save(); }

            // something other than a pick took the block away, tnt or a shovel. not while the area isn't loaded though
            for (int i = All.Count - 1; i >= 0; i--)
            {
                Chest c = All[i];
                if (Time.time < c.Settle) continue;
                Block? b = BSWorld.GetBlock(c.X, c.Y, c.Z);
                if (b == null || b.Value.type == null || b.Value.id == BlockTypes.Unknown.id) continue;
                if (b.Value.type.name == Host) { c.GoneSince = 0f; continue; }
                if (Online)
                {
                    // only judged up close, where the chunk is surely loaded, and only once it's stayed gone a few seconds
                    Vector3? near = BSWorld.PlayerPos();
                    if (near == null || (near.Value - new Vector3(c.X, c.Y, c.Z)).sqrMagnitude > 48f * 48f) { c.GoneSince = 0f; continue; }
                    if (c.GoneSince == 0f) { c.GoneSince = Time.time; continue; }
                    if (Time.time - c.GoneSince < 3f) continue;
                }
                Spill(c);
                Remove(c);
            }

            Vector3? me = BSWorld.PlayerPos();
            if (me == null) return;
            foreach (Chest c in All)
            {
                if (!c.Draws) continue;
                if (c.View == null) c.View = ChestView.Build(c);
            }
            ChestView.Shade(All, me.Value);
            ChestView.Animate(All);
        }

        private static void Sync()
        {
            string k = BSWorld.SaveKey();
            if (k == _world) return;
            if (_dirty && _world.Length > 0) Save();
            _dirty = false;
            foreach (Chest c in All) ChestView.Kill(c);
            All.Clear();
            _at.Clear();
            _world = k;
            _contents.Clear();
            _held.Clear();
            _nextList = 0f;
            if (k.Length > 0 && !Online) Load();
            L.Info("world '" + k + "' chests " + All.Count);
        }

        // ---------- online, on a Block Story Server ----------

        // the server app keeps "at:x,y,z" = where a chest is and which way it faces, and "in:x,y,z" = what that half holds.
        // a chest's contents can only be read and changed while this player has it claimed, one player per chest
        public const string Mod = "Big Chest";
        private const float ListEvery = 4f, Renew = 5f;
        private static readonly Dictionary<string, List<LootItem>> _contents = new Dictionary<string, List<LootItem>>();
        private static readonly HashSet<string> _held = new HashSet<string>();
        private static float _nextList, _nextRenew;
        private static bool _listing;

        public static string KeyOf(Chest c) => c.X + "," + c.Y + "," + c.Z;

        private static void Publish(Chest c)
        {
            string v = c.Face + "|" + (c.Mate != null ? c.Mate.X + "," + c.Mate.Z : "-");
            BSServerStore.Put(Mod, "at:" + KeyOf(c), v);
        }

        private static void TickOnline()
        {
            if (!BSServerStore.Ready) return;
            if (!_listing && Time.unscaledTime >= _nextList)
            {
                _listing = true;
                string world = _world;
                BSServerStore.List(Mod, "at:", r =>
                {
                    _listing = false;
                    _nextList = Time.unscaledTime + ListEvery;
                    if (r.Ok && r.List != null && world == _world) Match(r.List);
                });
            }
            if (_held.Count > 0 && Time.unscaledTime >= _nextRenew)
            {
                _nextRenew = Time.unscaledTime + Renew;
                foreach (string k in new List<string>(_held))
                    BSServerStore.Claim(Mod, "in:" + k, r =>
                    {
                        if (!r.Busy || !_held.Contains(k)) return;
                        // the claim ran out during a long freeze and someone else has it now, stop before anything gets overwritten
                        L.Warn("lost chest " + k + " to " + r.Holder);
                        ChestScreen.Close();
                    });
            }
        }

        // brings this PC's chest list in line with the server's, keeping the same objects so an open screen stays open
        private static void Match(Dictionary<string, string> list)
        {
            var seen = new HashSet<long>();
            var mates = new Dictionary<Chest, string>();
            foreach (var kv in list)
            {
                string[] xyz = kv.Key.Substring(3).Split(',');
                string[] v = (kv.Value ?? "").Split('|');
                if (xyz.Length != 3 || v.Length != 2 || !int.TryParse(xyz[0], out int x) || !int.TryParse(xyz[1], out int y) || !int.TryParse(xyz[2], out int z) || !int.TryParse(v[0], out int face)) continue;
                seen.Add(Key(x, y, z));
                Chest c = At(x, y, z);
                if (c == null)
                {
                    c = new Chest { X = x, Y = y, Z = z, Face = face, Settle = Time.time + 2f };
                    _at[Key(x, y, z)] = c;
                    All.Add(c);
                }
                else if (c.Face != face) { c.Face = face; ChestView.Kill(c); if (c.Mate != null) ChestView.Kill(c.Mate); }
                mates[c] = v[1];
            }
            for (int i = All.Count - 1; i >= 0; i--)
            {
                Chest c = All[i];
                if (seen.Contains(Key(c.X, c.Y, c.Z)) || Time.time < c.Fresh) continue;
                // gone on the server: someone else broke it, they did the spilling
                All.RemoveAt(i);
                _at.Remove(Key(c.X, c.Y, c.Z));
                ChestView.Kill(c);
                if (c.Mate != null) { c.Mate.Mate = null; ChestView.Kill(c.Mate); }
                c.Mate = null;
            }
            foreach (var kv in mates)
            {
                Chest c = kv.Key, want = null;
                if (Time.time < c.Fresh || (c.Mate != null && Time.time < c.Mate.Fresh)) continue;
                string[] m = kv.Value.Split(',');
                if (m.Length == 2 && int.TryParse(m[0], out int mx) && int.TryParse(m[1], out int mz)) want = At(mx, c.Y, mz);
                if (c.Mate == want) continue;
                ChestView.Kill(c);
                if (c.Mate != null) ChestView.Kill(c.Mate);
                c.Mate = want;
            }
        }

        // asks for both halves, opens only if this player gets both
        public static void Claim(Chest c, Action<string> done)
        {
            Chest a = c, b = c.Mate;
            string ka = KeyOf(a), kb = b != null ? KeyOf(b) : null;
            BSServerStore.Claim(Mod, "in:" + ka, ra =>
            {
                if (!ra.Ok) { done(ra.Busy ? Holder(ra) + " is using this chest" : "The server didn't answer"); return; }
                _held.Add(ka);
                _contents[ka] = Unpack(ra.Value);
                if (kb == null) { done(null); return; }
                BSServerStore.Claim(Mod, "in:" + kb, rb =>
                {
                    if (!rb.Ok) { Let(ka); done(rb.Busy ? Holder(rb) + " is using this chest" : "The server didn't answer"); return; }
                    _held.Add(kb);
                    _contents[kb] = Unpack(rb.Value);
                    done(null);
                });
            });
        }

        public static void LetAll()
        {
            foreach (string k in new List<string>(_held)) Let(k);
        }

        private static void Let(string k)
        {
            _held.Remove(k);
            BSServerStore.Release(Mod, "in:" + k);
        }

        private static string Holder(BSStoreReply r) => r.Holder.Length > 0 ? r.Holder : "Someone";

        // one line per slot, blank line for an empty one, so items stay in the slot they were put in
        private static string Pack(List<LootItem> items)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                LootItem it = items[i];
                if (!IsBlank(it)) sb.Append(it.name).Append('\t').Append(it.data).Append('\t').Append(it.paintData).Append('\t').Append(it.count);
            }
            return sb.ToString();
        }

        private static List<LootItem> Unpack(string text)
        {
            var list = new List<LootItem>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (string line in text.Split('\n'))
            {
                string[] p = line.Split('\t');
                if (p.Length == 4 && ushort.TryParse(p[1], out ushort data) && ushort.TryParse(p[2], out ushort paint) && int.TryParse(p[3], out int count))
                    list.Add(new LootItem { name = p[0], data = data, paintData = paint, count = count });
                else list.Add(new LootItem { name = "" });
            }
            return list;
        }

        private static int Drop(InventoryCollector col, Vector3 at, List<LootItem> items)
        {
            int n = 0;
            foreach (LootItem it in items)
            {
                if (IsBlank(it) || Refuses(it.name) || InvDatabase.FindByName(it.name) == null) continue;
                col.DropLoot(at, it.name, it.data, it.count, true, 1f, it.paintData);
                n++;
            }
            return n;
        }

        // ---------- save file ----------

        private static string FilePath()
        {
            string d = Path.Combine(Application.persistentDataPath, "bsmod_bigchests");
            if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            return Path.Combine(d, _world + ".bschest");
        }

        private static void Save()
        {
            if (_world.Length == 0 || Online) return;
            try
            {
                string p = FilePath();
                string tmp = p + ".tmp";
                using (var w = new BinaryWriter(new FileStream(tmp, FileMode.Create)))
                {
                    w.Write(1);
                    w.Write(All.Count);
                    foreach (Chest c in All)
                    {
                        w.Write(c.X); w.Write(c.Y); w.Write(c.Z); w.Write(c.Face);
                        w.Write(c.Mate != null);
                        w.Write(c.Mate?.X ?? 0); w.Write(c.Mate?.Z ?? 0);
                    }
                }
                if (File.Exists(p)) File.Delete(p);
                File.Move(tmp, p);
            }
            catch (Exception e) { L.Err("save", e); }
        }

        private static void Load()
        {
            string p = FilePath();
            if (!File.Exists(p)) return;
            try
            {
                var mates = new List<KeyValuePair<Chest, Vector3i>>();
                using (var r = new BinaryReader(new FileStream(p, FileMode.Open)))
                {
                    r.ReadInt32();
                    int n = r.ReadInt32();
                    for (int i = 0; i < n; i++)
                    {
                        var c = new Chest { X = r.ReadInt32(), Y = r.ReadInt32(), Z = r.ReadInt32(), Face = r.ReadInt32() };
                        bool has = r.ReadBoolean();
                        int mx = r.ReadInt32(), mz = r.ReadInt32();
                        All.Add(c);
                        _at[Key(c.X, c.Y, c.Z)] = c;
                        if (has) mates.Add(new KeyValuePair<Chest, Vector3i>(c, new Vector3i(mx, c.Y, mz)));
                    }
                }
                foreach (KeyValuePair<Chest, Vector3i> m in mates)
                {
                    Chest o = At(m.Value.X, m.Value.Y, m.Value.Z);
                    if (o != null) m.Key.Mate = o;
                }
            }
            catch (Exception e)
            {
                // moved aside not deleted
                L.Err("load", e);
                All.Clear();
                _at.Clear();
                try { File.Move(p, p + ".bad" + DateTime.Now.Ticks); } catch { }
            }
        }
    }
}

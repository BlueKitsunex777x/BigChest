using System.Collections.Generic;
using Blocksters.Terrain;
using BlockStoryCore;
using UnityEngine;

namespace BlockStoryMod
{
    // same screen as the item vacuum. the item you're carrying stays in its slot until it lands, so closing the screen
    // or quitting the game can never lose it. a pair shows side by side, each half keeps its own side
    public static class ChestScreen
    {
        public static Chest Current;
        private const int BagCols = 9;
        private const float Gap = 4f, Pad = 24f, Bar = 18f;

        private class Spot
        {
            public int Index = -1;
            public ItemSlot Slot;
            public bool Same(Spot o) => o != null && Index == o.Index && Slot == o.Slot;
        }

        private static float _cell;
        private static Vector2 _vacScroll, _bagScroll;
        private static List<LootItem> _items = new List<LootItem>();
        private static readonly List<InvGameItem> _shown = new List<InvGameItem>();
        private static float _nextRead;
        private static string _note = "";
        private static float _noteUntil;
        private static GUIStyle _num;

        private static Spot _from, _over;
        private static int _amount;
        // a press that picked something up, letting go over another slot drops it there
        private static bool _pressPick;
        private static string _hover;

        public static bool Open => Current != null;

        public static void Show(Chest v)
        {
            Current = v;
            _vacScroll = _bagScroll = Vector2.zero;
            _nextRead = 0f;
            _note = "";
            _from = null;
            Overlay.ConfigOpen = true;
            ChestSound.Open();
            L.Info("chest opened" + (v.Mate != null ? ", double" : ""));
        }

        public static void Close()
        {
            if (Current == null) return;
            Current = null;
            _from = null;
            Overlay.ConfigOpen = false;
            ChestSound.Close();
            if (Chests.Online) Chests.LetAll();
            GUIUtility.keyboardControl = 0;
        }

        private static void Say(string m) { _note = m; _noteUntil = Time.unscaledTime + 5f; }

        private static InventoryCollector Bag => TerrainLoader.instance?.digger?.inventoryCollector;

        private static int Columns(Chest v) => v.Mate != null ? Chests.Cols * 2 : Chests.Cols;
        private static int Capacity(Chest v) => v.Mate != null ? Chests.Slots * 2 : Chests.Slots;

        // which half a screen slot belongs to, and where it sits in that half's own list
        private static Chest Locate(Chest v, int index, out int at)
        {
            if (v.Mate == null) { at = index; return v; }
            int col = index % (Chests.Cols * 2), row = index / (Chests.Cols * 2);
            at = row * Chests.Cols + col % Chests.Cols;
            Chest left = Chests.LeftOf(v);
            return col < Chests.Cols ? left : left.Mate;
        }

        private static LootItem Slot(Chest v, int index)
        {
            Chest half = Locate(v, index, out int at);
            List<LootItem> items = Chests.Read(half);
            return at < items.Count ? items[at] : new LootItem { name = "" };
        }

        private static void Refresh(Chest v)
        {
            int cap = Capacity(v);
            _items = new List<LootItem>(cap);
            _shown.Clear();
            Chest a = Chests.LeftOf(v);
            List<LootItem> la = Chests.Read(a), lb = a.Mate != null ? Chests.Read(a.Mate) : null;
            for (int i = 0; i < cap; i++)
            {
                Chest half = Locate(v, i, out int at);
                List<LootItem> list = half == a ? la : lb;
                LootItem it = at < list.Count ? list[at] : new LootItem { name = "" };
                _items.Add(it);
                _shown.Add(Chests.IsBlank(it) || InvDatabase.FindByName(it.name) == null ? null : InvDatabase.CreateItem(it.name, it.data, it.count, it.paintData));
            }
            _nextRead = Time.unscaledTime + 0.5f;
        }

        private static List<ItemSlot> Unlocked(ItemStorage s)
        {
            var list = new List<ItemSlot>();
            if (s != null) foreach (ItemSlot i in s) if (!i.Locked) list.Add(i);
            return list;
        }

        private static ItemStorage Storage(bool hotbar)
        {
            InventoryCollector c = Bag;
            if (c == null) return null;
            foreach (ItemStorage s in c.storage)
            {
                if (hotbar ? s is QuickSlotStorage : s is InventoryStorage && !(s is QuickSlotStorage) && s.GetComponent<InvEquipment>() != null) return s;
            }
            return null;
        }

        public static void Draw()
        {
            if (Current == null) return;
            Theme.Build();
            if (!Chests.All.Contains(Current)) { Close(); return; }
            if (_num == null) _num = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };

            Event ev = Event.current;
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
            {
                if (_from != null) _from = null;
                else Close();
                ev.Use();
                return;
            }
            Chest v = Current;
            if (ev.type == EventType.Layout && Time.unscaledTime >= _nextRead) Refresh(v);

            _cell = Mathf.Clamp((Screen.width - Pad * 3f - Bar * 2f - 20f) / (Columns(v) + BagCols) - Gap, 36f, 58f);
            float step = _cell + Gap;
            float vacW = Columns(v) * step + Bar, bagW = BagCols * step + Bar;
            float w = vacW + bagW + Pad * 3f, h = Mathf.Min(Screen.height - 40f, 660f);
            var win = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            GUI.depth = 5;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Theme.Solid(new Color(0f, 0f, 0f, 0.6f)));
            GUI.Box(win, GUIContent.none, Theme.Window);

            int cap = Capacity(v);
            int used = 0;
            foreach (LootItem it in _items) if (!Chests.IsBlank(it)) used++;
            GUI.Label(new Rect(win.x + Pad, win.y + 18, 400, 26), (v.Mate != null ? "Double Big Chest   " : "Big Chest   ") + used + " of " + cap + " slots used", Theme.LabelGold);

            float bx = win.xMax - Pad;
            if (GUI.Button(new Rect(bx -= 84, win.y + 16, 84, 30), "Close", Theme.Button)) { Close(); return; }
            bool storeAll = GUI.Button(new Rect(bx -= 108, win.y + 16, 100, 30), "Store all", Theme.Button);
            bool takeAll = GUI.Button(new Rect(bx -= 108, win.y + 16, 100, 30), "Take all", Theme.Button);
            bool sort = GUI.Button(new Rect(bx -= 78, win.y + 16, 70, 30), "Sort", Theme.Button);

            _over = null;
            _hover = null;
            float top = win.y + 56f, foot = 62f;
            float rightX = win.x + Pad * 2f + vacW;

            // vacuum, left side
            var vacView = new Rect(win.x + Pad, top, vacW, win.yMax - top - foot);
            Grid(vacView, ref _vacScroll, cap, Columns(v), ev, i => new Spot { Index = i }, i => i < _shown.Count ? _shown[i] : null);

            // bag and hotbar, right side
            List<ItemSlot> hot = Unlocked(Storage(true));
            List<ItemSlot> bag = Unlocked(Storage(false));
            float hotH = Mathf.Ceil(hot.Count / (float)BagCols) * step;
            GUI.Label(new Rect(rightX, top - 4f, 300, 22), "Your bag", Theme.LabelGold);
            var bagView = new Rect(rightX, top + 20f, bagW, win.yMax - top - foot - hotH - 48f);
            Grid(bagView, ref _bagScroll, bag.Count, BagCols, ev, i => new Spot { Slot = bag[i] }, i => bag[i].item);
            GUI.Label(new Rect(rightX, bagView.yMax + 2f, 300, 22), "Hotbar", Theme.LabelGold);
            var hotView = new Rect(rightX, bagView.yMax + 24f, bagW, hotH);
            Vector2 none = Vector2.zero;
            Grid(hotView, ref none, hot.Count, BagCols, ev, i => new Spot { Slot = hot[i] }, i => hot[i].item);

            GUI.Label(new Rect(win.x + Pad, win.yMax - 56, w - Pad * 2f, 24),
                _hover ?? "Click to pick up and put down, or drag. Right click: half, then one at a time. Shift click: send it across.", Theme.Label);
            if (_note.Length > 0 && Time.unscaledTime < _noteUntil)
                GUI.Label(new Rect(win.x + Pad, win.yMax - 32, w - Pad * 2f, 24), _note, Theme.LabelGold);

            Carried(ev);
            Mouse(ev, v);
            if (storeAll) StoreAll(v, bag);
            if (takeAll) TakeAll(v);
            if (sort) Sort(v);
        }

        private static void Grid(Rect view, ref Vector2 scroll, int count, int cols, Event ev, System.Func<int, Spot> spot, System.Func<int, InvGameItem> item)
        {
            float step = _cell + Gap;
            bool inView = view.Contains(ev.mousePosition);
            var all = new Rect(0, 0, view.width - Bar, Mathf.Ceil(count / (float)cols) * step);
            scroll = GUI.BeginScrollView(view, scroll, all);
            for (int i = 0; i < count; i++)
            {
                var r = new Rect((i % cols) * step, (i / cols) * step, _cell, _cell);
                Spot s = spot(i);
                bool hovered = inView && r.Contains(ev.mousePosition);
                if (hovered) _over = s;
                GUI.Box(r, GUIContent.none, s.Same(_from) || hovered ? Theme.ButtonSel : Theme.Button);
                InvGameItem it = item(i);
                if (it == null || it.baseItem == null)
                {
                    if (s.Index >= 0 && s.Index < _items.Count && !Chests.IsBlank(_items[s.Index]))
                    {
                        GUI.Label(r, "?", _num);
                        if (hovered) _hover = _items[s.Index].name + " (its mod isn't on)";
                    }
                    continue;
                }
                bool carried = s.Same(_from);
                Icon(new Rect(r.x + 6, r.y + 6, _cell - 12, _cell - 12), it, carried ? 0.3f : 1f);
                if (it.durability > 1 && it.topStack < it.durability)
                {
                    GUI.DrawTexture(new Rect(r.x + 5, r.yMax - 7, _cell - 10, 3), Theme.Solid(new Color(0f, 0f, 0f, 0.8f)));
                    GUI.DrawTexture(new Rect(r.x + 5, r.yMax - 7, (_cell - 10) * it.topStack / it.durability, 3), Theme.Solid(new Color(0.3f, 0.9f, 0.4f)));
                }
                GUI.Label(new Rect(r.x + 2, r.y + 2, _cell - 6, _cell - 4), Count(it.count, it.durability), _num);
                if (hovered) _hover = it.itemName;
            }
            GUI.EndScrollView();
        }

        private static void Carried(Event ev)
        {
            if (_from == null || !Read(_from, out LootItem a)) return;
            InvBaseItem b = InvDatabase.FindByName(a.name);
            if (b == null) return;
            InvGameItem it = InvDatabase.CreateItem(a.name, a.data, _amount, a.paintData);
            var r = new Rect(ev.mousePosition.x - _cell * 0.4f, ev.mousePosition.y - _cell * 0.4f, _cell * 0.8f, _cell * 0.8f);
            Icon(r, it, 1f);
            GUI.Label(r, Count(_amount, b.durability), _num);
        }

        // ---------- mouse ----------

        private static void Mouse(Event ev, Chest v)
        {
            if (ev.type == EventType.MouseDown && ev.button == 0)
            {
                bool shift = ev.shift;
                if (_from != null)
                {
                    if (_over == null) _from = null;
                    else Land(v, _over, _amount);
                }
                else if (_over != null && Read(_over, out LootItem a))
                {
                    if (shift) SendAcross(v, _over);
                    else { _from = _over; _amount = a.count; _pressPick = true; }
                }
                ev.Use();
            }
            else if (ev.type == EventType.MouseDown && ev.button == 1)
            {
                if (_from != null)
                {
                    if (_over != null && Read(_from, out LootItem a)) Land(v, _over, Mathf.Min(_amount, One(a)));
                }
                else if (_over != null && Read(_over, out LootItem b))
                {
                    _from = _over;
                    _amount = Half(b);
                    _pressPick = false;
                }
                ev.Use();
            }
            else if (ev.type == EventType.MouseUp && ev.button == 0)
            {
                if (_pressPick && _from != null && _over != null && !_over.Same(_from)) Land(v, _over, _amount);
                _pressPick = false;
            }
        }

        private static int Durability(LootItem a) => InvDatabase.FindByName(a.name)?.durability ?? 1;
        private static int One(LootItem a) { int d = Durability(a); return (a.count - 1) % d + 1; }

        // same sums as the game: a tool's count is its durability, so half means half the whole ones
        private static int Half(LootItem a)
        {
            int d = Durability(a);
            int stacks = (a.count - 1) / d + 1;
            return stacks < 2 ? a.count : ((stacks >> 1) - 1) * d + One(a);
        }

        // ---------- moving ----------

        private static bool Read(Spot s, out LootItem it)
        {
            it = new LootItem { name = "" };
            if (s.Slot != null)
            {
                InvGameItem g = s.Slot.item;
                if (g == null || g.baseItem == null) return false;
                it = new LootItem { name = g.name, data = g.data, paintData = g.paintData, count = g.count };
                return true;
            }
            if (s.Index < 0 || s.Index >= Capacity(Current)) return false;
            LootItem got = Slot(Current, s.Index);
            if (Chests.IsBlank(got)) return false;
            it = got;
            return true;
        }

        private static void Set(Spot s, LootItem it)
        {
            if (s.Slot != null)
            {
                InvGameItem old = s.Slot.item;
                bool sameKind = old != null && !Chests.IsBlank(it) && old.name == it.name && old.data == it.data && old.paintData == it.paintData;
                if (sameKind) { old.count = it.count; return; }
                Release(old);
                s.Slot.item = Chests.IsBlank(it) ? null : InvDatabase.CreateItem(it.name, it.data, it.count, it.paintData);
                return;
            }
            Chest half = Locate(Current, s.Index, out int at);
            List<LootItem> items = Chests.Read(half);
            while (items.Count <= at) items.Add(new LootItem { name = "" });
            items[at] = it;
            Chests.Write(half, items);
        }

        // the game keeps your held item linked to its bag slot, take it out of your hand before it leaves
        private static void Release(InvGameItem item)
        {
            InvEquipment eq = Bag?.equipment;
            if (eq == null || item == null) return;
            if (eq.GetItem(InvBaseItem.Slot.Weapon) == item) eq.Unequip(InvBaseItem.Slot.Weapon);
            if (eq.GetItem(InvBaseItem.Slot.LeftHand) == item) eq.Unequip(InvBaseItem.Slot.LeftHand);
        }

        private static LootItem With(LootItem a, int count)
        {
            a.count = count;
            if (count <= 0) a = new LootItem { name = "" };
            return a;
        }

        private static bool Same(LootItem a, LootItem b) => a.name == b.name && a.data == b.data && a.paintData == b.paintData;

        // two damaged tools can't share a stack, the game refuses that as well
        private static bool CanStack(LootItem a, int amount, LootItem b)
        {
            int d = Durability(a);
            return Same(a, b) && (amount % d == 0 || b.count % d == 0);
        }

        private static void Land(Chest v, Spot to, int amount)
        {
            _nextRead = 0f;
            if (to.Same(_from)) { _from = null; return; }
            if (!Read(_from, out LootItem a)) { _from = null; return; }
            amount = Mathf.Min(amount, a.count);
            if (InvDatabase.FindByName(a.name) == null) { Say("That item's mod isn't on"); _from = null; return; }
            if (to.Index >= 0 && Chests.Refuses(a.name)) { Say("Diamonds can't go in a chest"); _from = null; return; }

            bool occupied = Read(to, out LootItem b);
            bool partial = amount < a.count;
            if (!occupied)
            {
                if (!partial && to.Slot != null && _from.Slot != null) { MoveWhole(to); return; }
                Set(to, With(a, amount));
                Set(_from, With(a, a.count - amount));
            }
            else if (CanStack(a, amount, b))
            {
                Set(to, With(b, b.count + amount));
                Set(_from, With(a, a.count - amount));
            }
            else if (!partial)
            {
                if (to.Slot != null && _from.Slot != null) { MoveWhole(to); return; }
                if (_from.Index >= 0 && Chests.Refuses(b.name)) { Say("Diamonds can't go in a chest"); _from = null; return; }
                Set(_from, b);
                Set(to, a);
            }
            else { Say("Those don't stack"); return; }

            _amount -= amount;
            if (_amount <= 0 || !Read(_from, out LootItem _)) _from = null;
        }

        // bag to bag keeps the very same item object, nothing about it can get lost on the way
        private static void MoveWhole(Spot to)
        {
            InvGameItem a = _from.Slot.item, b = to.Slot.item;
            Release(a);
            Release(b);
            to.Slot.item = a;
            _from.Slot.item = b;
            _from = null;
        }

        private static void SendAcross(Chest v, Spot s)
        {
            _nextRead = 0f;
            if (!Read(s, out LootItem a)) return;
            if (s.Index >= 0)
            {
                InventoryCollector c = Bag;
                if (c == null || InvDatabase.FindByName(a.name) == null || !c.Collect(a.name, a.data, a.count, a.paintData)) { Say("Your bag is full"); return; }
                Set(s, new LootItem { name = "" });
                return;
            }
            if (!IntoChest(v, s, a)) Say(Chests.Refuses(a.name) ? "Diamonds can't go in a chest" : "The chest is full");
        }

        private static bool IntoChest(Chest v, Spot s, LootItem a)
        {
            if (Chests.Refuses(a.name)) return false;
            int cap = Capacity(v), at = -1, free = -1;
            for (int i = 0; i < cap && at < 0; i++)
            {
                LootItem o = Slot(v, i);
                if (Chests.IsBlank(o)) { if (free < 0) free = i; }
                else if (CanStack(a, a.count, o)) { at = i; a.count += o.count; }
            }
            if (at < 0) at = free;
            if (at < 0) return false;
            Set(new Spot { Index = at }, a);
            Set(s, new LootItem { name = "" });
            return true;
        }

        private static void StoreAll(Chest v, List<ItemSlot> bag)
        {
            _nextRead = 0f;
            _from = null;
            int moved = 0;
            foreach (ItemSlot i in bag)
            {
                var s = new Spot { Slot = i };
                if (!Read(s, out LootItem a)) continue;
                if (IntoChest(v, s, a)) moved++;
                else if (!Chests.Refuses(a.name)) Say("The chest is full");
            }
            L.Info("store all moved " + moved);
        }

        // joins up what stacks, then lays it all out by name from the first slot
        private static void Sort(Chest v)
        {
            _from = null;
            Refresh(v);
            var all = new List<LootItem>();
            foreach (LootItem it in _items) if (!Chests.IsBlank(it)) all.Add(it);
            all.Sort((a, b) =>
            {
                int by = string.CompareOrdinal(a.name, b.name);
                if (by == 0) by = a.data.CompareTo(b.data);
                if (by == 0) by = a.paintData.CompareTo(b.paintData);
                return by != 0 ? by : b.count.CompareTo(a.count);
            });
            var tidy = new List<LootItem>();
            foreach (LootItem it in all)
            {
                int last = tidy.Count - 1;
                if (last >= 0 && CanStack(it, it.count, tidy[last]))
                {
                    LootItem joined = tidy[last];
                    joined.count += it.count;
                    tidy[last] = joined;
                }
                else tidy.Add(it);
            }

            Chest a0 = Chests.LeftOf(v);
            var la = new List<LootItem>();
            var lb = new List<LootItem>();
            int cap = Capacity(v);
            for (int i = 0; i < cap; i++)
            {
                Chest half = Locate(v, i, out int at);
                List<LootItem> list = half == a0 ? la : lb;
                while (list.Count <= at) list.Add(new LootItem { name = "" });
                if (i < tidy.Count) list[at] = tidy[i];
            }
            Chests.Write(a0, la);
            if (a0.Mate != null) Chests.Write(a0.Mate, lb);
            _nextRead = 0f;
            L.Info("sorted " + all.Count + " stacks into " + tidy.Count);
        }

        private static void TakeAll(Chest v)
        {
            _nextRead = 0f;
            _from = null;
            int moved = 0;
            InventoryCollector c = Bag;
            if (c == null) return;
            int cap = Capacity(v);
            for (int i = 0; i < cap; i++)
            {
                LootItem it = Slot(v, i);
                if (Chests.IsBlank(it) || InvDatabase.FindByName(it.name) == null) continue;
                if (c.Collect(it.name, it.data, it.count, it.paintData)) { Set(new Spot { Index = i }, new LootItem { name = "" }); moved++; }
                else Say("Your bag is full");
            }
            L.Info("take all moved " + moved);
        }

        // what the game's own slots show: tools hold durability in count, so the number is how many whole ones
        private static string Count(int count, int durability)
        {
            int n = durability > 1 ? (count - 1) / durability + 1 : count;
            return n > 1 ? n.ToString() : "";
        }

        private static void Icon(Rect r, InvGameItem it, float alpha)
        {
            UIAtlas a = it.baseItem?.iconAtlas;
            if (a == null) return;
            if (a.replacement != null) a = a.replacement;
            UIAtlas.Sprite sp = a.GetSprite(it.baseItem.iconName);
            Texture tex = a.texture;
            if (sp == null || tex == null) return;
            Rect uv = sp.outer;
            // ngui keeps pixel rects top-left based, unity uv is bottom-left
            if (a.coordinates == UIAtlas.Coordinates.Pixels)
                uv = new Rect(uv.x / tex.width, 1f - (uv.y + uv.height) / tex.height, uv.width / tex.width, uv.height / tex.height);
            Color was = GUI.color;
            Color c = it.color;
            c.a = alpha;
            GUI.color = c;
            GUI.DrawTextureWithTexCoords(r, tex, uv);
            GUI.color = was;
        }
    }
}

using System;
using BepInEx;
using Blocksters.MathLib;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using BlockStoryCore;
using BlockStoryMod;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("com.mrc.blockstory.bigchest", "Big Chest", "1.1.0")]
[BepInDependency(Core.Guid)]
[BepInDependency("com.mrc.blockstory.quickcraft")]
public class BigChestPlugin : BaseUnityPlugin
{
    private Chest _aimed;
    private int _swallow;
    private string _toast;
    private float _toastUntil;
    private GUIStyle _hud;
    private TerrainLoader _recipesFor;
    private float _nextRecipes;
    private bool _opening;

    private void Awake()
    {
        try { new Harmony("com.mrc.blockstory.bigchest").PatchAll(); L.Info("loaded"); }
        catch (Exception e) { L.Err("PatchAll", e); }

        // real item so character editor and craft from inventory list it
        BSItems.WhenReady(() =>
        {
            InvBaseItem it = BSItems.RegisterClone(Chests.Host, Chests.Item);
            if (it == null) { L.Warn("chest item FAILED"); return; }
            BSItems.SetIcon(Chests.Item, ChestView.Icon());
            BSItemLook.Register(Chests.Item, ChestView.MiniModel);
        });

        ModRegistry.Register(new ModInfo
        {
            Name = "Big Chest",
            Description = "A Big Chest with 64 slots. Put two side by side and they become one chest with 128. Online it needs a Block Story Server (BSMK), one player per chest at a time.",
            GetEnabled = () => Chests.Enabled,
            SetEnabled = Chests.SetEnabled,
            Config = new ModConfig()
                .Button("Creative worlds", "Give me 10 chests", GiveChests, "Otherwise craft them, the recipe is in Craft From Inventory."),
        });
    }

    private static void GiveChests()
    {
        if (!BSWorld.InWorld || TerrainLoader.Mode != WorldMode.Creative) { L.Info("give refused, not creative"); return; }
        L.Info("give chests " + BSItems.Give(Chests.Item, 10));
    }

    // goes in the normal recipe list as a custom one, that's the list craft from inventory reads
    private static bool Recipe()
    {
        string[] parts = { "Plank", "Iron Block", "Plank", "Plank", "Safebox", "Plank", "Plank", "Plank", "Plank" };
        var g = new InvGameItem[3, 3];
        for (int i = 0; i < 9; i++)
        {
            g[i / 3, i % 3] = BSItems.Create(parts[i], 1);
            if (g[i / 3, i % 3] == null) { L.Warn("chest recipe skipped, no item called " + parts[i]); return false; }
        }
        InvGameItem result = BSItems.Create(Chests.Item, 1);
        if (result == null) { L.Warn("chest recipe skipped, item isn't registered"); return false; }
        if (BSItems.RegisterRecipe(Chests.Item, g, result)) return true;
        foreach (RecipeManager rm in FindObjectsByType<RecipeManager>())
            if (rm.recipeList != null) foreach (Recipe r in rm.recipeList) if (r != null && r.name == Chests.Item) return true;
        return false;
    }

    private void Update()
    {
        try { Step(); }
        catch (Exception e) { L.Err("update", e); }
    }

    private void Step()
    {
        if (!BSWorld.InWorld)
        {
            if (ChestScreen.Open) ChestScreen.Close();
            Chests.Tick();
            return;
        }

        if (Time.unscaledTime >= _nextRecipes)
        {
            _nextRecipes = Time.unscaledTime + 2f;
            if (_recipesFor != TerrainLoader.instance && Recipe()) { _recipesFor = TerrainLoader.instance; L.Info("chest recipe registered"); }
        }

        Chests.Tick();

        if (ChestPlacePatch.Toast != null) { _toast = ChestPlacePatch.Toast; _toastUntil = Time.time + 5f; ChestPlacePatch.Toast = null; }

        _aimed = null;
        if (!ChestScreen.Open && Chests.Usable && Cursor.lockState == CursorLockMode.Locked && Inventory.allowInput) _aimed = Aimed();

        // shift lets you place against a chest instead of opening it, that's how you'd build a pair off its side
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (shift) _aimed = null;
        if (_aimed != null && Input.GetMouseButtonDown(1)) { Open(_aimed); _swallow = 3; }
        if (_swallow > 0 && !Input.GetMouseButton(1)) _swallow--;
    }

    // online the server app has to hand over both halves first, so a second player can't change them at the same time
    private void Open(Chest c)
    {
        if (!Chests.Online) { ChestScreen.Show(c); return; }
        if (_opening) return;
        _opening = true;
        Chests.Claim(c, problem =>
        {
            _opening = false;
            if (problem != null) { _toast = problem; _toastUntil = Time.time + 5f; Chests.LetAll(); return; }
            if (!BSWorld.InWorld || !Chests.All.Contains(c) || ChestScreen.Open) { Chests.LetAll(); return; }
            ChestScreen.Show(c);
        });
    }

    private void LateUpdate()
    {
        // same as the grave, stops the right click also placing whatever's in your hand
        if (_aimed != null || _swallow > 0) Placer.isUsing = true;
    }

    private static Chest Aimed()
    {
        TerrainLoader tl = TerrainLoader.instance;
        Camera cam = Camera.main;
        if (tl == null || tl.world == null || cam == null) return null;
        Vector3 o = cam.transform.position;
        int reach = tl.digger != null ? Mathf.Max(4, tl.digger.reach) : 10;
        float maxSq = (reach + 1f) * (reach + 1f);
        foreach (RayTracer.Hit h in RayTracer.trace(o, cam.transform.forward))
        {
            float dx = h.x + 0.5f - o.x, dy = h.y + 0.5f - o.y, dz = h.z + 0.5f - o.z;
            if (dx * dx + dy * dy + dz * dz > maxSq) return null;
            Block b = tl.world[new Vector3i(h.x, h.y, h.z)];
            if (b.IsAir || b.IsLiquid) continue;
            return Chests.At(h.x, h.y, h.z);
        }
        return null;
    }

    private void OnGUI()
    {
        if (ChestScreen.Open) { ChestScreen.Draw(); return; }
        if (!BSWorld.InWorld || Cursor.lockState != CursorLockMode.Locked) return;
        if (_hud == null) _hud = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
        float y = Screen.height * 0.5f + 40f;
        if (_aimed != null) Line(y, (_aimed.Mate != null ? "Double Big Chest" : "Big Chest") + ". Right click: open. Shift + right click: place against it");
        if (_toast != null && Time.time < _toastUntil) Line(y + 26f, _toast);
    }

    private void Line(float y, string text)
    {
        var r = new Rect(0f, y, Screen.width, 24f);
        _hud.normal.textColor = Color.black;
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _hud);
        _hud.normal.textColor = new Color(0.95f, 0.8f, 0.4f);
        GUI.Label(r, text, _hud);
    }
}

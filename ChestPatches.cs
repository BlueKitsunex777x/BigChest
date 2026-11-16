using Blocksters.MathLib;
using Blocksters.Terrain.chunk;
using BlockStoryCore;
using BlockStoryMod;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(BlockItemData), nameof(BlockItemData.ToBlock), new[] { typeof(string), typeof(ushort), typeof(ushort) })]
public static class ChestToBlockPatch
{
    private static bool Prefix(string name, ref Block __result)
    {
        if (name != Chests.Item) return true;
        BlockType bt = BSBlocks.Find(Chests.Host);
        if (bt == null) return true;
        __result = new Block(bt);
        return false;
    }
}

[HarmonyPatch(typeof(Placer), "PlaceBlockInternal")]
public static class ChestPlacePatch
{
    public static string Toast;

    private static bool Prefix(Placer __instance, ref bool __state)
    {
        __state = false;
        if (__instance.collector?.weapon?.baseItem?.name != Chests.Item) return true;
        if (BSWorld.InRealm) { Toast = "The Big Chest doesn't work in realms"; return false; }
        if (BSWorld.Online && !BSServerStore.Ready) { Toast = "Online the Big Chest needs a Block Story Server"; return false; }
        __state = true;
        return true;
    }

    private static void Postfix(Placer __instance, bool __state)
    {
        if (!__state) return;
        // the block's still queued at this point. if the game refused the place the check in Chests.Tick drops this again
        Vector3i at = __instance.selected;
        if (Chests.At(at.X, at.Y, at.Z) == null) Chests.Add(at);
    }
}

// mining one gives the chest back, not a plank, plus whatever that half was holding
[HarmonyPatch(typeof(Digger), "DropLoot", [typeof(Vector3), typeof(Block), typeof(int)])]
public static class ChestMineDropPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Digger __instance, Vector3 pos, Block block, int count)
    {
        // online the game holds the drop back until the server says the block really broke, see ChestMineDropOnlinePatch
        if (Chests.Online) return true;
        return !ChestMineDropOnlinePatch.Swap(__instance, pos, block, count);
    }
}

[HarmonyPatch(typeof(Digger), "DropLootImmediate")]
public static class ChestMineDropOnlinePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Digger __instance, Vector3 pos, Block block, int count)
    {
        if (!Chests.Online) return true;
        return !Swap(__instance, pos, block, count);
    }

    internal static bool Swap(Digger digger, Vector3 pos, Block block, int count)
    {
        Chest c = Chests.At(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), Mathf.FloorToInt(pos.z));
        if (c == null || block.type == null || block.type.name != Chests.Host) return false;
        digger.inventoryCollector?.DropLoot(pos, Chests.Item, 0, count, true, 1f, 0);
        Chests.Spill(c);
        return true;
    }
}

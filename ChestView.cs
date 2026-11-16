using System.Collections.Generic;
using Blocksters.MathLib;
using Blocksters.Terrain.chunk;
using BlockStoryCore;
using UnityEngine;

// cubes sat just outside the real block, no colliders so the game's click rays go through to it. built facing +z,
// +x along the long side of a pair, then the whole thing is turned. every layer sticks out further than the one
// under it so no two faces share a plane and flicker
public static class ChestView
{
    private const float Half = 0.52f;
    private const float Seam = 0.14f;
    private const float LidTop = 0.60f;
    private const float OpenAngle = 72f;
    private const int Size = 64;
    private const float DetailRange = 90f;

    private static Texture2D _wood, _lidWood, _end, _iron, _ironDark, _brass, _lock, _dark, _coins, _ruby;
    private static float _nextShade;
    private static DayTimeManager _time;

    // ---------- textures ----------

    private static Texture2D New() => new(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

    private static void Textures()
    {
        if (_wood != null) return;
        _wood = Planks(1, 6, new Color(0.47f, 0.27f, 0.13f));
        _lidWood = Planks(2, 5, new Color(0.53f, 0.31f, 0.15f));
        _end = Planks(3, 6, new Color(0.40f, 0.23f, 0.11f));
        _iron = Metal(4, new Color(0.34f, 0.35f, 0.39f), 0.05f);
        _ironDark = Metal(5, new Color(0.17f, 0.17f, 0.20f), 0.03f);
        _brass = Metal(6, new Color(0.86f, 0.64f, 0.25f), 0.08f);
        _lock = LockPlate();
        _dark = Inside();
        _coins = Coins();
        _ruby = Ruby();
    }

    private static Texture2D Planks(int seed, int boards, Color tone)
    {
        var t = New();
        var r = new System.Random(seed * 101);
        int high = Size / boards;
        float[] shade = new float[boards + 1];
        int[] joint = new int[boards + 1];
        for (int i = 0; i <= boards; i++) { shade[i] = 0.84f + (float)r.NextDouble() * 0.28f; joint[i] = r.Next(10, Size - 10); }
        // a knot or two per face, the grain bends round them further down
        var knots = new List<Vector3>();
        for (int i = 0; i < 3; i++) knots.Add(new Vector3(r.Next(6, Size - 6), r.Next(6, Size - 6), 2f + (float)r.NextDouble() * 2.4f));
        float phase = (float)r.NextDouble() * 10f;

        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int b = Mathf.Min(y / high, boards);
                int inBoard = y % high;
                float bend = 0f, knot = 0f;
                foreach (Vector3 k in knots)
                {
                    float dx = (x - k.x) / (k.z * 2.4f), dy = (y - k.y) / k.z;
                    float d = dx * dx + dy * dy;
                    if (d < 7f) bend += (y < k.y ? -1f : 1f) * 3.2f / (1f + d);
                    if (d < 1f) knot = Mathf.Max(knot, 1f - d);
                }
                float wave = Mathf.Sin((y + bend + Mathf.Sin(x * 0.11f + b * 2f + phase) * 1.6f) * 1.5f + b * 3f);
                float grain = 0.9f + wave * 0.075f + (float)r.NextDouble() * 0.05f - 0.025f;
                if (r.NextDouble() < 0.012) grain -= 0.14f;
                float v = shade[b] * grain;
                if (knot > 0f) v *= 1f - knot * 0.55f + Mathf.Sin(knot * 16f) * 0.06f;
                // dark gap between boards with a lit edge above it, reads as depth
                if (inBoard == 0) v = 0.30f;
                else if (inBoard == 1) v *= 1.20f;
                else if (inBoard == high - 1) v *= 0.72f;
                if (x == joint[b]) v = 0.34f;
                else if (x == joint[b] + 1) v *= 1.15f;
                t.SetPixel(x, y, new Color(tone.r * v, tone.g * v, tone.b * v, 1f));
            }
        // nail heads at each end of every board
        for (int b = 0; b < boards; b++)
            foreach (int nx in new[] { 3, Size - 5, joint[b] - 3, joint[b] + 3 })
            {
                if (nx < 1 || nx > Size - 3) continue;
                int ny = b * high + high / 2;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                        t.SetPixel(nx + dx, ny + dy, dx == 0 && dy == 1 ? new Color(0.62f, 0.62f, 0.66f) : new Color(0.20f, 0.20f, 0.23f));
            }
        t.Apply();
        return t;
    }

    private static Texture2D Metal(int seed, Color tone, float scratch)
    {
        var t = New();
        var r = new System.Random(seed * 211);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float v = 0.88f + (float)r.NextDouble() * 0.14f + Mathf.Sin((x + y) * 0.28f) * 0.04f;
                if (r.NextDouble() < scratch) v += 0.16f;
                int edge = Mathf.Min(x, y, Size - 1 - x, Size - 1 - y);
                if (edge < 2) v *= y > Size / 2 || x < Size / 2 ? 1.28f : 0.62f;
                t.SetPixel(x, y, new Color(tone.r * v, tone.g * v, tone.b * v, 1f));
            }
        t.Apply();
        return t;
    }

    private static Texture2D LockPlate()
    {
        var t = New();
        var r = new System.Random(77);
        var tone = new Color(0.86f, 0.64f, 0.25f);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float v = 0.86f + (float)r.NextDouble() * 0.14f;
                int edge = Mathf.Min(x, y, Size - 1 - x, Size - 1 - y);
                if (edge < 3) v *= y > Size / 2 || x < Size / 2 ? 1.25f : 0.55f;
                else if (edge == 6) v *= 0.68f;
                else if (edge == 7) v *= 1.18f;
                float cx = x - 31.5f, cy = y - 38f;
                bool hole = cx * cx + cy * cy < 36f || (Mathf.Abs(cx) < 3.2f - (y - 18) * -0.05f && y > 18 && y < 38);
                bool ring = cx * cx + cy * cy < 64f;
                Color c = new(tone.r * v, tone.g * v, tone.b * v, 1f);
                if (ring) c *= 0.82f;
                if (hole) c = new Color(0.03f, 0.02f, 0.02f, 1f);
                c.a = 1f;
                t.SetPixel(x, y, c);
            }
        t.Apply();
        return t;
    }

    private static Texture2D Inside()
    {
        var t = New();
        var r = new System.Random(88);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float v = 0.08f + (float)r.NextDouble() * 0.03f;
                if (y % 12 == 0) v = 0.04f;
                t.SetPixel(x, y, new Color(v * 1.3f, v * 0.9f, v * 0.6f, 1f));
            }
        t.Apply();
        return t;
    }

    // a heap of coins seen from above
    private static Texture2D Coins()
    {
        var t = New();
        var r = new System.Random(99);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float v = 0.32f + (float)r.NextDouble() * 0.1f;
                t.SetPixel(x, y, new Color(0.62f * v * 2f, 0.42f * v * 2f, 0.12f * v * 2f, 1f));
            }
        for (int i = 0; i < 90; i++)
        {
            int cx = r.Next(0, Size), cy = r.Next(0, Size);
            float bright = 0.78f + (float)r.NextDouble() * 0.3f;
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy, d = dx * dx + dy * dy;
                    if (x < 0 || y < 0 || x >= Size || y >= Size || d > 16) continue;
                    float v = bright;
                    if (d > 10) v *= dy > 0 ? 1.2f : 0.62f;
                    else if (d < 4) v *= 0.84f;
                    t.SetPixel(x, y, new Color(0.98f * v, 0.76f * v, 0.26f * v, 1f));
                }
        }
        t.Apply();
        return t;
    }

    private static Texture2D Ruby()
    {
        var t = New();
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int cx = Mathf.Abs(x * 2 - Size + 1), cy = Mathf.Abs(y * 2 - Size + 1);
                float v = cx + cy < Size * 0.7f ? 1.1f : y > x ? 0.86f : 0.52f;
                if (x > 12 && x < 26 && y > 38 && y < 50) v = 1.7f;
                t.SetPixel(x, y, new Color(0.84f * v, 0.08f * v, 0.14f * v, 1f));
            }
        t.Apply();
        return t;
    }

    // ---------- pieces ----------

    private class Parts
    {
        public readonly Dictionary<Texture2D, Material> Mats = [];
        public Transform Group;
    }

    private static void Box(Parts p, Texture2D tex, Vector3 min, Vector3 max)
    {
        GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(c.GetComponent<Collider>());
        c.transform.SetParent(p.Group, false);
        c.transform.localPosition = (min + max) * 0.5f;
        c.transform.localScale = max - min;
        if (!p.Mats.TryGetValue(tex, out Material m)) { m = BSModel.SkinMaterial(tex); p.Mats[tex] = m; }
        c.GetComponent<Renderer>().sharedMaterial = m;
    }

    // a band all the way round, layered over whatever's underneath
    private static void Hoop(Parts p, Texture2D tex, float w, float d, float low, float high, float proud)
    {
        float x = w + proud, z = d + proud;
        Box(p, tex, new Vector3(-x, low, z - 0.03f), new Vector3(x, high, z));
        Box(p, tex, new Vector3(-x, low, -z), new Vector3(x, high, -z + 0.03f));
        Box(p, tex, new Vector3(-x, low, -z + 0.03f), new Vector3(-x + 0.03f, high, z - 0.03f));
        Box(p, tex, new Vector3(x - 0.03f, low, -z + 0.03f), new Vector3(x, high, z - 0.03f));
    }

    private static void Rivet(Parts p, Vector3 at, Vector3 normal)
    {
        const float r = 0.024f, tall = 0.012f;
        Vector3 size = new(Mathf.Abs(normal.x) > 0.5f ? tall : r, Mathf.Abs(normal.y) > 0.5f ? tall : r, Mathf.Abs(normal.z) > 0.5f ? tall : r);
        Box(p, _brass, at - size + normal * tall, at + size + normal * tall);
    }

    // the bottom half of the chest
    private static void Body(Parts p, float w, int halves)
    {
        float d = Half, low = -Half;
        float each = w * 2f / halves;
        for (int i = 0; i < halves; i++)
        {
            float a = -w + each * i;
            Box(p, _wood, new Vector3(a, low, -d), new Vector3(a + each, Half, d));
        }
        // base rail, top rail, end caps
        Hoop(p, _iron, w, d, low, low + 0.09f, 0.016f);
        Hoop(p, _ironDark, w, d, Seam - 0.06f, Seam, 0.016f);

        // straps down the front and back of each half
        foreach (int side in new[] { 1, -1 })
            for (int i = 0; i < halves; i++)
            {
                float mid = -w + each * (i + 0.5f);
                foreach (float off in new[] { -0.30f, 0.30f })
                {
                    float x = mid + off * each;
                    float z = d + 0.032f;
                    Vector3 a = new(x - 0.042f, low, side > 0 ? z - 0.03f : -z);
                    Vector3 b = new(x + 0.042f, Seam, side > 0 ? z : -z + 0.03f);
                    Box(p, _iron, a, b);
                    for (float y = low + 0.12f; y < Seam - 0.06f; y += 0.15f)
                        Rivet(p, new Vector3(x, y, side * z), new Vector3(0f, 0f, side));
                }
            }

        // corner guards with a rivet each way
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                float x = sx * (w + 0.048f), z = sz * (d + 0.048f);
                Box(p, _ironDark, new Vector3(Mathf.Min(x, x - sx * 0.11f), low, Mathf.Min(z, z - sz * 0.11f)),
                    new Vector3(Mathf.Max(x, x - sx * 0.11f), Seam, Mathf.Max(z, z - sz * 0.11f)));
                foreach (float y in new[] { low + 0.14f, Seam - 0.14f })
                {
                    Rivet(p, new Vector3(x - sx * 0.055f, y, z), new Vector3(0f, 0f, sz));
                    Rivet(p, new Vector3(x, y, z - sz * 0.055f), new Vector3(sx, 0f, 0f));
                }
            }

        // a carry handle on each end
        foreach (int sx in new[] { -1, 1 })
        {
            float x = sx * (w + 0.034f), o = sx * 0.05f;
            Vector3 Lo(float y, float z) => new(Mathf.Min(x, x + o), y, z);
            Vector3 Hi(float y, float z) => new(Mathf.Max(x, x + o), y, z);
            Box(p, _ironDark, Lo(-0.10f, -0.20f), Hi(-0.07f, 0.20f));
            Box(p, _ironDark, Lo(-0.07f, -0.20f), Hi(0.02f, -0.17f));
            Box(p, _ironDark, Lo(-0.07f, 0.17f), Hi(0.02f, 0.20f));
            float mx = sx * (w + 0.016f), my = sx * 0.03f;
            foreach (float z in new[] { -0.185f, 0.185f })
                Box(p, _brass, new Vector3(Mathf.Min(mx, mx + my), -0.02f, z - 0.04f), new Vector3(Mathf.Max(mx, mx + my), 0.05f, z + 0.04f));
        }

        // lock, in the middle of the whole thing so a pair shares one
        float lz = d + 0.05f;
        Box(p, _lock, new Vector3(-0.15f, -0.22f, d), new Vector3(0.15f, Seam - 0.01f, lz));
        foreach (int sx in new[] { -1, 1 })
            foreach (float y in new[] { -0.19f, 0.09f })
                Rivet(p, new Vector3(sx * 0.12f, y, lz), Vector3.forward);
    }

    // only drawn while the lid's up, the real block's a full cube so all this sits on top of it and under the closed lid
    private static void Inner(Parts p, float w)
    {
        float d = Half, y = Half;
        const float rim = 0.05f;
        Box(p, _dark, new Vector3(-w + rim, y, -d + rim), new Vector3(w - rim, y + 0.012f, d - rim));
        Box(p, _wood, new Vector3(-w, y, -d), new Vector3(w, y + 0.024f, -d + rim));
        Box(p, _wood, new Vector3(-w, y, d - rim), new Vector3(w, y + 0.024f, d));
        Box(p, _wood, new Vector3(-w, y, -d + rim), new Vector3(-w + rim, y + 0.024f, d - rim));
        Box(p, _wood, new Vector3(w - rim, y, -d + rim), new Vector3(w, y + 0.024f, d - rim));
        Box(p, _coins, new Vector3(-w + rim + 0.03f, y + 0.012f, -d + rim + 0.03f), new Vector3(w - rim - 0.03f, y + 0.032f, d - rim - 0.03f));
        Box(p, _coins, new Vector3(-w * 0.55f, y + 0.032f, -d * 0.55f), new Vector3(w * 0.55f, y + 0.046f, d * 0.55f));
        Box(p, _ruby, new Vector3(-0.07f, y + 0.046f, -0.07f), new Vector3(0.07f, y + 0.055f, 0.07f));
    }

    // built in place, the group it's in swings on the back edge. hollow so it can come down over the coins
    private static void LidPart(Parts p, float w, int halves)
    {
        float d = Half + 0.02f, low = Seam, top = LidTop;
        float t = 0.02f;
        float each = (w + 0.02f) * 2f / halves;
        for (int i = 0; i < halves; i++)
        {
            float a = -(w + 0.02f) + each * i, b = a + each;
            Box(p, _lidWood, new Vector3(a, top - t, -d), new Vector3(b, top, d));
            Box(p, _lidWood, new Vector3(a, low, d - t), new Vector3(b, top - t, d));
            Box(p, _lidWood, new Vector3(a, low, -d), new Vector3(b, top - t, -d + t));
        }
        Box(p, _end, new Vector3(-w - 0.02f, low, -d + t), new Vector3(-w - 0.02f + t, top - t, d - t));
        Box(p, _end, new Vector3(w + 0.02f - t, low, -d + t), new Vector3(w + 0.02f, top - t, d - t));
        // dark underside for when it's up
        Box(p, _dark, new Vector3(-w, top - t - 0.004f, -d + t), new Vector3(w, top - t, d - t));

        // raised crown on top
        float cw = w - 0.12f, cd = d - 0.14f;
        float each2 = cw * 2f / halves;
        for (int i = 0; i < halves; i++)
        {
            float a = -cw + each2 * i;
            Box(p, _lidWood, new Vector3(a, top, -cd), new Vector3(a + each2, top + 0.035f, cd));
        }

        Hoop(p, _iron, w + 0.02f, d, low, low + 0.06f, 0.016f);
        Hoop(p, _ironDark, w + 0.02f, d, top - 0.05f, top + 0.014f, 0.016f);

        // straps carry on over the lid and down the back
        for (int i = 0; i < halves; i++)
        {
            float mid = -w + w * 2f / halves * (i + 0.5f);
            foreach (float off in new[] { -0.30f, 0.30f })
            {
                float x = mid + off * (w * 2f / halves);
                float z = d + 0.032f, h = top + 0.05f;
                Box(p, _iron, new Vector3(x - 0.042f, low, z - 0.03f), new Vector3(x + 0.042f, h, z));
                Box(p, _iron, new Vector3(x - 0.042f, low, -z), new Vector3(x + 0.042f, h, -z + 0.03f));
                Box(p, _iron, new Vector3(x - 0.042f, h - 0.012f, -z + 0.03f), new Vector3(x + 0.042f, h, z - 0.03f));
                for (float y = low + 0.09f; y < h - 0.04f; y += 0.13f)
                {
                    Rivet(p, new Vector3(x, y, z), Vector3.forward);
                    Rivet(p, new Vector3(x, y, -z), Vector3.back);
                }
                for (float zz = -cd + 0.08f; zz < cd; zz += 0.2f)
                    Rivet(p, new Vector3(x, h, zz), Vector3.up);
            }
        }

        // corner caps
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                float x = sx * (w + 0.068f), z = sz * (d + 0.048f);
                Box(p, _brass, new Vector3(Mathf.Min(x, x - sx * 0.10f), top - 0.09f, Mathf.Min(z, z - sz * 0.10f)),
                    new Vector3(Mathf.Max(x, x - sx * 0.10f), top + 0.026f, Mathf.Max(z, z - sz * 0.10f)));
                Box(p, _ironDark, new Vector3(Mathf.Min(x, x - sx * 0.09f), low, Mathf.Min(z, z - sz * 0.09f)),
                    new Vector3(Mathf.Max(x, x - sx * 0.09f), top - 0.09f, Mathf.Max(z, z - sz * 0.09f)));
            }

        // hinges on the back
        for (int i = 0; i < halves; i++)
        {
            float mid = -w + w * 2f / halves * (i + 0.5f);
            Box(p, _brass, new Vector3(mid - 0.09f, low - 0.05f, -d - 0.05f), new Vector3(mid + 0.09f, low + 0.06f, -d + 0.01f));
        }

        // hasp that hangs down over the lock, with a stone in it
        float hz = Half + 0.075f;
        Box(p, _brass, new Vector3(-0.07f, low - 0.08f, d - 0.01f), new Vector3(0.07f, low + 0.16f, hz));
        Box(p, _ruby, new Vector3(-0.038f, low + 0.03f, hz), new Vector3(0.038f, low + 0.11f, hz + 0.018f));
        Box(p, _ironDark, new Vector3(-0.022f, low - 0.06f, hz), new Vector3(0.022f, low - 0.01f, hz + 0.01f));
    }

    // hundreds of little cubes per chest would cost a lot once somebody builds a storage room, so each group
    // becomes one mesh per texture
    private static void Merge(Transform group)
    {
        var byMat = new Dictionary<Material, List<CombineInstance>>();
        Matrix4x4 back = group.worldToLocalMatrix;
        var kids = new List<GameObject>();
        foreach (MeshFilter f in group.GetComponentsInChildren<MeshFilter>(true))
        {
            Material m = f.GetComponent<Renderer>().sharedMaterial;
            if (!byMat.TryGetValue(m, out List<CombineInstance> list)) { list = []; byMat[m] = list; }
            list.Add(new CombineInstance { mesh = f.sharedMesh, transform = back * f.transform.localToWorldMatrix });
            kids.Add(f.gameObject);
        }
        foreach (KeyValuePair<Material, List<CombineInstance>> kv in byMat)
        {
            var mesh = new Mesh();
            mesh.CombineMeshes([.. kv.Value], true, true);
            mesh.RecalculateBounds();
            bool plain = kv.Key.mainTexture == _wood || kv.Key.mainTexture == _lidWood;
            var go = new GameObject(plain ? "part" : "detail");
            go.transform.SetParent(group, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = kv.Key;
        }
        foreach (GameObject k in kids) Object.DestroyImmediate(k);
    }

    public static GameObject Build(Chest c)
    {
        Textures();
        Vector3 centre = new(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f);
        if (c.Mate != null) centre = (centre + new Vector3(c.Mate.X + 0.5f, c.Mate.Y + 0.5f, c.Mate.Z + 0.5f)) * 0.5f;
        Vector3i f = Chests.Dirs[c.Face];
        GameObject root = Model(c.Mate != null ? 2 : 1, centre, Quaternion.LookRotation(new Vector3(f.X, 0f, f.Z), Vector3.up), out Transform lid);
        c.Lid = lid;
        c.LidAngle = 0f;
        return root;
    }

    private static GameObject Model(int halves, Vector3 centre, Quaternion turn, out Transform lidPivot)
    {
        float w = halves == 2 ? 1f + 0.02f : Half;
        var root = new GameObject("Big Chest view");
        root.transform.position = centre;
        root.transform.rotation = turn;

        var p = new Parts { Group = root.transform };
        Body(p, w, halves);
        Merge(root.transform);

        var inner = new GameObject("inner");
        inner.transform.SetParent(root.transform, false);
        p.Group = inner.transform;
        Inner(p, w);
        Merge(inner.transform);

        // hinge line along the back top edge of the body. the pieces get made relative to the chest so they're moved back after
        var lid = new GameObject("lid");
        lid.transform.SetParent(root.transform, false);
        var pieces = new GameObject("pieces");
        pieces.transform.SetParent(lid.transform, false);
        float hingeZ = -Half - 0.02f;
        lid.transform.localPosition = new Vector3(0f, Seam, hingeZ);
        pieces.transform.localPosition = new Vector3(0f, -Seam, -hingeZ);
        p.Group = pieces.transform;
        LidPart(p, w, halves);
        Merge(pieces.transform);

        lidPivot = lid.transform;
        inner.SetActive(false);
        return root;
    }

    // the small one, in your hand and lying on the ground
    public static GameObject MiniModel()
    {
        Textures();
        GameObject m = Model(1, Vector3.zero, Quaternion.identity, out Transform _);
        Object.DestroyImmediate(m.transform.Find("inner").gameObject);
        return m;
    }

    public static void Kill(Chest c)
    {
        if (c.View == null) return;
        foreach (MeshFilter f in c.View.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null) Object.Destroy(f.sharedMesh);
        foreach (Renderer r in c.View.GetComponentsInChildren<Renderer>(true)) if (r.sharedMaterial != null) Object.Destroy(r.sharedMaterial);
        Object.Destroy(c.View);
        c.View = null;
        c.Lid = null;
    }

    // lid swings up while the screen's open, down when it shuts
    public static void Animate(List<Chest> all)
    {
        Chest open = ChestScreen.Current;
        foreach (Chest c in all)
        {
            if (c.View == null || c.Lid == null) continue;
            bool wants = open != null && (open == c || open == c.Mate);
            float to = wants ? OpenAngle : 0f;
            if (Mathf.Approximately(c.LidAngle, to)) continue;
            c.LidAngle = Mathf.MoveTowards(c.LidAngle, to, 200f * Time.unscaledDeltaTime);
            c.Lid.localRotation = Quaternion.Euler(-c.LidAngle, 0f, 0f);
            Transform inner = c.View.transform.Find("inner");
            inner?.gameObject.SetActive(c.LidAngle > 0.01f);
        }
    }

    // shaded by hand from the light around it, the game's voxel light doesn't reach a loose object
    public static void Shade(List<Chest> all, Vector3 me)
    {
        if (Time.unscaledTime < _nextShade) return;
        _nextShade = Time.unscaledTime + 0.5f;
        if (_time == null) _time = Object.FindAnyObjectByType<DayTimeManager>();
        float sun = _time != null ? Mathf.Max(_time.realSun.r, _time.realSun.g, _time.realSun.b) : 1f;
        foreach (Chest c in all)
        {
            if (c.View == null || !c.View.activeSelf) continue;
            float lit = Around(c, sun);
            if (c.Mate != null) lit = Mathf.Max(lit, Around(c.Mate, sun));
            Color tint = Color.white * Mathf.Clamp(lit / 15f, 0.06f, 1f);
            tint.a = 1f;
            bool close = (new Vector3(c.X, c.Y, c.Z) - me).sqrMagnitude < DetailRange * DetailRange;
            foreach (Renderer r in c.View.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial.color = tint;
                if (r.name == "detail") r.enabled = close;
            }
        }
    }

    private static float Around(Chest c, float sun)
    {
        float lit = 0f;
        foreach (Vector3i n in new[] { new Vector3i(0, 1, 0), new Vector3i(0, -1, 0), new Vector3i(0, 0, 1), new Vector3i(1, 0, 0), new Vector3i(0, 0, -1), new Vector3i(-1, 0, 0) })
        {
            Block? b = BSWorld.GetBlock(c.X + n.X, c.Y + n.Y, c.Z + n.Z);
            if (b == null || b.Value.IsSolid) continue;
            BlockLight l = b.Value.light;
            lit = Mathf.Max(lit, l.R, l.G, l.B, l.S * sun);
        }
        return lit;
    }

    // ---------- item icon ----------

    public static Texture2D Icon()
    {
        Textures();
        var t = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                Color c = new(0f, 0f, 0f, 0f);
                if (x >= 1 && x < 31 && y >= 3 && y < 29)
                {
                    // y runs bottom up here, the lid is the top third
                    bool lid = y >= 18;
                    c = (lid ? _lidWood : _wood).GetPixel(x * 2, y * 2);
                    if (y == 3 || y == 28 || x == 1 || x == 30) c = new Color(0.10f, 0.07f, 0.05f);
                    else if (y == 4 || y == 27 || x == 2 || x == 29) c = _iron.GetPixel(x * 2, y * 2);
                    if (y == 17 || y == 18) c = _ironDark.GetPixel(x * 2, y * 2);
                    if (x == 7 || x == 8 || x == 23 || x == 24) c = _iron.GetPixel(x * 2, y * 2);
                    if ((x == 7 || x == 24) && y % 5 == 0) c = _brass.GetPixel(x * 2, y * 2);
                    if (x >= 12 && x < 20 && y >= 9 && y < 18) c = _lock.GetPixel((x - 12) * 8, (y - 9) * 7);
                    if (x >= 13 && x < 19 && y >= 16 && y < 23) c = _brass.GetPixel(x * 2, y * 2);
                    if (x >= 15 && x < 17 && y >= 18 && y < 21) c = new Color(0.86f, 0.1f, 0.16f);
                }
                t.SetPixel(x, y, c);
            }
        t.Apply();
        return t;
    }
}
using UnityEngine;

namespace BlockStoryMod
{
    // no sound files to ship, both are worked out on first use. goes through the game's own ui sound call so its mute works
    public static class ChestSound
    {
        private const int Rate = 22050;
        private static AudioClip _open, _close;

        public static void Open()
        {
            if (_open == null) _open = Make("chest open", 0.5f, Creak(0.02f, 0.42f, 150f, 300f), Click(0f));
            NGUITools.PlaySound(_open, 0.7f);
        }

        public static void Close()
        {
            if (_close == null) _close = Make("chest close", 0.42f, Creak(0f, 0.24f, 280f, 150f), Thump(0.24f), Click(0.26f));
            NGUITools.PlaySound(_close, 0.7f);
        }

        private delegate float Part(float t, System.Random r);

        private static AudioClip Make(string name, float seconds, params Part[] parts)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            var r = new System.Random(n);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, v = 0f;
                foreach (Part p in parts) v += p(t, r);
                data[i] = Mathf.Clamp(v, -1f, 1f);
            }
            AudioClip c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        // a rough saw wave that wanders about, that's what makes it wood and not a beep
        private static Part Creak(float from, float to, float startHz, float endHz)
        {
            float phase = 0f;
            return (t, r) =>
            {
                if (t < from || t > to) return 0f;
                float k = (t - from) / (to - from);
                float hz = Mathf.Lerp(startHz, endHz, k) * (1f + Mathf.Sin(t * 70f) * 0.06f + ((float)r.NextDouble() - 0.5f) * 0.05f);
                phase += hz / Rate;
                float saw = (phase % 1f) * 2f - 1f;
                float flutter = Mathf.Abs(Mathf.Sin(t * 95f));
                return saw * flutter * Mathf.Sin(k * Mathf.PI) * 0.32f;
            };
        }

        private static Part Click(float at) => (t, r) =>
        {
            float d = t - at;
            if (d < 0f || d > 0.03f) return 0f;
            return ((float)r.NextDouble() * 2f - 1f) * (1f - d / 0.03f) * 0.5f + Mathf.Sin(d * 2f * Mathf.PI * 1900f) * (1f - d / 0.03f) * 0.25f;
        };

        private static Part Thump(float at) => (t, r) =>
        {
            float d = t - at;
            if (d < 0f) return 0f;
            float fade = Mathf.Exp(-d * 26f);
            return (Mathf.Sin(d * 2f * Mathf.PI * 95f) * 0.8f + ((float)r.NextDouble() * 2f - 1f) * 0.12f) * fade;
        };
    }
}

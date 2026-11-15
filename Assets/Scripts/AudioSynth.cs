using UnityEngine;

// Synthesized placeholder audio — no asset files. Every clip gets a 5 ms fade in/out.
public static class AudioSynth
{
    const int SR = 44100;
    static System.Random rng = new(42);

    static AudioClip Make(string name, float[] d, bool fade = true)
    {
        if (fade)
        {
            int f = Mathf.Min(SR / 200, d.Length / 2);
            for (int i = 0; i < f; i++) { float g = i / (float)f; d[i] *= g; d[d.Length - 1 - i] *= g; }
        }
        var c = AudioClip.Create(name, d.Length, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }

    static float N() => (float)rng.NextDouble() * 2f - 1f;

    public static AudioClip Blip()
    {
        int n = (int)(SR * 0.09f);
        var d = new float[n]; float ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float f = Mathf.Lerp(880, 1320, t);
            ph += f / SR;
            d[i] = Mathf.Sin(ph * Mathf.PI * 2) * Mathf.Exp(-t * 4f) * 0.5f;
        }
        return Make("Blip", d);
    }

    public static AudioClip Sting()
    {
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
        int step = (int)(SR * 0.12f), len = step * notes.Length + SR / 2;
        var d = new float[len];
        for (int k = 0; k < notes.Length; k++)
        {
            int start = k * step;
            int dur = k == notes.Length - 1 ? SR / 2 : step + SR / 20;
            for (int i = 0; i < dur && start + i < len; i++)
            {
                float t = i / (float)SR;
                float env = Mathf.Min(1, t * 200f) * Mathf.Exp(-t * (k == notes.Length - 1 ? 3.5f : 7f));
                float s = Mathf.Sin(2 * Mathf.PI * notes[k] * t) + 0.3f * Mathf.Sin(4 * Mathf.PI * notes[k] * t);
                d[start + i] += s * env * 0.3f;
            }
        }
        return Make("Sting", d);
    }

    public static AudioClip Click()
    {
        int n = (int)(SR * 0.02f);
        var d = new float[n]; float lp = 0;
        for (int i = 0; i < n; i++)
        {
            lp = Mathf.Lerp(lp, N(), 0.35f);
            d[i] = lp * (1f - i / (float)n) * 0.6f;
        }
        return Make("Click", d);
    }

    public static AudioClip Footstep()
    {
        int n = (int)(SR * 0.07f);
        var d = new float[n]; float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            lp = Mathf.Lerp(lp, N(), 0.12f);
            d[i] = (lp * 0.8f + Mathf.Sin(2 * Mathf.PI * 95 * i / SR) * 0.3f) * Mathf.Exp(-t * 6f) * 0.6f;
        }
        return Make("Footstep", d);
    }

    public static AudioClip Door()
    {
        int n = (int)(SR * 0.22f);
        var d = new float[n]; float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            lp = Mathf.Lerp(lp, N(), 0.08f);
            d[i] = (Mathf.Sin(2 * Mathf.PI * 70 * i / SR) * 0.7f + lp) * Mathf.Exp(-t * 9f) * 0.8f;
        }
        return Make("Door", d);
    }

    public static AudioClip Crash()
    {
        int n = (int)(SR * 0.45f);
        var d = new float[n]; float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            lp = Mathf.Lerp(lp, N(), 0.25f);
            float metal = Mathf.Sin(2 * Mathf.PI * 410 * i / SR) * Mathf.Sin(2 * Mathf.PI * 13 * i / SR) * 0.25f;
            d[i] = (lp + metal) * Mathf.Exp(-t * 7f) * 0.9f;
        }
        return Make("Crash", d);
    }

    public static AudioClip Horn()
    {
        int n = (int)(SR * 0.45f);
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float s = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 392 * t)) * 0.5f + Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 494 * t)) * 0.5f;
            d[i] = s * 0.18f;
        }
        // soften the square waves
        for (int i = 1; i < n; i++) d[i] = Mathf.Lerp(d[i - 1], d[i], 0.25f);
        return Make("Horn", d);
    }

    // Seamless 1 s engine loop: harmonics of 55 Hz (integer cycles) + a little rumble.
    public static AudioClip Engine()
    {
        int n = SR;
        var d = new float[n];
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float s = 0;
            for (int h = 1; h <= 6; h++) s += Mathf.Sin(2 * Mathf.PI * 55 * h * t + h) / (h * 1.3f);
            float firing = 0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 27.5f * 2 * t);
            lp = Mathf.Lerp(lp, N(), 0.05f);
            d[i] = (s * firing * 0.22f + lp * 0.12f);
        }
        return Make("Engine", d, false);
    }

    // City room tone: brown noise with a slow swell, seamless by crossfading the ends.
    public static AudioClip CityAmbience()
    {
        int n = SR * 6;
        var d = new float[n];
        float b = 0;
        for (int i = 0; i < n; i++)
        {
            b = Mathf.Clamp(b + N() * 0.02f, -1, 1) * 0.998f;
            float t = i / (float)SR;
            float swell = 0.75f + 0.25f * Mathf.Sin(2 * Mathf.PI * t / 6f);
            d[i] = b * 0.5f * swell;
        }
        int x = SR / 2;
        for (int i = 0; i < x; i++)
        {
            float g = i / (float)x;
            d[i] = d[i] * g + d[n - x + i] * (1 - g);
        }
        var trimmed = new float[n - x];
        System.Array.Copy(d, 0, trimmed, 0, n - x);
        return Make("CityAmbience", trimmed, false);
    }

    // A slow, warm pad progression (Am – F – C – G), 12 s loop.
    public static AudioClip MusicPad()
    {
        float[][] chords =
        {
            new[] { 220.00f, 261.63f, 329.63f },
            new[] { 174.61f, 220.00f, 261.63f },
            new[] { 130.81f, 196.00f, 261.63f, 329.63f },
            new[] { 196.00f, 246.94f, 293.66f },
        };
        float chordLen = 3f;
        int n = (int)(SR * chordLen * chords.Length);
        var d = new float[n];
        for (int c = 0; c < chords.Length; c++)
        {
            int start = (int)(c * chordLen * SR);
            int dur = (int)((chordLen + 0.8f) * SR);
            for (int i = 0; i < dur; i++)
            {
                int idx = (start + i) % n;
                float t = i / (float)SR;
                float env = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / 0.9f)) * Mathf.SmoothStep(0, 1, Mathf.Clamp01((chordLen + 0.8f - t) / 1.2f));
                float s = 0;
                foreach (float f in chords[c])
                {
                    s += Mathf.Sin(2 * Mathf.PI * f * t) * 0.5f;
                    s += Mathf.Sin(2 * Mathf.PI * f * 1.003f * t + 0.7f) * 0.3f;
                    s += Mathf.Sin(2 * Mathf.PI * f * 0.5f * t) * 0.25f;
                }
                d[idx] += s * env * 0.07f;
            }
        }
        return Make("MusicPad", d, false);
    }
}

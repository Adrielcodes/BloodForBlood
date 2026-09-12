using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Synthesizes every sound in the game as 16-bit mono WAVs under Assets/Resources/Audio (loaded at
// runtime via Sfx / LoopingAudio with Resources.Load). No audio assets exist in this project, so
// music, ambience and every one-shot are built from oscillators, filtered noise and envelopes.
// Generated once, reused by path — delete a .wav to regenerate it.
internal static class ProceduralAudio
{
    private const int Rate = 44100;
    private const string Dir = "Assets/Resources/Audio";

    public static void GenerateAll()
    {
        Save("MenuMusic", MenuMusic(64f));
        Save("Ambient", Ambient(48f));
        Save("Heartbeat", Heartbeat());
        Save("DoorSlam", DoorSlam());
        Save("DoorBreak", DoorBreak());
        Save("Hit", Hit());
        Save("Vault", Vault());
        Save("BeaconActivate", BeaconActivate());
        Save("SkillCheckWarn", Blip(1200f, 0.12f, 0.35f));
        Save("SkillCheckSuccess", Success());
        Save("SkillCheckFail", Fail());
        Save("UIClick", Blip(700f, 0.05f, 0.3f));
        Save("Stun", Stun());
    }

    // ------------------------------------------------------------------ music / ambience

    private static float[] MenuMusic(float seconds)
    {
        int n = (int)(seconds * Rate);
        var s = new float[n];
        var rng = new System.Random(7);
        float[] scale = { 220f, 261.63f, 293.66f, 311.13f, 349.23f, 392f, 415.3f, 440f };
        var bells = new List<(float t, float f)>();
        for (float t = 4f; t < seconds - 4f; t += 5f + (float)rng.NextDouble() * 6f) bells.Add((t, scale[rng.Next(scale.Length)]));

        float pad = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float drone = 0.18f * Mathf.Sin(TwoPi * 55f * t + 0.5f * Mathf.Sin(TwoPi * 0.11f * t))
                        + 0.14f * Mathf.Sin(TwoPi * 82.41f * t * (1f + 0.002f * Mathf.Sin(TwoPi * 0.07f * t)))
                        + 0.10f * Mathf.Sin(TwoPi * 110f * t)
                        + 0.05f * Mathf.Sin(TwoPi * 164.8f * t + Mathf.Sin(TwoPi * 0.05f * t));
            float breath = 0.7f + 0.3f * Mathf.Sin(TwoPi * 0.05f * t);
            pad += 0.02f * (White(rng) - pad);
            float padOut = pad * 0.6f * (0.5f + 0.5f * Mathf.Sin(TwoPi * 0.03f * t + 1f));
            float bell = 0f;
            foreach (var (bt, bf) in bells)
            {
                float dt = t - bt;
                if (dt < 0f || dt > 6f) continue;
                bell += 0.12f * Mathf.Exp(-dt * 1.1f) * Mathf.Sin(TwoPi * bf * dt) * (1f + 0.3f * Mathf.Sin(TwoPi * 3f * dt));
            }
            s[i] = Soft(drone * breath + padOut + bell);
        }
        Fade(s, 3f, 3f);
        return s;
    }

    private static float[] Ambient(float seconds)
    {
        int n = (int)(seconds * Rate);
        var s = new float[n];
        var rng = new System.Random(11);
        var creaks = new List<float>();
        for (float t = 3f; t < seconds - 3f; t += 8f + (float)rng.NextDouble() * 7f) creaks.Add(t);
        var drips = new List<(float t, float f)>();
        for (float t = 1.5f; t < seconds - 1f; t += 4f + (float)rng.NextDouble() * 5f) drips.Add((t, 1600f + (float)rng.NextDouble() * 1200f));

        float wind = 0f, rumble = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            wind += 0.004f * (White(rng) - wind);
            rumble += 0.0012f * (White(rng) - rumble);
            float gust = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(TwoPi * 0.045f * t + 0.8f * Mathf.Sin(TwoPi * 0.013f * t)));
            float v = wind * 1.6f * gust + rumble * 3f + 0.03f * Mathf.Sin(TwoPi * 41f * t) * (0.6f + 0.4f * Mathf.Sin(TwoPi * 0.2f * t));
            foreach (float ct in creaks)
            {
                float dt = t - ct;
                if (dt < 0f || dt > 0.7f) continue;
                float f = 190f - 80f * dt;
                v += 0.07f * Mathf.Sin(TwoPi * f * dt + 2f * Mathf.Sin(TwoPi * 27f * dt)) * Mathf.Sin(Mathf.PI * dt / 0.7f);
            }
            foreach (var (dt0, df) in drips)
            {
                float dt = t - dt0;
                if (dt < 0f || dt > 0.6f) continue;
                v += 0.05f * Mathf.Exp(-dt * 60f) * Mathf.Sin(TwoPi * df * dt) + 0.015f * (dt > 0.18f ? Mathf.Exp(-(dt - 0.18f) * 60f) * Mathf.Sin(TwoPi * df * 0.8f * dt) : 0f);
            }
            s[i] = Soft(v);
        }
        Fade(s, 2f, 2f);
        return s;
    }

    // ------------------------------------------------------------------ one-shots

    private static float[] Heartbeat()
    {
        float Thump(float t) => t < 0f ? 0f : Mathf.Exp(-t * 22f) * Mathf.Sin(TwoPi * 52f * t * (1f + 1.5f * Mathf.Exp(-t * 40f)));
        return Gen(0.85f, t => 0.9f * Thump(t) + 0.55f * Thump(t - 0.22f));
    }

    private static float[] DoorSlam()
    {
        var rng = new System.Random(5);
        float lp = 0f;
        return Gen(0.4f, t =>
        {
            lp += 0.05f * (White(rng) - lp);
            float crack = White(rng) * Mathf.Exp(-t * 120f) * 0.6f;
            float body = lp * 5f * Mathf.Exp(-t * 14f) * 0.7f;
            float thump = 0.8f * Mathf.Exp(-t * 12f) * Mathf.Sin(TwoPi * 65f * t);
            return crack + body + thump;
        });
    }

    private static float[] DoorBreak()
    {
        var rng = new System.Random(6);
        float lp = 0f;
        return Gen(0.65f, t =>
        {
            lp += 0.06f * (White(rng) - lp);
            float cracks = 0f;
            foreach (float c in new[] { 0f, 0.08f, 0.2f, 0.31f })
                if (t >= c) cracks += White(rng) * Mathf.Exp(-(t - c) * 90f) * 0.5f;
            float body = lp * 5f * Mathf.Exp(-t * 8f) * 0.6f;
            float wood = 0.25f * Mathf.Exp(-t * 9f) * Mathf.Sin(TwoPi * 310f * t) + 0.5f * Mathf.Exp(-t * 10f) * Mathf.Sin(TwoPi * 58f * t);
            return cracks + body + wood;
        });
    }

    private static float[] Hit()
    {
        var rng = new System.Random(8);
        float lpHi = 0f, lpLo = 0f;
        return Gen(0.3f, t =>
        {
            float w = White(rng);
            lpHi += 0.3f * (w - lpHi);
            lpLo += 0.03f * (w - lpLo);
            float band = (lpHi - lpLo) * Mathf.Exp(-t * 40f) * 1.2f;
            float thump = 0.6f * Mathf.Exp(-t * 25f) * Mathf.Sin(TwoPi * 90f * t * (1f + Mathf.Exp(-t * 50f)));
            return band + thump;
        });
    }

    private static float[] Vault()
    {
        var rng = new System.Random(9);
        float lp = 0f;
        return Gen(0.45f, t =>
        {
            float p = t / 0.45f;
            float coeff = 0.02f + 0.3f * Mathf.Sin(Mathf.PI * p);
            lp += coeff * (White(rng) - lp);
            return lp * 2.2f * Mathf.Sin(Mathf.PI * p);
        });
    }

    private static float[] BeaconActivate()
    {
        var rng = new System.Random(10);
        float lp = 0f;
        float[] notes = { 220f, 277.18f, 329.63f, 440f, 554.37f, 659.25f };
        return Gen(3.2f, t =>
        {
            float v = 0f;
            for (int k = 0; k < notes.Length; k++)
            {
                float on = k * 0.12f;
                float dt = t - on;
                if (dt < 0f) continue;
                float env = Mathf.Min(1f, dt / 0.35f) * Mathf.Exp(-dt * 0.9f);
                v += (0.22f - k * 0.02f) * env * Mathf.Sin(TwoPi * notes[k] * dt * (1f + 0.015f * dt));
            }
            lp += 0.3f * (White(rng) - lp);
            v += lp * 0.5f * Mathf.Min(1f, t / 0.8f) * Mathf.Exp(-t * 1.2f);
            return v;
        });
    }

    private static float[] Blip(float freq, float len, float amp) =>
        Gen(len, t => amp * Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-t * 30f) * Mathf.Sin(TwoPi * freq * t));

    private static float[] Success() => Gen(0.22f, t =>
    {
        float a = t < 0.1f ? 0.35f * Mathf.Exp(-t * 25f) * Mathf.Sin(TwoPi * 880f * t) : 0f;
        float b = t >= 0.09f ? 0.35f * Mathf.Exp(-(t - 0.09f) * 22f) * Mathf.Sin(TwoPi * 1318f * (t - 0.09f)) : 0f;
        return a + b;
    });

    private static float[] Fail()
    {
        var rng = new System.Random(12);
        return Gen(0.35f, t => 0.35f * Mathf.Exp(-t * 6f) * Mathf.Sign(Mathf.Sin(TwoPi * 90f * t)) + 0.08f * White(rng) * Mathf.Exp(-t * 10f));
    }

    private static float[] Stun()
    {
        var rng = new System.Random(13);
        return Gen(0.5f, t =>
        {
            float f = 150f - 70f * (t / 0.5f);
            return 0.45f * Mathf.Exp(-t * 5f) * Mathf.Sign(Mathf.Sin(TwoPi * f * t)) * (0.6f + 0.4f * Mathf.Sin(TwoPi * 13f * t)) + 0.1f * White(rng) * Mathf.Exp(-t * 8f);
        });
    }

    // ------------------------------------------------------------------ helpers

    private const float TwoPi = Mathf.PI * 2f;

    private static float White(System.Random rng) => (float)rng.NextDouble() * 2f - 1f;

    private static float Soft(float v) => (float)Math.Tanh(v);

    private static float[] Gen(float seconds, Func<float, float> f)
    {
        int n = (int)(seconds * Rate);
        var s = new float[n];
        for (int i = 0; i < n; i++) s[i] = Soft(f((float)i / Rate));
        Fade(s, 0.002f, 0.01f);
        return s;
    }

    private static void Fade(float[] s, float inSec, float outSec)
    {
        int nIn = (int)(inSec * Rate), nOut = (int)(outSec * Rate);
        for (int i = 0; i < nIn && i < s.Length; i++) s[i] *= (float)i / nIn;
        for (int i = 0; i < nOut && i < s.Length; i++) s[s.Length - 1 - i] *= (float)i / nOut;
    }

    private static void Save(string name, float[] samples)
    {
        string path = $"{Dir}/{name}.wav";
        if (File.Exists(path)) return;

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Resources", "Audio");

        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            int dataBytes = samples.Length * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataBytes);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(Rate);
            w.Write(Rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(dataBytes);
            foreach (float v in samples) w.Write((short)(Mathf.Clamp(v, -1f, 1f) * 32767f));
            File.WriteAllBytes(path, ms.ToArray());
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }
}

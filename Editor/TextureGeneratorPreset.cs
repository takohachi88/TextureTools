#if UNITY_EDITOR
using System;
using UnityEngine;

namespace TextureTools.Editor
{
    public enum TextureGenerationMode { Noise, MultipleGradients, Curves, ChannelPacking }
    public enum TextureImageOutput { PNG, EXR }

    [Serializable]
    public sealed class TextureGeneratorGradientSettings
    {
        public Vector2Int size = new Vector2Int(256, 1);
        [GradientUsage(true)] public Gradient[] gradients = { new Gradient() };
        public AnimationCurve red = AnimationCurve.Linear(0, 0, 1, 1);
        public AnimationCurve green = AnimationCurve.Linear(0, 0, 1, 1);
        public AnimationCurve blue = AnimationCurve.Linear(0, 0, 1, 1);
        public AnimationCurve alpha = AnimationCurve.Linear(0, 1, 1, 1);
    }

    [Serializable]
    public sealed class TextureGeneratorTone
    {
        public bool enabled;
        public float contrastR = 1, contrastG = 1, contrastB = 1, contrastA = 1;
        public AnimationCurve rgb = AnimationCurve.Linear(0, 0, 1, 1);
        public AnimationCurve red = AnimationCurve.Linear(0, 0, 1, 1);
        public AnimationCurve green = AnimationCurve.Linear(0, 0, 1, 1);
        public AnimationCurve blue = AnimationCurve.Linear(0, 0, 1, 1);
        public AnimationCurve alpha = AnimationCurve.Linear(0, 0, 1, 1);
        private static float Curve(AnimationCurve curve, float value, bool hdr)
        {
            if (curve == null || curve.length == 0) return value;
            if (hdr && curve.length == 2)
            {
                Keyframe first = curve[0], last = curve[1];
                if (first.time == 0 && first.value == 0 && first.outTangent == 1 &&
                    last.time == 1 && last.value == 1 && last.inTangent == 1 &&
                    first.weightedMode == WeightedMode.None && last.weightedMode == WeightedMode.None)
                    return value;
            }
            return curve.Evaluate(value);
        }
        private static float Contrast(float value, float contrast, bool hdr)
        {
            float result = contrast == 1 ? value : (value - 0.5f) * contrast + 0.5f;
            return hdr ? result : Mathf.Clamp01(result);
        }
        public Color[] Apply(Color[] pixels, bool hdr = false)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                Color p = pixels[i];
                p.r = Contrast(p.r, contrastR, hdr); p.g = Contrast(p.g, contrastG, hdr);
                p.b = Contrast(p.b, contrastB, hdr); p.a = Contrast(p.a, contrastA, hdr);
                if (enabled)
                {
                    // LDR keeps the existing per-stage clamp; HDR curves retain their full range.
                    float Limit(float v) => hdr ? v : Mathf.Clamp01(v);
                    p = new Color(Limit(Curve(red, Limit(Curve(rgb, p.r, hdr)), hdr)),
                        Limit(Curve(green, Limit(Curve(rgb, p.g, hdr)), hdr)),
                        Limit(Curve(blue, Limit(Curve(rgb, p.b, hdr)), hdr)), Limit(Curve(alpha, p.a, hdr)));
                }
                pixels[i] = p;
            }
            return pixels;
        }
        public Color32[] Apply(Color32[] pixels)
        {
            if (!enabled && contrastR == 1 && contrastG == 1 && contrastB == 1 && contrastA == 1) return pixels;
            Color[] values = Apply(Array.ConvertAll(pixels, p => (Color)p));
            for (int i = 0; i < pixels.Length; i++) pixels[i] = values[i];
            return pixels;
        }
    }

    [Serializable]
    public sealed class TextureGeneratorRecipe
    {
        public TextureGenerationMode mode;
        public TextureImageOutput output;
        public bool generate3D;
        public Vector2Int size = new Vector2Int(512, 512);
        public Vector3Int volumeSize = new Vector3Int(64, 64, 64);
        public TextureGenerationSettings noise = new TextureGenerationSettings();
        public TextureGeneratorGradientSettings gradient = new TextureGeneratorGradientSettings();
        public TextureGeneratorTone tone = new TextureGeneratorTone();
        public TextureGeneratorChannelPacking packing = new TextureGeneratorChannelPacking();
        public bool IsVolume => mode == TextureGenerationMode.Noise && generate3D;
        public bool IsHDR => output == TextureImageOutput.EXR;
        public Color[] ApplyOutput(Color[] pixels)
        {
            if (mode == TextureGenerationMode.Noise) return tone.Apply(pixels, IsHDR);
            // Gradient modes output their authored values directly. PNG alone needs LDR clamping.
            if (!IsHDR)
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color p = pixels[i];
                    pixels[i] = new Color(Mathf.Clamp01(p.r), Mathf.Clamp01(p.g), Mathf.Clamp01(p.b), Mathf.Clamp01(p.a));
                }
            return pixels;
        }
        public TextureGeneratorRecipe Clone() => JsonUtility.FromJson<TextureGeneratorRecipe>(JsonUtility.ToJson(this));
        public void Normalize()
        {
            size.x = Mathf.Clamp(size.x, 16, 4096); size.y = Mathf.Clamp(size.y, 16, 4096);
            volumeSize.x = Mathf.Clamp(volumeSize.x, 4, 256); volumeSize.y = Mathf.Clamp(volumeSize.y, 4, 256);
            volumeSize.z = Mathf.Clamp(volumeSize.z, 4, 256);
            if (noise == null) noise = new TextureGenerationSettings();
            if (packing == null) packing = new TextureGeneratorChannelPacking();
            if (gradient == null) gradient = new TextureGeneratorGradientSettings();
            gradient.size.x = Mathf.Clamp(gradient.size.x, 1, 4096); gradient.size.y = Mathf.Clamp(gradient.size.y, 1, 4096);
            if (tone == null) tone = new TextureGeneratorTone();
            tone.contrastR = Mathf.Clamp(tone.contrastR, 0, 4); tone.contrastG = Mathf.Clamp(tone.contrastG, 0, 4);
            tone.contrastB = Mathf.Clamp(tone.contrastB, 0, 4); tone.contrastA = Mathf.Clamp(tone.contrastA, 0, 4);
        }
        public FilterMode Filter
        {
            get
            {
                if (mode != TextureGenerationMode.Noise) return FilterMode.Bilinear;
                if (noise.pattern == GeneratedPattern.Block || noise.pattern == GeneratedPattern.RectangleLayers) return FilterMode.Point;
                GeneratedChannel[] channels = { noise.red, noise.green, noise.blue, noise.alpha };
                foreach (GeneratedChannel c in channels)
                    if (c == GeneratedChannel.CenterU || c == GeneratedChannel.CenterV || c == GeneratedChannel.CenterW)
                        return FilterMode.Point;
                return FilterMode.Bilinear;
            }
        }
    }

    public sealed class TextureGeneratorPreset : ScriptableObject
    {
        public TextureGeneratorRecipe recipe = new TextureGeneratorRecipe();
    }
}
#endif
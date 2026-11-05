#if UNITY_EDITOR
using System;
using UnityEngine;

namespace TextureTools.Editor
{
    public enum GeneratedPattern { Perlin, Value, Fractal, Billow, Ridged, White, Voronoi, Hexagon, Triangle, Block, RectangleLayers }
    public enum GeneratedChannel { Value, U, V, CellId, Zero, One, CenterU, CenterV, W, CenterW }

    [Serializable]
    public sealed class TextureGenerationSettings
    {
        public GeneratedPattern pattern = GeneratedPattern.Fractal;
        public int columns = 8, rows = 8, layers = 8, seed = 1, octaves = 4;
        public float persistence = 0.5f;
        public int rectangleCount = 256;
        public Vector2 rectangleSize = new Vector2(0.03f, 0.25f);
        public bool rectangleEqualize = true;
        public float rectangleStrength = 0.5f, rectangleBackground = 0.5f;
        public bool seamless = true;
        public bool separateSeeds;
        public int redSeed = 1, greenSeed = 1, blueSeed = 1, alphaSeed = 1;
        public TextureGenerationSettings WithSeed(int value)
        {
            var copy = (TextureGenerationSettings)MemberwiseClone();
            copy.seed = value;
            return copy;
        }
        public GeneratedChannel red = GeneratedChannel.Value, green = GeneratedChannel.Value,
            blue = GeneratedChannel.Value, alpha = GeneratedChannel.One;
    }

    public static partial class TextureGeneratorEngine
    {
        private static int Wrap(int n, int period) => (n % period + period) % period;
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0x00ffffff) / 16777216f;
            }
        }

        private static float Lattice(int x, int y, int w, int h, TextureGenerationSettings s, int salt = 0)
            => Hash(s.seamless ? Wrap(x, w) : x, s.seamless ? Wrap(y, h) : y, unchecked(s.seed + salt));

        private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

        private static float Noise(float x, float y, int w, int h, TextureGenerationSettings s, bool value)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            float Corner(int dx, int dy)
            {
                float r = Lattice(ix + dx, iy + dy, w, h, s);
                if (value) return r;
                float angle = r * Mathf.PI * 2;
                return Mathf.Cos(angle) * (fx - dx) + Mathf.Sin(angle) * (fy - dy);
            }
            float n = Mathf.Lerp(Mathf.Lerp(Corner(0, 0), Corner(1, 0), Fade(fx)),
                Mathf.Lerp(Corner(0, 1), Corner(1, 1), Fade(fx)), Fade(fy));
            return value ? n : Mathf.Clamp01(0.5f + n * 0.70710678f);
        }

        // Returns value, local U, local V, and a deterministic cell identifier.
        public static Vector4 Sample(float u, float v, TextureGenerationSettings s)
            => Sample(u, v, s, out _);

        private static Vector4 Sample(float u, float v, TextureGenerationSettings s, out Vector2 center)
        {
            if (s.pattern == GeneratedPattern.RectangleLayers)
            {
                VolumeSample rectangles = new RectangleLayerSampler(s, false).Sample(u, v, 0);
                center = new Vector2(rectangles.center.x, rectangles.center.y);
                return new Vector4(rectangles.value, rectangles.uv.x, rectangles.uv.y, rectangles.id);
            }
            int w = Mathf.Clamp(s.columns, 1, 128), h = Mathf.Clamp(s.rows, 2, 128);
            bool staggered = s.pattern == GeneratedPattern.Hexagon || s.pattern == GeneratedPattern.Triangle;
            if (staggered && s.seamless && (h & 1) != 0) h++;
            float x = u * w, y = v * h;
            center = new Vector2((Mathf.Floor(x) + 0.5f) / w, (Mathf.Floor(y) + 0.5f) / h);
            if (s.pattern == GeneratedPattern.Triangle)
            {
                float q = x - y * 0.5f;
                int i = Mathf.FloorToInt(q), j = Mathf.FloorToInt(y);
                float a = q - i, b = y - j;
                bool upper = a + b > 1;
                if (upper) { a = 1 - a; b = 1 - b; }
                // Canonicalize the skew lattice under the rectangular vertical period.
                int canonicalJ = s.seamless ? Wrap(j, h) : j;
                int canonicalI = s.seamless ? Wrap(i + (j - canonicalJ) / 2, w) : i;
                float id = Hash(canonicalI, canonicalJ, unchecked(s.seed + (upper ? 173 : 0)));
                float centroid = upper ? 2f / 3f : 1f / 3f;
                center = new Vector2((i + centroid + (j + centroid) * 0.5f) / w, (j + centroid) / h);
                center = ResolveCenter(center, s.seamless);
                return new Vector4(Mathf.Min(a, Mathf.Min(b, 1 - a - b)) * 3, a, b, id);
            }
            if (s.pattern == GeneratedPattern.Voronoi || s.pattern == GeneratedPattern.Hexagon)
            {
                bool hex = s.pattern == GeneratedPattern.Hexagon;
                float best = float.MaxValue, localX = 0, localY = 0, id = 0;
                int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
                for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int cx = ix + dx, cy = iy + dy;
                    float px = cx + (hex ? 0.5f * (cy & 1) : Lattice(cx, cy, w, h, s));
                    float py = cy + (hex ? 0 : Lattice(cx, cy, w, h, s, 719));
                    float vx = x - px, vy = (y - py) * (hex ? 0.8660254f : 1);
                    float distance = vx * vx + vy * vy;
                    if (distance >= best) continue;
                    best = distance; localX = vx; localY = vy;
                    center = new Vector2(px / w, py / h);
                    id = Lattice(cx, cy, w, h, s, 1237);
                }
                center = ResolveCenter(center, s.seamless);
                return new Vector4(Mathf.Clamp01(Mathf.Sqrt(best)),
                    Mathf.Clamp01(0.5f + localX / (hex ? 1 : 2)),
                    Mathf.Clamp01(0.5f + localY / (hex ? 1.1547005f : 2)), id);
            }
            float n;
            if (s.pattern == GeneratedPattern.White)
                n = Lattice(Mathf.FloorToInt(x), Mathf.FloorToInt(y), w, h, s);
            else if (s.pattern == GeneratedPattern.Block)
            {
                float total = 0, weight = 1, weights = 0;
                for (int octave = 0; octave < Mathf.Clamp(s.octaves, 1, 8); octave++)
                {
                    int frequency = 1 << octave;
                    total += Lattice(Mathf.FloorToInt(x * frequency), Mathf.FloorToInt(y * frequency),
                        w * frequency, h * frequency, s) * weight;
                    weights += weight; weight *= Mathf.Clamp01(s.persistence);
                }
                n = total / weights;
            }
            else if (s.pattern == GeneratedPattern.Perlin || s.pattern == GeneratedPattern.Value)
                n = Noise(x, y, w, h, s, s.pattern == GeneratedPattern.Value);
            else
            {
                float total = 0, weight = 1, weights = 0;
                for (int octave = 0; octave < Mathf.Clamp(s.octaves, 1, 8); octave++)
                {
                    int frequency = 1 << octave;
                    float sample = Noise(x * frequency, y * frequency, w * frequency, h * frequency, s, false);
                    if (s.pattern == GeneratedPattern.Billow) sample = Mathf.Abs(sample * 2 - 1);
                    if (s.pattern == GeneratedPattern.Ridged) sample = 1 - Mathf.Abs(sample * 2 - 1);
                    total += sample * weight; weights += weight;
                    weight *= Mathf.Clamp01(s.persistence);
                }
                n = total / weights;
            }
            return new Vector4(n, x - Mathf.Floor(x), y - Mathf.Floor(y),
                Lattice(Mathf.FloorToInt(x), Mathf.FloorToInt(y), w, h, s));
        }

        private static float Channel(Vector4 sample, GeneratedChannel channel)
        {
            switch (channel)
            {
                case GeneratedChannel.U: return sample.y;
                case GeneratedChannel.V: return sample.z;
                case GeneratedChannel.CellId: return sample.w;
                case GeneratedChannel.W:
                case GeneratedChannel.CenterW:
                case GeneratedChannel.Zero: return 0;
                case GeneratedChannel.One: return 1;
                default: return sample.x;
            }
        }

        private static Vector2 ResolveCenter(Vector2 center, bool seamless)
            => seamless ? new Vector2(center.x - Mathf.Floor(center.x), center.y - Mathf.Floor(center.y))
                : new Vector2(Mathf.Clamp01(center.x), Mathf.Clamp01(center.y));

        private static float OutputChannel(Vector4 sample, Vector2 center, GeneratedChannel channel)
            => channel == GeneratedChannel.CenterU ? center.x : channel == GeneratedChannel.CenterV ? center.y : Channel(sample, channel);

        public static Texture2D Generate(int width, int height, TextureGenerationSettings settings,
            Func<float, bool> cancel = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (width < 1 || height < 1 || width > 4096 || height > 4096)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (settings.pattern == GeneratedPattern.RectangleLayers)
                return Create(width, height, GenerateRectanglePixels(width, height, 1, settings, false, cancel), true, settings.seamless);
            var pixels = new Color32[width * height];
            var channels = new[] { settings.red, settings.green, settings.blue, settings.alpha };
            var seeds = new[] { settings.redSeed, settings.greenSeed, settings.blueSeed, settings.alphaSeed };
            var channelSettings = new TextureGenerationSettings[4];
            for (int c = 0; c < 4; c++)
                channelSettings[c] = settings.separateSeeds ? settings.WithSeed(seeds[c]) : settings;
            for (int y = 0; y < height; y++)
            {
                if (y % 16 == 0 && cancel != null && cancel((float)y / height))
                    throw new OperationCanceledException();
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width, v = (y + 0.5f) / height;
                    Vector4 sample = Sample(u, v, channelSettings[0], out Vector2 center);
                    Color color = Color.clear;
                    for (int c = 0; c < 4; c++)
                    {
                        Vector4 current = sample;
                        Vector2 currentCenter = center;
                        if (c > 0 && channelSettings[c].seed != channelSettings[0].seed &&
                            channels[c] != GeneratedChannel.Zero && channels[c] != GeneratedChannel.One)
                            current = Sample(u, v, channelSettings[c], out currentCenter);
                        color[c] = OutputChannel(current, currentCenter, channels[c]);
                    }
                    pixels[y * width + x] = color;
                }
            }
            return Create(width, height, pixels, true, settings.seamless);
        }

        public static Color32[] OffsetPixels(Color32[] source, int width, int height, bool horizontal, bool vertical)
        {
            if (source == null || width < 1 || height < 1 || (long)width * height != source.Length)
                throw new ArgumentException("Invalid source dimensions.");
            var output = new Color32[source.Length];
            int dx = horizontal ? width / 2 : 0, dy = vertical ? height / 2 : 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                output[Wrap(y + dy, height) * width + Wrap(x + dx, width)] = source[y * width + x];
            return output;
        }

        public static Texture2D Offset(Texture2D source, bool horizontal, bool vertical,
            bool blendSeams = false, float blendWidth = 0.15f, float blendHeight = 0.15f, int maximumSize = 0,
            float solidWidth = 0f, float solidHeight = 0f)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            float scale = maximumSize > 0 ? Mathf.Min(1, (float)maximumSize / Mathf.Max(source.width, source.height)) : 1;
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            bool linear = !source.isDataSRGB;
            Color32[] pixels;
            if (width == source.width && height == source.height && source.isReadable &&
                (source.format == TextureFormat.RGBA32 || source.format == TextureFormat.RGB24))
                pixels = source.GetPixels32();
            else
            {
                var previous = RenderTexture.active;
                bool previousWrite = GL.sRGBWrite;
                var temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                    linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
                Texture2D readable = null;
                try
                {
                    GL.sRGBWrite = !linear && QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(source, temporary);
                    RenderTexture.active = temporary;
                    readable = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
                    readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    readable.Apply();
                    pixels = readable.GetPixels32();
                }
                finally
                {
                    GL.sRGBWrite = previousWrite;
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(temporary);
                    if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
                }
            }
            Color32[] output = OffsetPixels(pixels, width, height, horizontal, vertical);
            if (blendSeams)
            {
                int w = width, h = height;
                int dx = horizontal ? w / 2 : 0, dy = vertical ? h / 2 : 0;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float ax = horizontal ? SeamWeight(x, dx, w, solidWidth, blendWidth) : 0;
                    float ay = vertical ? SeamWeight(y, dy, h, solidHeight, blendHeight) : 0;
                    int sx = Wrap(x - dx, w), sy = Wrap(y - dy, h);
                    // Blend four phases so a repair in one axis cannot reintroduce the other seam.
                    Color bottom = Color.Lerp(pixels[sy * w + sx], pixels[sy * w + x], ax);
                    Color top = Color.Lerp(pixels[y * w + sx], pixels[y * w + x], ax);
                    output[y * w + x] = Color.Lerp(bottom, top, ay);
                }
            }
            return Create(width, height, output, linear, true);
        }

        private static float SeamWeight(int pixel, int seam, int size, float solidWidth, float fadeWidth)
        {
            if (pixel == 0 || pixel == size - 1) return 0;
            // Fully cover both pixels next to the relocated discontinuity.
            float distance = Mathf.Max(0, Mathf.Abs(pixel - (seam - 0.5f)) - 0.5f);
            float available = Mathf.Max(0, Mathf.Min(seam - 1, size - seam - 1));
            float solidRadius = Mathf.Min(Mathf.Clamp01(solidWidth) * 0.5f * size, available);
            float fadeRadius = Mathf.Min(Mathf.Clamp(fadeWidth, 0f, 0.5f) * size, available - solidRadius);
            if (distance <= solidRadius) return 1;
            if (fadeRadius <= 0) return 0;
            float t = Mathf.Clamp01((distance - solidRadius) / fadeRadius);
            return 1 - Fade(t);
        }

        private static Texture2D Create(int w, int h, Color32[] pixels, bool linear, bool repeat)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false, linear)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
#endif

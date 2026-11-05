#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace TextureTools.Editor
{
    public static partial class TextureGeneratorEngine
    {
        public const int MaximumVolumeDimension = 256;

        private struct VolumeSample
        {
            public float value, id;
            public Vector3 uv, center;
        }

        private static float VolumeLattice(int x, int y, int z, int width, int height, int depth,
            TextureGenerationSettings settings, int salt = 0)
        {
            if (settings.seamless)
            {
                x = Wrap(x, width); y = Wrap(y, height); z = Wrap(z, depth);
            }
            return Hash(x, y, unchecked(settings.seed + salt + z * 73856093));
        }

        private static float VolumeNoise(float x, float y, float z, int width, int height, int depth,
            TextureGenerationSettings settings, bool valueNoise)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y), iz = Mathf.FloorToInt(z);
            float fx = x - ix, fy = y - iy, fz = z - iz;
            float Corner(int dx, int dy, int dz)
            {
                float random = VolumeLattice(ix + dx, iy + dy, iz + dz, width, height, depth, settings);
                if (valueNoise) return random;
                int gradient = (int)(random * 16);
                float a = gradient < 8 ? fx - dx : fy - dy;
                float b = gradient < 4 ? fy - dy : (gradient == 12 || gradient == 14 ? fx - dx : fz - dz);
                return ((gradient & 1) == 0 ? a : -a) + ((gradient & 2) == 0 ? b : -b);
            }
            float tx = Fade(fx), ty = Fade(fy), tz = Fade(fz);
            float lower = Mathf.Lerp(Mathf.Lerp(Corner(0, 0, 0), Corner(1, 0, 0), tx),
                Mathf.Lerp(Corner(0, 1, 0), Corner(1, 1, 0), tx), ty);
            float upper = Mathf.Lerp(Mathf.Lerp(Corner(0, 0, 1), Corner(1, 0, 1), tx),
                Mathf.Lerp(Corner(0, 1, 1), Corner(1, 1, 1), tx), ty);
            float result = Mathf.Lerp(lower, upper, tz);
            return valueNoise ? result : Mathf.Clamp01(0.5f + result * 0.5f);
        }

        private static VolumeSample SampleVolume(float u, float v, float w, TextureGenerationSettings settings)
        {
            // Hexagons and triangles are prisms, preserving the existing 2D tiling definition.
            if (settings.pattern == GeneratedPattern.Hexagon || settings.pattern == GeneratedPattern.Triangle)
            {
                Vector4 planar = Sample(u, v, settings, out Vector2 planarCenter);
                return new VolumeSample
                {
                    value = planar.x, id = planar.w,
                    uv = new Vector3(planar.y, planar.z, w - Mathf.Floor(w)),
                    center = new Vector3(planarCenter.x, planarCenter.y, 0.5f)
                };
            }
            int columns = Mathf.Clamp(settings.columns, 1, 128);
            int rows = Mathf.Clamp(settings.rows, 2, 128);
            int layers = Mathf.Clamp(settings.layers, 1, 128);
            float x = u * columns, y = v * rows, z = w * layers;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y), iz = Mathf.FloorToInt(z);
            var result = new VolumeSample
            {
                uv = new Vector3(x - ix, y - iy, z - iz),
                center = new Vector3((ix + 0.5f) / columns, (iy + 0.5f) / rows, (iz + 0.5f) / layers),
                id = VolumeLattice(ix, iy, iz, columns, rows, layers, settings)
            };
            if (settings.pattern == GeneratedPattern.Voronoi)
            {
                float best = float.MaxValue;
                for (int dz = -2; dz <= 2; dz++)
                for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int cx = ix + dx, cy = iy + dy, cz = iz + dz;
                    float px = cx + VolumeLattice(cx, cy, cz, columns, rows, layers, settings);
                    float py = cy + VolumeLattice(cx, cy, cz, columns, rows, layers, settings, 719);
                    float pz = cz + VolumeLattice(cx, cy, cz, columns, rows, layers, settings, 1543);
                    var delta = new Vector3(x - px, y - py, z - pz);
                    float distance = delta.sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    result.uv = new Vector3(Mathf.Clamp01(0.5f + delta.x * 0.5f),
                        Mathf.Clamp01(0.5f + delta.y * 0.5f), Mathf.Clamp01(0.5f + delta.z * 0.5f));
                    result.center = new Vector3(px / columns, py / rows, pz / layers);
                    result.id = VolumeLattice(cx, cy, cz, columns, rows, layers, settings, 1237);
                }
                result.value = Mathf.Clamp01(Mathf.Sqrt(best));
            }
            else if (settings.pattern == GeneratedPattern.White)
                result.value = result.id;
            else if (settings.pattern == GeneratedPattern.Block)
            {
                float total = 0, weight = 1, weights = 0;
                for (int octave = 0; octave < Mathf.Clamp(settings.octaves, 1, 8); octave++)
                {
                    int frequency = 1 << octave;
                    total += VolumeLattice(Mathf.FloorToInt(x * frequency), Mathf.FloorToInt(y * frequency),
                        Mathf.FloorToInt(z * frequency), columns * frequency, rows * frequency, layers * frequency, settings) * weight;
                    weights += weight; weight *= Mathf.Clamp01(settings.persistence);
                }
                result.value = total / weights;
            }
            else if (settings.pattern == GeneratedPattern.Perlin || settings.pattern == GeneratedPattern.Value)
                result.value = VolumeNoise(x, y, z, columns, rows, layers, settings, settings.pattern == GeneratedPattern.Value);
            else
            {
                float total = 0, weight = 1, weights = 0;
                for (int octave = 0; octave < Mathf.Clamp(settings.octaves, 1, 8); octave++)
                {
                    int frequency = 1 << octave;
                    float sample = VolumeNoise(x * frequency, y * frequency, z * frequency,
                        columns * frequency, rows * frequency, layers * frequency, settings, false);
                    if (settings.pattern == GeneratedPattern.Billow) sample = Mathf.Abs(sample * 2 - 1);
                    if (settings.pattern == GeneratedPattern.Ridged) sample = 1 - Mathf.Abs(sample * 2 - 1);
                    total += sample * weight; weights += weight;
                    weight *= Mathf.Clamp01(settings.persistence);
                }
                result.value = total / weights;
            }
            Vector3 center = result.center;
            result.center = settings.seamless
                ? new Vector3(center.x - Mathf.Floor(center.x), center.y - Mathf.Floor(center.y), center.z - Mathf.Floor(center.z))
                : new Vector3(Mathf.Clamp01(center.x), Mathf.Clamp01(center.y), Mathf.Clamp01(center.z));
            return result;
        }

        private static float VolumeChannel(VolumeSample sample, GeneratedChannel channel)
        {
            switch (channel)
            {
                case GeneratedChannel.U: return sample.uv.x;
                case GeneratedChannel.V: return sample.uv.y;
                case GeneratedChannel.W: return sample.uv.z;
                case GeneratedChannel.CenterU: return sample.center.x;
                case GeneratedChannel.CenterV: return sample.center.y;
                case GeneratedChannel.CenterW: return sample.center.z;
                case GeneratedChannel.CellId: return sample.id;
                case GeneratedChannel.Zero: return 0;
                case GeneratedChannel.One: return 1;
                default: return sample.value;
            }
        }

        private sealed class VolumeSampler
        {
            private readonly TextureGenerationSettings[] settings = new TextureGenerationSettings[4];
            private readonly GeneratedChannel[] channels;
            public VolumeSampler(TextureGenerationSettings source)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                channels = new[] { source.red, source.green, source.blue, source.alpha };
                for (int c = 0; c < 4; c++)
                    settings[c] = source.WithChannel(c);
            }
            public Color32 Sample(float u, float v, float w)
            {
                VolumeSample first = SampleVolume(u, v, w, settings[0]);
                Color result = Color.clear;
                for (int c = 0; c < 4; c++)
                {
                    if (channels[c] == GeneratedChannel.Zero) { result[c] = 0; continue; }
                    if (channels[c] == GeneratedChannel.One) { result[c] = 1; continue; }
                    VolumeSample sample = c == 0 || SameSampling(settings[c], settings[0])
                        ? first : SampleVolume(u, v, w, settings[c]);
                    result[c] = VolumeChannel(sample, channels[c]);
                }
                return result;
            }
        }

        private static void ValidateVolumeDimensions(int width, int height, int depth)
        {
            if (width < 1 || height < 1 || depth < 1 || width > MaximumVolumeDimension ||
                height > MaximumVolumeDimension || depth > MaximumVolumeDimension)
                throw new ArgumentOutOfRangeException(nameof(width), "Each volume dimension must be between 1 and 256.");
        }

        public static Texture2D Generate3DSlice(int width, int height, int depth, int slice,
            TextureGenerationSettings settings)
        {
            ValidateVolumeDimensions(width, height, depth);
            if (slice < 0 || slice >= depth) throw new ArgumentOutOfRangeException(nameof(slice));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.pattern == GeneratedPattern.RectangleLayers)
                return Create(width, height, GenerateRectanglePixels(width, height, depth, settings, true, null, slice), true, settings.seamless);
            var sampler = new VolumeSampler(settings);
            var pixels = new Color32[width * height];
            float w = (slice + 0.5f) / depth;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = sampler.Sample((x + 0.5f) / width, (y + 0.5f) / height, w);
            return Create(width, height, pixels, true, settings.seamless);
        }

        public static Color32[] Generate3DPixels(int width, int height, int depth, TextureGenerationSettings settings,
            Func<float, bool> cancel = null)
        {
            ValidateVolumeDimensions(width, height, depth);
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.pattern == GeneratedPattern.RectangleLayers)
                return GenerateRectanglePixels(width, height, depth, settings, true, cancel);
            var sampler = new VolumeSampler(settings);
            var pixels = new Color32[width * height * depth];
            for (int z = 0; z < depth; z++)
            for (int y = 0; y < height; y++)
            {
                if (y % 8 == 0 && cancel != null && cancel((float)(z * height + y) / (depth * height)))
                    throw new OperationCanceledException();
                for (int x = 0; x < width; x++)
                    pixels[(z * height + y) * width + x] = sampler.Sample(
                        (x + 0.5f) / width, (y + 0.5f) / height, (z + 0.5f) / depth);
            }
            return pixels;
        }

        public static Texture3D Generate3D(int width, int height, int depth, TextureGenerationSettings settings,
            Func<float, bool> cancel = null)
        {
            ValidateVolumeDimensions(width, height, depth);
            if (!SystemInfo.supports3DTextures || Mathf.Max(width, Mathf.Max(height, depth)) > SystemInfo.maxTexture3DSize)
                throw new NotSupportedException("This device does not support the requested Texture3D dimensions.");
            Color32[] pixels = Generate3DPixels(width, height, depth, settings, cancel);
            // Explicit UNorm format keeps the volume linear, matching 2D data textures.
            var texture = new Texture3D(width, height, depth, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None)
            {
                name = "GeneratedVolume",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = settings.seamless ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                return texture;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }
    }
}
#endif
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureTools.Editor
{
    public static partial class TextureGeneratorEngine
    {
        // Bins contain indices in drawing order, avoiding a full stamp scan at every voxel.
        private sealed class RectangleLayerSampler
        {
            private const int Grid = 8;
            private struct Stamp
            {
                public Vector3 center, size;
                public float value, id;
                public int blend;
            }
            private readonly Stamp[] stamps;
            private readonly List<int>[] bins;
            private readonly bool volume, seamless;
            private readonly float background, strength;
            private readonly float[] distribution;

            public RectangleLayerSampler(TextureGenerationSettings settings, bool isVolume)
            {
                volume = isVolume; seamless = settings.seamless;
                background = Mathf.Clamp01(settings.rectangleBackground);
                strength = Mathf.Clamp01(settings.rectangleStrength);
                float minimum = Mathf.Clamp(settings.rectangleSize.x, 0.001f, 1);
                float maximum = Mathf.Clamp(settings.rectangleSize.y, minimum, 1);
                stamps = new Stamp[Mathf.Clamp(settings.rectangleCount, 1, 2048)];
                bins = new List<int>[Grid * Grid * (volume ? Grid : 1)];
                float Random(int index, int salt) => Hash(index, salt, settings.seed);
                for (int i = 0; i < stamps.Length; i++)
                {
                    var stamp = new Stamp
                    {
                        center = new Vector3(Random(i, 11), Random(i, 23), volume ? Random(i, 37) : 0.5f),
                        size = new Vector3(Mathf.Lerp(minimum, maximum, Random(i, 41)),
                            Mathf.Lerp(minimum, maximum, Random(i, 53)),
                            volume ? Mathf.Lerp(minimum, maximum, Random(i, 67)) : 1),
                        value = Random(i, 79), id = Random(i, 83),
                        blend = Mathf.Min(2, (int)(Random(i, 97) * 3))
                    };
                    stamps[i] = stamp;
                    for (int z = 0; z < (volume ? Grid : 1); z++)
                    {
                        if (volume && !TouchesBin(stamp.center.z, stamp.size.z, z)) continue;
                        for (int y = 0; y < Grid; y++)
                        {
                            if (!TouchesBin(stamp.center.y, stamp.size.y, y)) continue;
                            for (int x = 0; x < Grid; x++)
                            {
                                if (!TouchesBin(stamp.center.x, stamp.size.x, x)) continue;
                                int bin = (z * Grid + y) * Grid + x;
                                if (bins[bin] == null) bins[bin] = new List<int>();
                                bins[bin].Add(i);
                            }
                        }
                    }
                }
                if (settings.rectangleEqualize)
                {
                    distribution = new float[4096];
                    BuildDistribution(distribution);
                }
            }

            // Resolution-independent CDF shared by preview, slices and final output.
            private void BuildDistribution(float[] values)
            {
                for (int i = 0; i < values.Length; i++)
                    values[i] = SampleRaw(Hash(i, 149, 0), Hash(i, 157, 0),
                        volume ? Hash(i, 163, 0) : 0.5f).value;
                Array.Sort(values);
            }

            private float Equalize(float value)
            {
                if (distribution == null) return Mathf.Clamp01(value);
                int count = distribution.Length;
                if (distribution[count - 1] - distribution[0] < 0.000001f)
                    return Mathf.Clamp01(value);
                int lo = 0, hi = count;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (distribution[mid] < value) lo = mid + 1; else hi = mid;
                }
                int first = lo;
                hi = count;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (distribution[mid] <= value) lo = mid + 1; else hi = mid;
                }
                if (lo > first) return (first + lo) * 0.5f / count;
                if (first == 0) return 0;
                if (first == count) return 1;
                float t = Mathf.InverseLerp(distribution[first - 1], distribution[first], value);
                return (first - 0.5f + t) / count;
            }

            private bool TouchesBin(float center, float size, int bin)
            {
                float delta = (bin + 0.5f) / Grid - center;
                if (seamless) delta -= Mathf.Floor(delta + 0.5f);
                return Mathf.Abs(delta) <= size * 0.5f + 0.5f / Grid;
            }

            public VolumeSample Sample(float u, float v, float w)
            {
                VolumeSample sample = SampleRaw(u, v, w);
                sample.value = Equalize(sample.value);
                return sample;
            }

            private VolumeSample SampleRaw(float u, float v, float w)
            {
                if (seamless) { u -= Mathf.Floor(u); v -= Mathf.Floor(v); w -= Mathf.Floor(w); }
                int x = Mathf.Clamp(Mathf.FloorToInt(u * Grid), 0, Grid - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(v * Grid), 0, Grid - 1);
                int z = volume ? Mathf.Clamp(Mathf.FloorToInt(w * Grid), 0, Grid - 1) : 0;
                var result = new VolumeSample { value = background, center = new Vector3(0.5f, 0.5f, 0.5f) };
                List<int> candidates = bins[(z * Grid + y) * Grid + x];
                if (candidates == null) return result;
                foreach (int index in candidates)
                {
                    Stamp stamp = stamps[index];
                    Vector3 delta = new Vector3(u, v, volume ? w : 0.5f) - stamp.center;
                    if (seamless)
                    {
                        delta.x -= Mathf.Floor(delta.x + 0.5f);
                        delta.y -= Mathf.Floor(delta.y + 0.5f);
                        delta.z -= Mathf.Floor(delta.z + 0.5f);
                    }
                    if (Mathf.Abs(delta.x) >= stamp.size.x * 0.5f ||
                        Mathf.Abs(delta.y) >= stamp.size.y * 0.5f ||
                        (volume && Mathf.Abs(delta.z) >= stamp.size.z * 0.5f)) continue;
                    // Keep float headroom until all stamps are composed; clipping each addition
                    // would collapse many distinct regions to exactly white.
                    switch (stamp.blend)
                    {
                        case 0: result.value += stamp.value * strength; break;
                        case 1: result.value *= Mathf.Lerp(1, stamp.value, strength); break;
                        default: result.value = Mathf.Lerp(result.value, stamp.value, strength); break;
                    }
                    result.id = stamp.id; result.center = stamp.center;
                    result.uv = new Vector3(delta.x / stamp.size.x + 0.5f,
                        delta.y / stamp.size.y + 0.5f, volume ? delta.z / stamp.size.z + 0.5f : 0);
                }
                return result;
            }
        }

        private static Color32[] GenerateRectanglePixels(int width, int height, int depth,
            TextureGenerationSettings settings, bool volume, Func<float, bool> cancel, int slice = -1)
        {
            var channels = new[] { settings.red, settings.green, settings.blue, settings.alpha };
            var seeds = new[] { settings.redSeed, settings.greenSeed, settings.blueSeed, settings.alphaSeed };
            var samplers = new RectangleLayerSampler[4];
            for (int c = 0; c < 4; c++)
            {
                if (channels[c] == GeneratedChannel.Zero || channels[c] == GeneratedChannel.One) continue;
                int seed = settings.separateSeeds ? seeds[c] : settings.seed;
                for (int previous = 0; previous < c; previous++)
                    if (samplers[previous] != null && (!settings.separateSeeds || seeds[previous] == seed))
                    { samplers[c] = samplers[previous]; break; }
                if (samplers[c] == null) samplers[c] = new RectangleLayerSampler(settings.WithSeed(seed), volume);
            }
            int outputDepth = slice >= 0 ? 1 : depth;
            var pixels = new Color32[width * height * outputDepth];
            var samples = new VolumeSample[4];
            for (int z = 0; z < outputDepth; z++)
            for (int y = 0; y < height; y++)
            {
                if (y % 8 == 0 && cancel != null && cancel((float)(z * height + y) / (outputDepth * height)))
                    throw new OperationCanceledException();
                for (int x = 0; x < width; x++)
                {
                    Color color = Color.clear;
                    for (int c = 0; c < 4; c++)
                    {
                        if (channels[c] == GeneratedChannel.Zero) { color[c] = 0; continue; }
                        if (channels[c] == GeneratedChannel.One) { color[c] = 1; continue; }
                        int previous = 0;
                        for (; previous < c; previous++) if (samplers[previous] == samplers[c]) break;
                        samples[c] = previous < c ? samples[previous] : samplers[c].Sample(
                            (x + 0.5f) / width, (y + 0.5f) / height, ((slice >= 0 ? slice : z) + 0.5f) / depth);
                        color[c] = !volume && (channels[c] == GeneratedChannel.W || channels[c] == GeneratedChannel.CenterW)
                            ? 0 : VolumeChannel(samples[c], channels[c]);
                    }
                    pixels[(z * height + y) * width + x] = color;
                }
            }
            return pixels;
        }
    }
}
#endif
#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEngine;

namespace TextureTools.Editor.Tests
{
    public class TextureGeneratorTests
    {
        [Test]
        public void EveryPatternRepeatsInBothDirectionsIncludingOddStaggeredRowCounts()
        {
            var settings = new TextureGenerationSettings { columns = 7, rows = 9, seed = -213 };
            foreach (GeneratedPattern pattern in Enum.GetValues(typeof(GeneratedPattern)))
            {
                settings.pattern = pattern;
                for (int i = 0; i < 80; i++)
                {
                    float u = (i + 0.371f) / 79, v = (i * 7 % 83 + 0.219f) / 83;
                    Vector4 sample = TextureGeneratorEngine.Sample(u, v, settings);
                    Vector4 horizontal = TextureGeneratorEngine.Sample(u + 1, v, settings);
                    Vector4 vertical = TextureGeneratorEngine.Sample(u, v + 1, settings);
                    for (int channel = 0; channel < 4; channel++)
                    {
                        Assert.That(sample[channel], Is.InRange(0f, 1f), pattern.ToString());
                        Assert.That(horizontal[channel], Is.EqualTo(sample[channel]).Within(0.0002f), pattern.ToString());
                        Assert.That(vertical[channel], Is.EqualTo(sample[channel]).Within(0.0002f), pattern.ToString());
                    }
                }
            }
        }

        [Test]
        public void OffsetPreservesAllChannelsAndRoundsOddDimensionsDown()
        {
            foreach (int width in new[] { 1, 4, 5 })
            foreach (int height in new[] { 1, 6, 7 })
            foreach (bool horizontal in new[] { false, true })
            foreach (bool vertical in new[] { false, true })
            {
                var source = new Color32[width * height];
                for (int i = 0; i < source.Length; i++) source[i] = new Color32((byte)i, 31, 73, (byte)(i + 1));
                Color32[] output = TextureGeneratorEngine.OffsetPixels(source, width, height, horizontal, vertical);
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int destinationX = (x + (horizontal ? width / 2 : 0)) % width;
                    int destinationY = (y + (vertical ? height / 2 : 0)) % height;
                    Assert.That(output[destinationY * width + destinationX], Is.EqualTo(source[y * width + x]));
                }
                if (width % 2 == 0 && height % 2 == 0)
                    CollectionAssert.AreEqual(source, TextureGeneratorEngine.OffsetPixels(output, width, height, horizontal, vertical));
            }
        }

        [Test]
        public void GeneratedTexturePacksUVAndConstantsWithoutChangingDimensions()
        {
            var settings = new TextureGenerationSettings
            {
                pattern = GeneratedPattern.Triangle,
                red = GeneratedChannel.U, green = GeneratedChannel.V,
                blue = GeneratedChannel.Zero, alpha = GeneratedChannel.One
            };
            Texture2D texture = TextureGeneratorEngine.Generate(17, 23, settings);
            try
            {
                Assert.That(texture.width, Is.EqualTo(17));
                Assert.That(texture.height, Is.EqualTo(23));
                Assert.That(texture.isDataSRGB, Is.False);
                Color32[] pixels = texture.GetPixels32();
                for (int y = 0; y < 23; y++)
                for (int x = 0; x < 17; x++)
                {
                    Vector4 sample = TextureGeneratorEngine.Sample((x + 0.5f) / 17, (y + 0.5f) / 23, settings);
                    Color32 expected = new Color(sample.y, sample.z, 0, 1);
                    Assert.That(pixels[y * 17 + x], Is.EqualTo(expected));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test]
        public void GenerationCanBeCancelled()
        {
            Assert.Throws<OperationCanceledException>(() => TextureGeneratorEngine.Generate(64, 64,
                new TextureGenerationSettings(), progress => progress >= 0.25f));
        }
    }
}
#endif

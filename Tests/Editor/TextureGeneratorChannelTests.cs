#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace TextureTools.Editor.Tests
{
    public sealed class TextureGeneratorChannelTests
    {
        [Test]
        public void IndependentCellCountsAffectEachChannelWithTheSameSeed()
        {
            var settings = new TextureGenerationSettings
            {
                pattern = GeneratedPattern.Value, separateCells = true,
                redCells = new Vector3Int(2, 2, 1), greenCells = new Vector3Int(4, 2, 1),
                blueCells = new Vector3Int(6, 2, 1),
                red = GeneratedChannel.U, green = GeneratedChannel.U, blue = GeneratedChannel.U
            };
            Texture2D image = TextureGeneratorEngine.Generate(16, 16, settings);
            try
            {
                Color32 pixel = image.GetPixels32()[7];
                Assert.That(pixel.r / 255f, Is.EqualTo(0.9375f).Within(1f / 255));
                Assert.That(pixel.g / 255f, Is.EqualTo(0.875f).Within(1f / 255));
                Assert.That(pixel.b / 255f, Is.EqualTo(0.8125f).Within(1f / 255));
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }

        [Test]
        public void IndependentLayerCountsAffectTheVolumeSlice()
        {
            var settings = new TextureGenerationSettings
            {
                pattern = GeneratedPattern.Value, separateCells = true,
                redCells = new Vector3Int(2, 2, 1), greenCells = new Vector3Int(2, 2, 2),
                blueCells = new Vector3Int(2, 2, 3),
                red = GeneratedChannel.W, green = GeneratedChannel.W, blue = GeneratedChannel.W
            };
            Texture2D image = TextureGeneratorEngine.Generate3DSlice(4, 4, 4, 0, settings);
            try
            {
                Color32 pixel = image.GetPixels32()[0];
                Assert.That(pixel.r / 255f, Is.EqualTo(0.125f).Within(1f / 255));
                Assert.That(pixel.g / 255f, Is.EqualTo(0.25f).Within(1f / 255));
                Assert.That(pixel.b / 255f, Is.EqualTo(0.375f).Within(1f / 255));
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }

        [Test]
        public void PackingSupportsConstantsAndReordering()
        {
            var settings = new TextureGeneratorChannelPacking();
            settings.channels[0].channel = TextureSourceChannel.One;
            settings.channels[1].channel = TextureSourceChannel.Zero;
            settings.channels[2].channel = TextureSourceChannel.One;
            settings.channels[3].channel = TextureSourceChannel.Zero;
            Assert.That(TextureGeneratorEngine.GeneratePackedPixels(settings, 1, 1)[0], Is.EqualTo(new Color(1, 0, 1, 0)));
            Array.Reverse(settings.channels);
            Assert.That(TextureGeneratorEngine.GeneratePackedPixels(settings, 1, 1)[0], Is.EqualTo(new Color(0, 1, 0, 1)));
        }

        [Test]
        public void PackingReadsAnUnreadableHDRSourceWithoutLosingValues()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
                Assert.Ignore("Float render targets require a graphics device.");
            var source = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                source.SetPixel(0, 0, new Color(4, 0.25f, 0.75f, 0.5f));
                source.Apply(false, true);
                var settings = new TextureGeneratorChannelPacking();
                foreach (var mapping in settings.channels) mapping.texture = source;
                settings.channels[0].channel = TextureSourceChannel.B;
                settings.channels[1].channel = TextureSourceChannel.R;
                settings.channels[2].channel = TextureSourceChannel.One;
                settings.channels[3].channel = TextureSourceChannel.A;
                Color pixel = TextureGeneratorEngine.GeneratePackedPixels(settings, 1, 1)[0];
                Assert.That(pixel.r, Is.EqualTo(0.75f).Within(0.0001));
                Assert.That(pixel.g, Is.EqualTo(4).Within(0.0001));
                Assert.That(pixel.b, Is.EqualTo(1).Within(0.0001));
                Assert.That(pixel.a, Is.EqualTo(0.5f).Within(0.0001));
                Assert.That(source.isReadable, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
    }
}
#endif
#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

namespace TextureTools.Editor.Tests
{
    public sealed class TextureGeneratorGradientTests
    {
        private static Gradient CreateGradient(Color start, Color end)
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(start, 0), new GradientColorKey(end, 1) },
                new[] { new GradientAlphaKey(start.a, 0), new GradientAlphaKey(end.a, 1) });
            return gradient;
        }

        [Test]
        public void MultipleGradientsUsesTopToBottomBandsAndIncludesEndpoints()
        {
            var recipe = new TextureGeneratorRecipe { mode = TextureGenerationMode.MultipleGradients };
            recipe.gradient.size = new Vector2Int(3, 2);
            recipe.gradient.gradients = new[] { CreateGradient(Color.red, Color.white), CreateGradient(Color.green, Color.blue) };
            Assert.That(TextureGeneratorEngine.GetImageSize(recipe), Is.EqualTo(new Vector2Int(3, 4)));
            Color[] pixels = TextureGeneratorAssetUtility.GeneratePixels(recipe);
            Assert.That(pixels[9], Is.EqualTo(Color.red));
            Assert.That(pixels[11], Is.EqualTo(Color.white));
            Assert.That(pixels[0], Is.EqualTo(Color.green));
            Assert.That(pixels[2], Is.EqualTo(Color.blue));
            Assert.That(pixels[6], Is.EqualTo(pixels[9]));
        }

        [Test]
        public void HorizontalCurvesKeepNegativeAndHDRValuesThroughPresetClone()
        {
            var recipe = new TextureGeneratorRecipe { mode = TextureGenerationMode.Curves, output = TextureImageOutput.EXR };
            recipe.gradient.size = new Vector2Int(3, 1);
            recipe.gradient.red = AnimationCurve.Linear(0, -2, 1, 4);
            recipe.gradient.alpha = AnimationCurve.Linear(0, 0.25f, 1, 0.75f);
            recipe = recipe.Clone();
            Color[] values = recipe.ApplyOutput(TextureGeneratorAssetUtility.GeneratePixels(recipe));
            Assert.That(values[0].r, Is.EqualTo(-2).Within(0.00001));
            Assert.That(values[2].r, Is.EqualTo(4).Within(0.00001));
            Assert.That(values[0].a, Is.EqualTo(0.25f).Within(0.00001));
            Assert.That(values[2].a, Is.EqualTo(0.75f).Within(0.00001));
        }

        [Test]
        public void HDRGradientIsNotQuantizedToEightBits()
        {
            var recipe = new TextureGeneratorRecipe { mode = TextureGenerationMode.MultipleGradients, output = TextureImageOutput.EXR };
            recipe.gradient.size = new Vector2Int(2, 1);
            recipe.gradient.gradients = new[] { CreateGradient(new Color(4, 0.12345f, 0, 1), new Color(8, 0, 0, 1)) };
            Color[] values = TextureGeneratorAssetUtility.GeneratePixels(recipe.Clone());
            Assert.That(values[0].r, Is.EqualTo(4).Within(0.00001));
            Assert.That(values[0].g, Is.EqualTo(0.12345f).Within(0.00001));
            Assert.That(values[1].r, Is.EqualTo(8).Within(0.00001));
        }

        [Test]
        public void IdentityTonePreservesHDRWhilePNGClamps()
        {
            var tone = new TextureGeneratorTone { enabled = true };
            var input = new Color(4, -2, 0.5f, 1.5f);
            Assert.That(tone.Apply(new[] { input }, true)[0], Is.EqualTo(input));
            Assert.That(tone.Apply(new[] { input }, false)[0], Is.EqualTo(new Color(1, 0, 0.5f, 1)));
        }

        [TestCase(TextureGenerationMode.Curves)]
        [TestCase(TextureGenerationMode.MultipleGradients)]
        public void GradientOutputIgnoresNoiseTone(TextureGenerationMode mode)
        {
            var recipe = new TextureGeneratorRecipe { mode = mode, output = TextureImageOutput.EXR };
            recipe.tone.enabled = true;
            recipe.tone.contrastR = 0;
            recipe.tone.red = AnimationCurve.Linear(0, 0, 1, 0);
            Color value = new Color(4, -2, 0.25f, 0.75f);
            Assert.That(recipe.ApplyOutput(new[] { value })[0], Is.EqualTo(value));
            recipe.output = TextureImageOutput.PNG;
            Assert.That(recipe.ApplyOutput(new[] { value })[0], Is.EqualTo(new Color(1, 0, 0.25f, 0.75f)));
        }

        [Test]
        public void VolumeAtlasPackingKeepsFloatValuesAndSliceOrder()
        {
            var first = new Color(4, -2, 0.12345f, 0.5f);
            var second = new Color(8, 3, 0, 1);
            Color[] packed = TextureGeneratorAssetUtility.PackVolumePNG(new[] { first, second },
                new Vector3Int(1, 1, 2), new Vector2Int(1, 2));
            Assert.That(packed[1], Is.EqualTo(first));
            Assert.That(packed[0], Is.EqualTo(second));
        }
    }
}
#endif
#if UNITY_EDITOR
using System;
using UnityEngine;

namespace TextureTools.Editor
{
    public static partial class TextureGeneratorEngine
    {
        public static Vector2Int GetImageSize(TextureGeneratorRecipe recipe)
        {
            recipe.Normalize();
            if (recipe.mode == TextureGenerationMode.Noise)
                return recipe.IsVolume ? new Vector2Int(recipe.volumeSize.x, recipe.volumeSize.y) : recipe.size;
            int count = recipe.mode == TextureGenerationMode.MultipleGradients ? recipe.ramp.gradients?.Length ?? 0 : 1;
            if (count < 1) throw new InvalidOperationException("Gradientを1つ以上追加してください。");
            long height = (long)recipe.ramp.size.y * count;
            if (height > 4096) throw new InvalidOperationException("1段の高さ×Gradient数は4096以下にしてください。");
            return new Vector2Int(recipe.ramp.size.x, (int)height);
        }

        public static Color[] GenerateRampPixels(TextureGeneratorRecipe recipe, int width = 0, int height = 0)
        {
            if (recipe.mode == TextureGenerationMode.Noise) throw new ArgumentException("Ramp mode is required.");
            Vector2Int fullSize = GetImageSize(recipe);
            if (width == 0) width = fullSize.x;
            if (height == 0) height = fullSize.y;
            if (width < 1 || height < 1 || width > 4096 || height > 4096) throw new ArgumentOutOfRangeException(nameof(width));
            var pixels = new Color[checked(width * height)];
            var ramp = recipe.ramp;
            for (int y = 0; y < height; y++)
            {
                // First gradient is the top band. Map reduced previews to the same band boundaries.
                int sourceY = Mathf.Min(fullSize.y - 1, Mathf.FloorToInt((y + 0.5f) * fullSize.y / height));
                int band = recipe.mode == TextureGenerationMode.MultipleGradients
                    ? ramp.gradients.Length - 1 - sourceY / ramp.size.y : 0;
                for (int x = 0; x < width; x++)
                {
                    float t = width == 1 ? 0 : (float)x / (width - 1);
                    Color value;
                    if (recipe.mode == TextureGenerationMode.MultipleGradients)
                    {
                        Gradient gradient = ramp.gradients[band];
                        if (gradient == null) throw new InvalidOperationException("空のGradientがあります。");
                        value = gradient.Evaluate(t);
                    }
                    else
                    {
                        float Evaluate(AnimationCurve curve, float fallback)
                            => curve == null || curve.length == 0 ? fallback : curve.Evaluate(t);
                        value = new Color(Evaluate(ramp.red, 0), Evaluate(ramp.green, 0),
                            Evaluate(ramp.blue, 0), Evaluate(ramp.alpha, 1));
                    }
                    pixels[y * width + x] = value;
                }
            }
            return pixels;
        }
    }
}
#endif
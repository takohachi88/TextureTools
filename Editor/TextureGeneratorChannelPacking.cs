#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureTools.Editor
{
    public enum TextureSourceChannel { R, G, B, A, Zero, One }

    [Serializable]
    public sealed class TextureGeneratorChannelSource
    {
        public Texture2D texture;
        public TextureSourceChannel channel;
        public TextureGeneratorChannelSource(TextureSourceChannel channel) { this.channel = channel; }
    }

    [Serializable]
    public sealed class TextureGeneratorChannelPacking
    {
        public TextureGeneratorChannelSource[] channels =
        {
            new TextureGeneratorChannelSource(TextureSourceChannel.R),
            new TextureGeneratorChannelSource(TextureSourceChannel.G),
            new TextureGeneratorChannelSource(TextureSourceChannel.B),
            new TextureGeneratorChannelSource(TextureSourceChannel.One)
        };
    }

    public static partial class TextureGeneratorEngine
    {
        public static Color[] GeneratePackedPixels(TextureGeneratorChannelPacking settings, int width, int height)
        {
            if (width < 1 || height < 1 || width > 4096 || height > 4096)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (settings?.channels == null || settings.channels.Length != 4)
                throw new ArgumentException("RGBAのマッピングを4つ指定してください。");
            var output = new Color[checked(width * height)];
            var sources = new Dictionary<Texture2D, Color[]>();
            for (int c = 0; c < 4; c++)
            {
                TextureGeneratorChannelSource mapping = settings.channels[c];
                if (mapping == null) continue;
                if (mapping.channel == TextureSourceChannel.One)
                {
                    for (int i = 0; i < output.Length; i++) { Color p = output[i]; p[c] = 1; output[i] = p; }
                    continue;
                }
                if (mapping.channel == TextureSourceChannel.Zero || mapping.texture == null) continue;
                if (!sources.TryGetValue(mapping.texture, out Color[] pixels))
                {
                    pixels = ReadSourcePixels(mapping.texture, width, height);
                    sources.Add(mapping.texture, pixels);
                }
                for (int i = 0; i < output.Length; i++)
                {
                    Color p = output[i]; p[c] = pixels[i][(int)mapping.channel]; output[i] = p;
                }
            }
            return output;
        }

        private static Color[] ReadSourcePixels(Texture2D source, int width, int height)
        {
            RenderTexture previous = RenderTexture.active;
            bool previousWrite = GL.sRGBWrite;
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Texture2D readable = null;
            try
            {
                GL.sRGBWrite = false;
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readable.Apply(false, false);
                return readable.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = previousWrite;
                RenderTexture.ReleaseTemporary(temporary);
                if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
            }
        }
    }
}
#endif
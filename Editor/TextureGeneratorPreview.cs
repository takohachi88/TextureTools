#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace TextureTools.Editor
{
    internal sealed class TextureGeneratorPreview : IDisposable
    {
        private TextureGeneratorRecipe recipe;
        private TextureGeneratorTone tone = new TextureGeneratorTone();
        private string generationKey, toneKey, error;
        private Color[] pixels;
        private Color32[] volume;
        private Vector3Int volumeSize;
        private int width, height, channel, slice = 32;
        private bool linear = true, pending, tiles, started;
        public bool HasStarted => started;
        private double due;
        private Texture2D display;
        private int[,] before, after;
        private float histogramMin, histogramMax = 1, exposure;
        public void SetRecipe(TextureGeneratorRecipe data)
        {
            recipe = data.Clone(); recipe.Normalize();
            string key = recipe.mode + "|" + (recipe.mode == TextureGenerationMode.Noise
                ? JsonUtility.ToJson(recipe.noise) + "|" + recipe.IsVolume + "|" +
                    (recipe.IsVolume ? recipe.volumeSize.ToString() : recipe.size.ToString())
                : JsonUtility.ToJson(recipe.ramp));
            if (generationKey != key)
            {
                generationKey = key; volume = null; pending = started; error = null;
                due = EditorApplication.timeSinceStartup + 0.2;
            }
            SetTone(recipe.tone);
        }
        public void SetTone(TextureGeneratorTone value)
        {
            string key = JsonUtility.ToJson(value) + "|" + recipe?.mode + "|" + (recipe != null && recipe.IsHDR);
            if (key == toneKey) return;
            toneKey = key; tone = value; BuildDisplay();
        }
        public void Request() { started = true; generationKey = null; if (recipe != null) SetRecipe(recipe); }
        public bool Tick()
        {
            if (!pending || recipe == null || EditorApplication.timeSinceStartup < due ||
                EditorApplication.isCompiling || EditorApplication.isUpdating) return false;
            pending = false;
            try
            {
                if (recipe.IsVolume)
                {
                    volumeSize = recipe.volumeSize;
                    volume = TextureGeneratorEngine.Generate3DPixels(volumeSize.x, volumeSize.y, volumeSize.z,
                        recipe.noise, p => EditorUtility.DisplayCancelableProgressBar("Texture Preview", "3Dデータを生成中…", p));
                    ShowSlice();
                }
                else
                {
                    Vector2Int size = TextureGeneratorEngine.GetImageSize(recipe);
                    float scale = Mathf.Min(1, 256f / Mathf.Max(size.x, size.y));
                    int w = Mathf.Max(1, Mathf.RoundToInt(size.x * scale)), h = Mathf.Max(1, Mathf.RoundToInt(size.y * scale));
                    if (recipe.mode != TextureGenerationMode.Noise)
                        SetPixels(TextureGeneratorEngine.GenerateRampPixels(recipe, w, h), w, h, true);
                    else
                    {
                        Texture2D raw = TextureGeneratorEngine.Generate(w, h, recipe.noise);
                        try { SetPixels(raw.GetPixels32(), raw.width, raw.height, true); }
                        finally { UnityEngine.Object.DestroyImmediate(raw); }
                    }
                }
            }
            catch (OperationCanceledException) { error = "生成をキャンセルしました。再生成ボタンで再試行できます。"; }
            catch (Exception e) { error = e.Message; Debug.LogException(e); }
            finally { EditorUtility.ClearProgressBar(); }
            return true;
        }
        public void SetPixels(Color32[] values, int w, int h, bool isLinear)
            => SetPixels(Array.ConvertAll(values, p => (Color)p), w, h, isLinear);
        public void SetPixels(Color[] values, int w, int h, bool isLinear)
        {
            pixels = values; width = w; height = h; linear = isLinear; BuildDisplay();
        }
        private void ShowSlice()
        {
            if (volume == null) return;
            slice = Mathf.Clamp(slice, 0, volumeSize.z - 1);
            var values = new Color32[volumeSize.x * volumeSize.y];
            Array.Copy(volume, slice * values.Length, values, 0, values.Length);
            SetPixels(values, volumeSize.x, volumeSize.y, true);
        }
        private int[,] Histogram(Color[] values)
        {
            var bins = new int[6, 256];
            foreach (Color p in values)
            {
                int r = Bin(p.r), g = Bin(p.g), b = Bin(p.b), a = Bin(p.a);
                bins[0, r]++; bins[0, g]++; bins[0, b]++;
                bins[1, r]++; bins[2, g]++; bins[3, b]++; bins[4, a]++;
                bins[5, r]++; bins[5, g]++; bins[5, b]++; bins[5, a]++;
            }
            return bins;
        }
        private int Bin(float value) => Mathf.Clamp(Mathf.FloorToInt(
            Mathf.InverseLerp(histogramMin, histogramMax, value) * 255), 0, 255);
        private void IncludeRange(Color[] values)
        {
            foreach (Color p in values)
                for (int c = 0; c < 4; c++)
                { histogramMin = Mathf.Min(histogramMin, p[c]); histogramMax = Mathf.Max(histogramMax, p[c]); }
        }
        private void BuildDisplay()
        {
            if (pixels == null) return;
            bool hdr = recipe != null && recipe.IsHDR;
            Color[] adjusted = (Color[])pixels.Clone();
            adjusted = recipe != null ? recipe.ApplyOutput(adjusted) : tone.Apply(adjusted);
            histogramMin = 0; histogramMax = 1;
            IncludeRange(pixels); IncludeRange(adjusted);
            before = Histogram(pixels); after = Histogram(adjusted);
            float gain = hdr ? Mathf.Pow(2, exposure) : 1;
            for (int i = 0; i < adjusted.Length; i++)
            {
                Color p = adjusted[i];
                if (channel > 0 && channel < 4)
                {
                    float v = p[channel - 1] * gain;
                    adjusted[i] = new Color(v, v, v, 1);
                }
                else adjusted[i] = new Color(p.r * gain, p.g * gain, p.b * gain, p.a);
            }
            if (display != null) UnityEngine.Object.DestroyImmediate(display);
            display = new Texture2D(width, height, TextureFormat.RGBAFloat, false, linear)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = recipe == null ? FilterMode.Bilinear : recipe.Filter };
            display.SetPixels(adjusted); display.Apply(false, false);
        }
        public void DrawHistogram()
        {
            EditorGUILayout.LabelField($"画素値の分布 [{histogramMin:G4} ～ {histogramMax:G4}]（灰: 前 / 色: 後）", EditorStyles.miniLabel);
            Rect rect = GUILayoutUtility.GetRect(1, 70, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.08f));
            if (before == null || after == null) return;
            int maximum = 1;
            for (int i = 0; i < 256; i++) maximum = Mathf.Max(maximum, Mathf.Max(before[channel, i], after[channel, i]));
            Color color = channel == 1 ? Color.red : channel == 2 ? Color.green : channel == 3 ? Color.cyan : channel == 5 ? Color.white : Color.yellow;
            color.a = 0.65f;
            for (int i = 0; i < 256; i++)
            {
                float x = rect.x + rect.width * i / 256;
                float a = (rect.height - 1) * before[channel, i] / maximum;
                float b = (rect.height - 1) * after[channel, i] / maximum;
                EditorGUI.DrawRect(new Rect(x, rect.yMax - a, Mathf.Max(1, rect.width / 256), a), Color.gray);
                EditorGUI.DrawRect(new Rect(x, rect.yMax - b, Mathf.Max(1, rect.width / 256), b), color);
            }
        }
        public void Draw(bool isVolume, Action generate, string generateLabel, bool canGenerate = true)
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool expanded;
                using (new EditorGUILayout.HorizontalScope())
                {
                    expanded = TextureGeneratorGUI.AreaFoldout("Preview", "プレビュー");
                    if (expanded)
                        using (new EditorGUI.DisabledScope(!canGenerate))
                            if (GUILayout.Button(generateLabel, GUILayout.Width(130))) generate();
                }
                if (!expanded) return;
                EditorGUI.BeginChangeCheck();
                channel = GUILayout.Toolbar(channel, new[] { "RGB", "R", "G", "B", "A", "RGBA" });
                if (EditorGUI.EndChangeCheck()) BuildDisplay();
                tiles = EditorGUILayout.Toggle("3 × 3 タイル", tiles);
                if (recipe != null && recipe.IsHDR)
                {
                    EditorGUI.BeginChangeCheck();
                    exposure = EditorGUILayout.Slider("表示露出 (EV)", exposure, -10, 10);
                    if (EditorGUI.EndChangeCheck()) BuildDisplay();
                }
                if (isVolume && recipe != null)
                {
                    EditorGUI.BeginChangeCheck();
                    slice = EditorGUILayout.IntSlider("Zスライス", slice, 0, recipe.volumeSize.z - 1);
                    if (EditorGUI.EndChangeCheck()) ShowSlice();
                    EditorGUILayout.LabelField("分布も選択中のZ断面です。圧縮前のデータを表示します。", EditorStyles.miniLabel);
                }
                if (pending) EditorGUILayout.LabelField("プレビュー更新待ち…", EditorStyles.miniLabel);
                if (error != null) EditorGUILayout.HelpBox(error, MessageType.Info);
                if (display == null) return;
                float w = Mathf.Clamp(EditorGUIUtility.currentViewWidth * 2f / 3f - 25, 100, 420);
                float h = w * display.height / display.width;
                if (h > 280) { h = 280; w = h * display.width / display.height; }
                // LUTs with one-pixel bands remain visible without changing the output resolution.
                if (recipe != null && recipe.mode != TextureGenerationMode.Noise) h = Mathf.Max(48, h);
                Rect area = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
                int count = tiles ? 3 : 1;
                for (int y = 0; y < count; y++) for (int x = 0; x < count; x++)
                {
                    Rect tile = new Rect(area.x + x * w / count, area.y + y * h / count, w / count, h / count);
                    if (channel == 4) EditorGUI.DrawTextureAlpha(tile, display, ScaleMode.StretchToFill);
                    else if (channel == 5) EditorGUI.DrawTextureTransparent(tile, display, ScaleMode.StretchToFill);
                    else GUI.DrawTexture(tile, display, ScaleMode.StretchToFill, false);
                }
            }
        }
        public void Clear()
        {
            if (display != null) UnityEngine.Object.DestroyImmediate(display);
            display = null; pixels = null; volume = null; before = after = null;
        }
        public void Dispose() { pending = false; Clear(); }
    }
}
#endif
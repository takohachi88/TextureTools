#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace TextureTools.Editor
{
    internal static class TextureGeneratorGUI
    {
        internal static bool AreaFoldout(string key, string title)
        {
            string sessionKey = "TextureTools.Generator.Area." + key;
            bool expanded = SessionState.GetBool(sessionKey, true);
            Rect header = EditorGUILayout.GetControlRect();
            header.xMin += 8;
            bool next = EditorGUI.Foldout(header, expanded, title, true, EditorStyles.foldoutHeader);
            if (next != expanded) SessionState.SetBool(sessionKey, next);
            return next;
        }
        private static void Field(SerializedProperty parent, string field, string label)
            => EditorGUILayout.PropertyField(parent.FindPropertyRelative(field), new GUIContent(label), true);
        private static void Int(SerializedProperty parent, string field, string label, int min, int max)
        {
            SerializedProperty p = parent.FindPropertyRelative(field);
            p.intValue = EditorGUILayout.IntSlider(label, p.intValue, min, max);
        }
        public static void DrawRecipe(SerializedProperty root, Action histogram, TextureGeneratorPackingGUI packingGUI)
        {
            Field(root, "mode", "生成モード");
            TextureGenerationMode mode = (TextureGenerationMode)root.FindPropertyRelative("mode").intValue;
            bool noiseMode = mode == TextureGenerationMode.Noise;
            bool hdr = root.FindPropertyRelative("output").intValue == (int)TextureImageOutput.EXR;
            if (noiseMode) Field(root, "generate3D", "3Dテクスチャ");
            bool volume = noiseMode && root.FindPropertyRelative("generate3D").boolValue;
            if (noiseMode || mode == TextureGenerationMode.ChannelPacking)
                Field(root, volume ? "volumeSize" : "size", volume ? "解像度 XYZ (4～256)" : "解像度 XY (16～4096)");
            else Field(root.FindPropertyRelative("gradient"), "size", mode == TextureGenerationMode.MultipleGradients ? "幅 / 1段の高さ" : "解像度 XY");
            SerializedProperty n = root.FindPropertyRelative("noise");
            if (noiseMode)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (AreaFoldout("Pattern", "パターン設定"))
                    {
                        Field(n, "pattern", "パターン");
                        GeneratedPattern pattern = (GeneratedPattern)n.FindPropertyRelative("pattern").intValue;
                        bool rectangles = pattern == GeneratedPattern.RectangleLayers;
                        bool prism = pattern == GeneratedPattern.Hexagon || pattern == GeneratedPattern.Triangle;
                        if (!rectangles)
                        {
                            bool separateCells = n.FindPropertyRelative("separateCells").boolValue;
                            Field(n, "separateCells", "RGBA別セル数");
                            if (n.FindPropertyRelative("separateCells").boolValue)
                            {
                                string[] cellFields = { "redCells", "greenCells", "blueCells", "alphaCells" };
                                for (int c = 0; c < cellFields.Length; c++)
                                {
                                    SerializedProperty cells = n.FindPropertyRelative(cellFields[c]);
                                    if (!separateCells) cells.vector3IntValue = new Vector3Int(
                                        n.FindPropertyRelative("columns").intValue, n.FindPropertyRelative("rows").intValue,
                                        n.FindPropertyRelative("layers").intValue);
                                    CellVector(cells, RgbaLabels[c], volume && !prism);
                                }
                            }
                            else CellCounts(n, volume && !prism);
                        }
                        Field(n, "seamless", "シームレス生成");
                        bool separate = n.FindPropertyRelative("separateSeeds").boolValue;
                        Field(n, "separateSeeds", "RGBA別シード");
                        if (n.FindPropertyRelative("separateSeeds").boolValue)
                        {
                            foreach (string c in new[] { "redSeed", "greenSeed", "blueSeed", "alphaSeed" })
                                if (!separate) n.FindPropertyRelative(c).intValue = n.FindPropertyRelative("seed").intValue;
                            FourNumbers(n, "シード", new[] { "redSeed", "greenSeed", "blueSeed", "alphaSeed" }, true);
                        }
                        else Field(n, "seed", "共通シード");

                        if (pattern == GeneratedPattern.Fractal || pattern == GeneratedPattern.Billow || pattern == GeneratedPattern.Ridged || pattern == GeneratedPattern.Block)
                        {
                            Int(n, "octaves", "オクターブ", 1, 8);
                            if (n.FindPropertyRelative("octaves").intValue > 1) Field(n, "persistence", "高周波の強さ");
                        }
                        if (pattern == GeneratedPattern.RectangleLayers)
                        {
                            Int(n, "rectangleCount", "描画回数", 1, 2048);
                            MinMaxRange(n.FindPropertyRelative("rectangleSize"), "サイズ (UV)", 0.001f, 1);
                            Field(n, "rectangleEqualize", "濃度分布を均等化");
                            SerializedProperty strength = n.FindPropertyRelative("rectangleStrength");
                            strength.floatValue = EditorGUILayout.Slider("合成の強さ", strength.floatValue, 0, 1);
                            SerializedProperty background = n.FindPropertyRelative("rectangleBackground");
                            background.floatValue = EditorGUILayout.Slider("背景値", background.floatValue, 0, 1);
                            EditorGUILayout.HelpBox("ランダムな位置・各辺の長さ・濃度の矩形（3Dでは直方体）を順に合成します。加算・乗算・通常合成を描画ごとにランダム選択します。濃度分布の均等化はValueだけに適用します。セル数は使いません。Valueは合成結果、CellId・中心UVは最後に重なった図形の情報です。", MessageType.None);
                        }
                        if (pattern == GeneratedPattern.Block)
                            EditorGUILayout.HelpBox("大小のセル乱数を補間せずに重ねるブロックノイズです。オクターブ1では指定したセルごとに一定値、増やすと細かいブロックが重なります。PNGはPointフィルターで取り込みます。", MessageType.None);
                        if (prism && (volume || n.FindPropertyRelative("seamless").boolValue))
                            EditorGUILayout.HelpBox(volume ? "六角形・三角形はZ方向に押し出した柱状パターンです。" :
                                "シームレス生成では縦セル数を偶数に切り上げます。", MessageType.None);
                    }
                }
                EditorGUILayout.Space();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (AreaFoldout("Channels", "RGBA チャンネル"))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                                ChannelColumn(n, volume);
                            GUILayout.Space(8);
                            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                            {
                                if (GUILayout.Button("グレースケール"))
                                    Assign(n, GeneratedChannel.Value, GeneratedChannel.Value, GeneratedChannel.Value);
                                if (GUILayout.Button(volume ? "UVW" : "UV + セルID"))
                                    Assign(n, GeneratedChannel.U, GeneratedChannel.V, volume ? GeneratedChannel.W : GeneratedChannel.CellId);
                                if (GUILayout.Button(new GUIContent(volume ? "セル中心UVW" : "セル中心UV", "セル中心の座標で画像をサンプリングするモザイク用の出力")))
                                {
                                    Assign(n, GeneratedChannel.CenterU, GeneratedChannel.CenterV, volume ? GeneratedChannel.CenterW : GeneratedChannel.Zero);
                                    n.FindPropertyRelative("greenSeed").intValue = n.FindPropertyRelative("redSeed").intValue;
                                    n.FindPropertyRelative("blueSeed").intValue = n.FindPropertyRelative("redSeed").intValue;
                                    if (n.FindPropertyRelative("separateCells").boolValue)
                                    {
                                        Vector3Int cells = n.FindPropertyRelative("redCells").vector3IntValue;
                                        n.FindPropertyRelative("columns").intValue = cells.x;
                                        n.FindPropertyRelative("rows").intValue = cells.y;
                                        n.FindPropertyRelative("layers").intValue = cells.z;
                                        n.FindPropertyRelative("separateCells").boolValue = false;
                                    }
                                    SerializedProperty tone = root.FindPropertyRelative("tone");
                                    tone.FindPropertyRelative("enabled").boolValue = false; ResetContrast(tone);
                                }
                            }
                        }
                        EditorGUILayout.HelpBox("U/V/W: セル内座標、CenterU/V/W: セル中心の画像座標、CellId: セルごとの乱数。\nVoronoiの座標を複数チャンネルで組み合わせる場合はシードを揃えてください。2DのWは0です。", MessageType.None);
                    }
                }
                EditorGUILayout.Space();
            }
            else if (mode == TextureGenerationMode.ChannelPacking)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    if (AreaFoldout("Packing", "チャンネル統合")) packingGUI.Draw();
            }
            else DrawGradients(root.FindPropertyRelative("gradient"), mode);
            if (noiseMode) DrawTone(root.FindPropertyRelative("tone"), histogram, hdr);
        }
        private static void DrawGradients(SerializedProperty gradient, TextureGenerationMode mode)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!AreaFoldout("Gradient", mode == TextureGenerationMode.MultipleGradients ? "Multiple Gradients" : "Curves")) return;
                if (mode == TextureGenerationMode.MultipleGradients)
                {
                    SerializedProperty gradients = gradient.FindPropertyRelative("gradients");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(12);
                        using (new EditorGUILayout.VerticalScope())
                            Field(gradient, "gradients", "HDR Gradients（上から順）");
                    }
                    Vector2Int size = gradient.FindPropertyRelative("size").vector2IntValue;
                    EditorGUILayout.LabelField("出力サイズ", $"{Mathf.Max(1, size.x)} × {(long)Mathf.Max(1, size.y) * gradients.arraySize}");
                    if (gradients.arraySize == 0)
                        EditorGUILayout.HelpBox("Gradientを1つ以上追加してください。", MessageType.Info);
                }
                else
                {
                    Curve(gradient, "red", "R", Color.red, 40, true);
                    Curve(gradient, "green", "G", Color.green, 40, true);
                    Curve(gradient, "blue", "B", Color.cyan, 40, true);
                    Curve(gradient, "alpha", "A", Color.white, 40, true);
                }
            }
            EditorGUILayout.Space();
        }
        private static readonly GUIContent[] RgbaLabels =
            { new GUIContent("R"), new GUIContent("G"), new GUIContent("B"), new GUIContent("A") };

        private static void FourNumbers(SerializedProperty parent, string label, string[] fields, bool integer, Action reset = null)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            float labelWidth = EditorGUIUtility.labelWidth;
            int indent = EditorGUI.indentLevel;
            try
            {
                EditorGUIUtility.labelWidth = 88;
                if (reset != null)
                {
                    Rect resetRect = new Rect(rect.xMax - 58, rect.y, 58, rect.height);
                    rect.width -= 62;
                    if (GUI.Button(resetRect, "リセット")) reset();
                }
                rect = EditorGUI.PrefixLabel(rect, new GUIContent(label, integer ? "RGBA別の整数シード" : "各チャンネルのコントラスト（0～4）"));
                EditorGUI.indentLevel = 0;
                EditorGUI.BeginChangeCheck();
                if (integer)
                {
                    int[] values = Array.ConvertAll(fields, f => parent.FindPropertyRelative(f).intValue);
                    EditorGUI.MultiIntField(rect, RgbaLabels, values);
                    if (EditorGUI.EndChangeCheck())
                        for (int i = 0; i < fields.Length; i++) parent.FindPropertyRelative(fields[i]).intValue = values[i];
                }
                else
                {
                    float[] values = Array.ConvertAll(fields, f => parent.FindPropertyRelative(f).floatValue);
                    EditorGUI.MultiFloatField(rect, RgbaLabels, values);
                    if (EditorGUI.EndChangeCheck())
                        for (int i = 0; i < fields.Length; i++) parent.FindPropertyRelative(fields[i]).floatValue = Mathf.Clamp(values[i], 0, 4);
                }
            }
            finally { EditorGUIUtility.labelWidth = labelWidth; EditorGUI.indentLevel = indent; }
        }

        private static void MinMaxRange(SerializedProperty property, string label, float lower, float upper)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            float labelWidth = EditorGUIUtility.labelWidth;
            int indent = EditorGUI.indentLevel;
            EditorGUI.BeginProperty(rect, new GUIContent(label), property);
            try
            {
                EditorGUIUtility.labelWidth = 88;
                rect = EditorGUI.PrefixLabel(rect, new GUIContent(label));
                EditorGUI.indentLevel = 0;
                // Keep the numeric endpoints editable as well as the two slider handles.
                float numberWidth = Mathf.Min(52, rect.width * 0.25f);
                Rect minimumRect = new Rect(rect.x, rect.y, numberWidth, rect.height);
                Rect maximumRect = new Rect(rect.xMax - numberWidth, rect.y, numberWidth, rect.height);
                Rect sliderRect = new Rect(minimumRect.xMax + 5, rect.y,
                    Mathf.Max(1, rect.width - numberWidth * 2 - 10), rect.height);
                Vector2 range = property.vector2Value;
                EditorGUI.BeginChangeCheck();
                range.x = Mathf.Clamp(EditorGUI.FloatField(minimumRect, range.x), lower, upper);
                range.y = Mathf.Clamp(EditorGUI.FloatField(maximumRect, range.y), range.x, upper);
                EditorGUI.MinMaxSlider(sliderRect, ref range.x, ref range.y, lower, upper);
                if (EditorGUI.EndChangeCheck()) property.vector2Value = range;
            }
            finally
            {
                EditorGUIUtility.labelWidth = labelWidth; EditorGUI.indentLevel = indent;
                EditorGUI.EndProperty();
            }
        }

        private static readonly GUIContent[] XyLabels = { new GUIContent("X"), new GUIContent("Y") };
        private static readonly GUIContent[] XyzLabels = { new GUIContent("X"), new GUIContent("Y"), new GUIContent("Z") };

        private static void CellVector(SerializedProperty property, GUIContent label, bool depth)
        {
            Vector3Int cells = property.vector3IntValue;
            int[] values = depth ? new[] { cells.x, cells.y, cells.z } : new[] { cells.x, cells.y };
            Rect rect = EditorGUILayout.GetControlRect();
            float oldWidth = EditorGUIUtility.labelWidth;
            int oldIndent = EditorGUI.indentLevel;
            EditorGUI.BeginProperty(rect, label, property);
            try
            {
                EditorGUIUtility.labelWidth = 88;
                rect = EditorGUI.PrefixLabel(rect, label);
                EditorGUI.indentLevel = 0;
                EditorGUI.BeginChangeCheck();
                EditorGUI.MultiIntField(rect, depth ? XyzLabels : XyLabels, values);
                if (EditorGUI.EndChangeCheck())
                    property.vector3IntValue = new Vector3Int(Mathf.Clamp(values[0], 1, 128),
                        Mathf.Clamp(values[1], 2, 128), depth ? Mathf.Clamp(values[2], 1, 128) : cells.z);
            }
            finally
            {
                EditorGUIUtility.labelWidth = oldWidth; EditorGUI.indentLevel = oldIndent;
                EditorGUI.EndProperty();
            }
        }

        private static void CellCounts(SerializedProperty noise, bool depth)
        {
            string[] fields = depth ? new[] { "columns", "rows", "layers" } : new[] { "columns", "rows" };
            int[] values = Array.ConvertAll(fields, f => noise.FindPropertyRelative(f).intValue);
            Rect rect = EditorGUILayout.GetControlRect();
            float labelWidth = EditorGUIUtility.labelWidth;
            int indent = EditorGUI.indentLevel;
            try
            {
                EditorGUIUtility.labelWidth = 88;
                rect = EditorGUI.PrefixLabel(rect, new GUIContent("セル数"));
                EditorGUI.indentLevel = 0;
                EditorGUI.BeginChangeCheck();
                EditorGUI.MultiIntField(rect, depth ? XyzLabels : XyLabels, values);
                if (EditorGUI.EndChangeCheck())
                    for (int i = 0; i < fields.Length; i++)
                        noise.FindPropertyRelative(fields[i]).intValue = Mathf.Clamp(values[i], i == 1 ? 2 : 1, 128);
            }
            finally { EditorGUIUtility.labelWidth = labelWidth; EditorGUI.indentLevel = indent; }
        }

        private static void ChannelColumn(SerializedProperty noise, bool volume)
        {
            string[] fields = { "red", "green", "blue", "alpha" };
            GeneratedChannel[] choices = volume
                ? (GeneratedChannel[])Enum.GetValues(typeof(GeneratedChannel))
                : new[] { GeneratedChannel.Value, GeneratedChannel.U, GeneratedChannel.V, GeneratedChannel.CellId,
                    GeneratedChannel.Zero, GeneratedChannel.One, GeneratedChannel.CenterU, GeneratedChannel.CenterV };
            string[] labels = Array.ConvertAll(choices, c => c.ToString());
            float labelWidth = EditorGUIUtility.labelWidth;
            int indent = EditorGUI.indentLevel;
            try
            {
                EditorGUIUtility.labelWidth = 16;
                EditorGUI.indentLevel = 0;
                for (int i = 0; i < fields.Length; i++)
                {
                    Rect rect = EditorGUILayout.GetControlRect();
                    SerializedProperty property = noise.FindPropertyRelative(fields[i]);
                    EditorGUI.BeginProperty(rect, RgbaLabels[i], property);
                    rect = EditorGUI.PrefixLabel(rect, RgbaLabels[i]);
                    int selected = Array.IndexOf(choices, (GeneratedChannel)property.intValue);
                    EditorGUI.BeginChangeCheck();
                    int next = EditorGUI.Popup(rect, selected, labels);
                    if (EditorGUI.EndChangeCheck() && next >= 0) property.intValue = (int)choices[next];
                    EditorGUI.EndProperty();
                }
            }
            finally { EditorGUIUtility.labelWidth = labelWidth; EditorGUI.indentLevel = indent; }
        }
        private static void Assign(SerializedProperty n, GeneratedChannel r, GeneratedChannel g, GeneratedChannel b)
        {
            n.FindPropertyRelative("red").enumValueIndex = (int)r; n.FindPropertyRelative("green").enumValueIndex = (int)g;
            n.FindPropertyRelative("blue").enumValueIndex = (int)b; n.FindPropertyRelative("alpha").enumValueIndex = (int)GeneratedChannel.One;
        }
        private static void ResetContrast(SerializedProperty p)
        {
            foreach (string c in new[] { "contrastR", "contrastG", "contrastB", "contrastA" }) p.FindPropertyRelative(c).floatValue = 1;
        }
        public static void DrawTone(SerializedProperty p, Action histogram, bool hdr = false)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!AreaFoldout("Tone", "トーン調整")) return;
                histogram?.Invoke();
                FourNumbers(p, "コントラスト", new[] { "contrastR", "contrastG", "contrastB", "contrastA" }, false, () => ResetContrast(p));
                Field(p, "enabled", "トーンカーブを適用");
                if (p.FindPropertyRelative("enabled").boolValue)
                {
                    Curve(p, "rgb", "RGB共通", Color.white, 64, hdr);
                    Curve(p, "red", "R", Color.red, 40, hdr); Curve(p, "green", "G", Color.green, 40, hdr);
                    Curve(p, "blue", "B", Color.cyan, 40, hdr); Curve(p, "alpha", "A", Color.gray, 40, hdr);
                    if (GUILayout.Button("カーブをリセット"))
                        foreach (string field in new[] { "rgb", "red", "green", "blue", "alpha" })
                            p.FindPropertyRelative(field).animationCurveValue = AnimationCurve.Linear(0, 0, 1, 1);
                }
                EditorGUILayout.LabelField("コントラスト → RGB共通カーブ → 各チャンネルカーブ（Aは専用のみ）", EditorStyles.miniLabel);
            }
        }
        private static void Curve(SerializedProperty p, string name, string label, Color color, float height, bool hdr = false)
        {
            SerializedProperty curve = p.FindPropertyRelative(name);
            EditorGUI.BeginChangeCheck();
            AnimationCurve edited = hdr
                ? EditorGUILayout.CurveField(label, curve.animationCurveValue, GUILayout.Height(height))
                : EditorGUILayout.CurveField(label, curve.animationCurveValue, color, new Rect(0, 0, 1, 1), GUILayout.Height(height));
            if (EditorGUI.EndChangeCheck()) curve.animationCurveValue = edited;
        }
    }
}
#endif
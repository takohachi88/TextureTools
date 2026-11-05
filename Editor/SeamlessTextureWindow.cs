#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TextureTools.Editor
{
    public sealed class SeamlessTextureWindow : EditorWindow
    {
        [SerializeField] private Texture2D _source;
        [SerializeField] private bool _horizontal = true, _vertical = true;
        [SerializeField] private float _solidWidth, _solidHeight, _blendWidth = 0.15f, _blendHeight = 0.15f;
        [SerializeField] private TextureGeneratorTone _tone = new TextureGeneratorTone();
        private TextureGeneratorPreview preview;
        private SerializedObject properties;
        private Vector2 scroll;
        private double due;
        private bool pending, previewStarted;
        private string error;
        [MenuItem("Tools/Texture Generator/シームレス加工")]
        private static void Open() => GetWindow<SeamlessTextureWindow>("シームレス加工").Show();
        [MenuItem("Assets/Texture Generator/シームレス加工", false, 2101)]
        private static void OpenSource()
        {
            var window = GetWindow<SeamlessTextureWindow>("シームレス加工");
            window._source = Selection.activeObject as Texture2D; window.Queue(); window.Show();
        }
        [MenuItem("Assets/Texture Generator/シームレス加工", true)]
        private static bool ValidateSource() => Selection.activeObject is Texture2D;
        private void OnEnable()
        {
            minSize = new Vector2(410, 580); preview = new TextureGeneratorPreview(); properties = new SerializedObject(this);
            EditorApplication.update += Tick; Queue();
        }
        private void OnDisable() { EditorApplication.update -= Tick; preview?.Dispose(); }
        private void Queue() { pending = previewStarted && _source != null; error = null; due = EditorApplication.timeSinceStartup + 0.2; if (!pending) preview?.Clear(); Repaint(); }
        private void Tick()
        {
            if (!pending || EditorApplication.timeSinceStartup < due || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            pending = false; Run(false); Repaint();
        }
        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();
            _source = (Texture2D)EditorGUILayout.ObjectField("元テクスチャ", _source, typeof(Texture2D), false);
            _horizontal = EditorGUILayout.Toggle("左右方向を加工", _horizontal); _vertical = EditorGUILayout.Toggle("上下方向を加工", _vertical);
            Band("縦帯（左右方向）", ref _solidWidth, ref _blendWidth);
            Band("横帯（上下方向）", ref _solidHeight, ref _blendHeight);
            EditorGUILayout.HelpBox("画像を半分ずらし、中央の継ぎ目に元画像の帯をフェードして重ねます。フェード加工は常に有効です。\n100%領域は中央の全幅、フェード幅はその片側の幅です。両方向の加工でXYの継ぎ目を処理します。", MessageType.Info);
            if (EditorGUI.EndChangeCheck()) Queue();
            properties.Update();
            TextureGeneratorGUI.DrawTone(properties.FindProperty("_tone"), preview.DrawHistogram);
            properties.ApplyModifiedProperties(); preview.SetTone(_tone);
            if (pending) EditorGUILayout.LabelField("プレビュー更新待ち…", EditorStyles.miniLabel);
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
            preview.Draw(false, () => { previewStarted = true; Queue(); },
                previewStarted ? "プレビューを再生成" : "プレビューを生成", _source != null);
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_source == null))
                if (GUILayout.Button("PNGを保存…")) Run(true);
            EditorGUILayout.EndScrollView();
        }
        private static void Band(string label, ref float solid, ref float fade)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            solid = EditorGUILayout.Slider("100%領域の幅 (UV)", solid, 0, 1);
            fade = EditorGUILayout.Slider("片側のフェード幅 (UV)", Mathf.Min(fade, (1 - solid) * 0.5f), 0, (1 - solid) * 0.5f);
        }
        private void Run(bool save)
        {
            if (_source == null) return;
            string path = null;
            if (save)
            {
                path = EditorUtility.SaveFilePanelInProject("シームレス加工を保存", _source.name + "_Seamless", "png", "保存先を選択");
                if (string.IsNullOrEmpty(path)) return;
                if (string.Equals(path, AssetDatabase.GetAssetPath(_source), StringComparison.OrdinalIgnoreCase))
                { EditorUtility.DisplayDialog("保存先", "元画像とは別のファイル名にしてください。", "OK"); return; }
            }
            Texture2D texture = null;
            try
            {
                texture = TextureGeneratorEngine.Offset(_source, _horizontal, _vertical, true,
                    _blendWidth, _blendHeight, save ? 0 : 256, _solidWidth, _solidHeight);
                if (!save) { preview.SetTone(_tone); preview.SetPixels(texture.GetPixels32(), texture.width, texture.height, !_source.isDataSRGB); return; }
                texture.SetPixels32(_tone.Apply(texture.GetPixels32())); texture.Apply(false, false);
                File.WriteAllBytes(TextureGeneratorAssetUtility.Absolute(path), texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default; importer.sRGBTexture = _source.isDataSRGB;
                importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = false;
                importer.mipmapEnabled = false; importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(texture.width, texture.height));
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport(); EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            }
            catch (Exception e) { error = e.Message; Debug.LogException(e); if (save) EditorUtility.DisplayDialog("シームレス加工", e.Message, "OK"); }
            finally { if (texture != null) DestroyImmediate(texture); }
        }
    }
}
#endif
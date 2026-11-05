#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace TextureTools.Editor
{
    internal sealed class TextureGeneratorInspectorPanel : IDisposable
    {
        private readonly TextureGeneratorPreset data;
        private readonly SerializedObject properties;
        private readonly TextureGeneratorPreview preview = new TextureGeneratorPreview();
        private readonly Action repaint;
        public TextureGeneratorInspectorPanel(TextureGeneratorPreset data, Action repaint)
        {
            this.data = data; this.repaint = repaint;
            properties = new SerializedObject(data); preview.SetRecipe(data.recipe);
            EditorApplication.update += Tick;
        }
        private void Tick() { if (preview.Tick()) repaint(); }
        private static void Run(Action action)
        {
            try { action(); }
            catch (ExitGUIException) { throw; }
            catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("Texture Generator", e.Message, "OK"); }
        }
        public void Draw(bool allowSavePreset = false)
        {
            if (data == null) return;
            properties.Update();
            TextureGeneratorGUI.DrawRecipe(properties.FindProperty("recipe"), preview.DrawHistogram);
            properties.ApplyModifiedProperties();
            preview.SetRecipe(data.recipe);
            EditorGUILayout.Space();

            preview.Draw(data.recipe.IsVolume, preview.Request, preview.HasStarted ? "プレビューを再生成" : "プレビューを生成");
            EditorGUILayout.Space();
            if (allowSavePreset && GUILayout.Button("Presetとして保存…"))
                Run(() => TextureGeneratorAssetUtility.SavePreset(data.recipe));
            if (!data.recipe.IsHDR && data.recipe.mode != TextureGenerationMode.Noise)
                EditorGUILayout.LabelField("PNGは0～1の8bit、EXRはHDR値を保持します。", EditorStyles.miniLabel);
            if (data.recipe.IsVolume)
                EditorGUILayout.LabelField("Zスライスを並べた画像を、標準Importerの3D形状で取り込みます。", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(properties.FindProperty("recipe.output"), GUIContent.none, GUILayout.Width(75));
                properties.ApplyModifiedProperties();
                preview.SetRecipe(data.recipe);
                string prefix = data.recipe.IsVolume ? "3D " : "";
                if (GUILayout.Button($"{prefix}{data.recipe.output}を出力…", GUILayout.ExpandWidth(true)))
                    Run(() => TextureGeneratorAssetUtility.ExportImage(data.recipe));
            }
        }
        public void Dispose() { EditorApplication.update -= Tick; preview.Dispose(); }
    }

    [CustomEditor(typeof(TextureGeneratorPreset))]
    public sealed class TextureGeneratorPresetEditor : UnityEditor.Editor
    {
        private TextureGeneratorInspectorPanel panel;
        private void OnEnable() => panel = new TextureGeneratorInspectorPanel((TextureGeneratorPreset)target, Repaint);
        private void OnDisable() => panel?.Dispose();
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("生成設定を保存するプリセットです。PNGまたはHDR対応EXRへ出力し、Unity標準のTextureImporterで取り込みます。出力画像はこのSOを参照しません。", MessageType.Info);
            panel?.Draw();
        }
    }
}
#endif
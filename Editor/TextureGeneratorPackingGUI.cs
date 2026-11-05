#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace TextureTools.Editor
{
    internal sealed class TextureGeneratorPackingGUI
    {
        private readonly ReorderableList list;
        private static readonly string[] Names = { "R", "G", "B", "A" };

        public TextureGeneratorPackingGUI(SerializedProperty channels)
        {
            list = new ReorderableList(channels.serializedObject, channels, true, true, false, false)
            {
                elementHeight = EditorGUIUtility.singleLineHeight + 6,
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "出力 ← 元テクスチャ / 元チャンネル"),
                onReorderCallback = _ => GUI.changed = true,
                drawElementCallback = (rect, index, active, focused) =>
                {
                    SerializedProperty source = channels.GetArrayElementAtIndex(index);
                    SerializedProperty selectedChannel = source.FindPropertyRelative("channel");
                    rect.y += 3; rect.height = EditorGUIUtility.singleLineHeight;
                    EditorGUI.LabelField(new Rect(rect.x, rect.y, 18, rect.height), Names[index]);
                    Rect channelRect = new Rect(rect.xMax - 72, rect.y, 72, rect.height);
                    Rect textureRect = new Rect(rect.x + 20, rect.y, Mathf.Max(1, rect.width - 98), rect.height);
                    bool constant = selectedChannel.intValue >= (int)TextureSourceChannel.Zero;
                    using (new EditorGUI.DisabledScope(constant))
                        EditorGUI.PropertyField(textureRect, source.FindPropertyRelative("texture"), GUIContent.none);
                    EditorGUI.PropertyField(channelRect, selectedChannel, GUIContent.none);
                }
            };
        }

        public void Draw()
        {
            list.DoLayoutList();
            EditorGUILayout.LabelField("行をドラッグして出力RGBAへの割り当てを変更します。", EditorStyles.miniLabel);
        }
    }
}
#endif
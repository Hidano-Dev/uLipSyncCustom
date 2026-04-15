using UnityEngine;
using UnityEditor;

namespace uLipSync
{

[CustomEditor(typeof(uLipSyncAsioInput))]
public class uLipSyncAsioInputEditor : Editor
{
    uLipSyncAsioInput asio => target as uLipSyncAsioInput;

    public override void OnInspectorGUI()
    {
#if !UNITY_STANDALONE_WIN && !UNITY_EDITOR_WIN
        EditorGUILayout.HelpBox(
            "このコンポーネントは Windows Standalone / Mono 環境でのみ動作します。",
            MessageType.Warning);
#else
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();
#endif
    }
}

}

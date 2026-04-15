using UnityEngine;
using UnityEditor;

namespace uLipSync
{

[CustomEditor(typeof(uLipSyncAsioInput))]
public class uLipSyncAsioInputEditor : Editor
{
    uLipSyncAsioInput asio => target as uLipSyncAsioInput;

    string[] _driverNames = new string[0];

    void OnEnable()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
        RefreshDriverList();
#endif
    }

    public override void OnInspectorGUI()
    {
#if !UNITY_STANDALONE_WIN && !UNITY_EDITOR
        EditorGUILayout.HelpBox(
            "このコンポーネントは Windows Standalone / Mono 環境でのみ動作します。",
            MessageType.Warning);
#else
        serializedObject.Update();

        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledGroupScope(true))
        {
            EditorGUILayout.Toggle("Is Recording", asio.isRecording);
            EditorGUILayout.TextField("Selected Device", asio.selectedDeviceName ?? "(none)");
            EditorGUILayout.IntField("Last Callback Samples", asio.lastCallbackSampleCount);
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("デバイス更新"))
        {
            RefreshDriverList();
        }

        if (_driverNames.Length == 0)
        {
            EditorGUILayout.HelpBox(
                "ASIO ドライバが見つかりません。ASIO ドライバがインストールされているか確認してください。",
                MessageType.Warning);
        }

        if (!serializedObject.FindProperty("isAutoStart").boolValue && EditorApplication.isPlaying)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Manual Control", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledGroupScope(asio.isRecording))
                {
                    if (GUILayout.Button("Start Recording"))
                    {
                        asio.StartRecord();
                    }
                }

                using (new EditorGUI.DisabledGroupScope(!asio.isRecording))
                {
                    if (GUILayout.Button("Stop Recording"))
                    {
                        asio.StopRecord();
                    }
                }
            }
        }

        serializedObject.ApplyModifiedProperties();

        if (EditorApplication.isPlaying)
        {
            Repaint();
        }
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR
    void RefreshDriverList()
    {
        _driverNames = asio.GetAsioDriverNames();
    }
#endif
}

}

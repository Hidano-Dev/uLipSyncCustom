#if UNITY_STANDALONE_WIN || UNITY_EDITOR

using UnityEngine;

namespace uLipSync
{

public class uLipSyncAsioInput : MonoBehaviour, IAudioInputSource
{
    [SerializeField] uLipSync lipSync;
    [SerializeField] bool isAutoStart = true;
    [SerializeField] int inputChannelOffset = 0;
    [SerializeField] int inputChannelCount = 1;

    public bool isRecording { get; private set; }

    public void StartRecord()
    {
    }

    public void StopRecord()
    {
    }
}

}

#else

using UnityEngine;

namespace uLipSync
{

public class uLipSyncAsioInput : MonoBehaviour, IAudioInputSource
{
    public bool isRecording => false;
    public void StartRecord() { }
    public void StopRecord() { }
}

}

#endif

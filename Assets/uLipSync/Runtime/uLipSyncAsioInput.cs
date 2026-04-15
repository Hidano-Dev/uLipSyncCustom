#if UNITY_STANDALONE_WIN || UNITY_EDITOR

using UnityEngine;
using NAudio.Wave;

namespace uLipSync
{

public class uLipSyncAsioInput : MonoBehaviour, IAudioInputSource
{
    [SerializeField] uLipSync lipSync;
    [SerializeField] bool isAutoStart = true;
    [SerializeField] int selectedDeviceIndex = 0;
    [SerializeField] int inputChannelOffset = 0;
    [SerializeField] int inputChannelCount = 1;

    public bool isRecording { get; private set; }

    public string[] GetAsioDriverNames()
    {
        try
        {
            return AsioOut.GetDriverNames();
        }
        catch (System.Exception)
        {
            return new string[0];
        }
    }

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

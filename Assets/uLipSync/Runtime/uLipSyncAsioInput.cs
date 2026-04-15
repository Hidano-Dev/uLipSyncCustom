#if UNITY_STANDALONE_WIN || UNITY_EDITOR

using UnityEngine;
using NAudio.Wave;
using System.Threading;

namespace uLipSync
{

public class uLipSyncAsioInput : MonoBehaviour, IAudioInputSource
{
    static class ErrorCode
    {
        public const int None = 0;
        public const int CallbackException = 1;
        public const int BufferOverflow = 2;
    }

    const int PreAllocFrames = 8192;
    const int PreAllocChannels = 32;

    [SerializeField] uLipSync lipSync;
    [SerializeField] bool isAutoStart = true;
    [SerializeField] int selectedDeviceIndex = 0;
    [SerializeField] int inputChannelOffset = 0;
    [SerializeField] int inputChannelCount = 1;

    float[] _preAllocBuffer;
    int _errorFlag;
    int _cachedSampleRate;
    bool _channelClampWarning;

    public bool isRecording { get; private set; }
    public string selectedDeviceName { get; private set; }
    public int lastCallbackSampleCount { get; private set; }

    void Awake()
    {
        _preAllocBuffer = new float[PreAllocFrames * PreAllocChannels];
    }

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

    public void ValidateChannelRange(int driverInputChannelCount)
    {
        if (inputChannelOffset + inputChannelCount > driverInputChannelCount)
        {
            _channelClampWarning = true;
            if (inputChannelOffset >= driverInputChannelCount)
            {
                inputChannelOffset = 0;
            }
            inputChannelCount = Mathf.Min(inputChannelCount, driverInputChannelCount - inputChannelOffset);
            if (inputChannelCount < 1) inputChannelCount = 1;
        }
    }

    void Update()
    {
        HandleErrorFlag();

        if (_channelClampWarning)
        {
            Debug.LogWarning("[uLipSyncAsioInput] inputChannelOffset + inputChannelCount が ASIO デバイスの最大入力チャンネル数を超えていたため、利用可能な範囲にクランプしました。");
            _channelClampWarning = false;
        }
    }

    void HandleErrorFlag()
    {
        int flag = Interlocked.Exchange(ref _errorFlag, ErrorCode.None);
        if (flag == ErrorCode.None) return;

        switch (flag)
        {
            case ErrorCode.CallbackException:
                Debug.LogError("[uLipSyncAsioInput] コールバック内で例外が発生しました。録音を停止します。");
                StopRecord();
                break;
            case ErrorCode.BufferOverflow:
                Debug.LogError("[uLipSyncAsioInput] バッファサイズが事前確保領域を超過しました。録音を停止します。");
                StopRecord();
                break;
        }
    }

    internal void OnAsioAudioAvailable(object sender, AsioAudioAvailableEventArgs e)
    {
        try
        {
            int totalSamples = e.SamplesPerBuffer * e.InputBuffers.Length;
            if (totalSamples > _preAllocBuffer.Length)
            {
                Interlocked.Exchange(ref _errorFlag, ErrorCode.BufferOverflow);
                return;
            }

            e.GetAsInterleavedSamples(_preAllocBuffer);

            lastCallbackSampleCount = totalSamples;

            if (lipSync != null)
            {
                lipSync.OnDataReceived(_preAllocBuffer, inputChannelCount, _cachedSampleRate);
            }
        }
        catch (System.Exception)
        {
            Interlocked.Exchange(ref _errorFlag, ErrorCode.CallbackException);
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

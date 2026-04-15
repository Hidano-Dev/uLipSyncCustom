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
    float[] _extractedBuffer;
    float[] _validPortion;
    int _errorFlag;
    int _cachedSampleRate;
    bool _channelClampWarning;
    AsioOut _asioOut;
    bool _lipSyncNullWarning;

    public bool isRecording { get; private set; }
    public string selectedDeviceName { get; private set; }
    public int lastCallbackSampleCount { get; private set; }

    void Awake()
    {
        _preAllocBuffer = new float[PreAllocFrames * PreAllocChannels];
        _extractedBuffer = new float[PreAllocFrames * PreAllocChannels];
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

        if (_lipSyncNullWarning)
        {
            Debug.LogWarning("[uLipSyncAsioInput] lipSync フィールドが null のため、データ供給をスキップしています。");
            _lipSyncNullWarning = false;
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
            int totalChannels = e.InputBuffers.Length;
            int samplesPerBuffer = e.SamplesPerBuffer;
            int totalSamples = samplesPerBuffer * totalChannels;
            if (totalSamples > _preAllocBuffer.Length)
            {
                Interlocked.Exchange(ref _errorFlag, ErrorCode.BufferOverflow);
                return;
            }

            e.GetAsInterleavedSamples(_preAllocBuffer);

            int extractedLength = samplesPerBuffer * inputChannelCount;
            for (int i = 0; i < samplesPerBuffer; i++)
            {
                for (int ch = 0; ch < inputChannelCount; ch++)
                {
                    _extractedBuffer[i * inputChannelCount + ch] =
                        _preAllocBuffer[i * totalChannels + inputChannelOffset + ch];
                }
            }

            lastCallbackSampleCount = extractedLength;

            if (_validPortion == null || _validPortion.Length != extractedLength)
            {
                _validPortion = new float[extractedLength];
            }
            System.Array.Copy(_extractedBuffer, _validPortion, extractedLength);

            if (lipSync != null)
            {
                lock (lipSync._lockObject)
                {
                    lipSync.OnDataReceived(_validPortion, inputChannelCount, _cachedSampleRate);
                }
            }
            else
            {
                _lipSyncNullWarning = true;
            }
        }
        catch (System.Exception)
        {
            Interlocked.Exchange(ref _errorFlag, ErrorCode.CallbackException);
        }
    }

    void OnEnable()
    {
        if (isAutoStart)
        {
            var drivers = GetAsioDriverNames();
            if (drivers.Length > 0 && selectedDeviceIndex >= 0 && selectedDeviceIndex < drivers.Length)
            {
                StartRecord();
            }
        }
    }

    void OnDisable()
    {
        if (isRecording) StopRecord();
    }

    void OnDestroy()
    {
        if (isRecording) StopRecord();
    }

    public void StartRecord()
    {
        var drivers = GetAsioDriverNames();
        if (drivers.Length == 0 || selectedDeviceIndex < 0 || selectedDeviceIndex >= drivers.Length)
        {
            Debug.LogError("[uLipSyncAsioInput] 有効な ASIO ドライバが見つかりません。");
            return;
        }

        if (_asioOut != null)
        {
            StopRecord();
        }

        string driverName = drivers[selectedDeviceIndex];

        int desiredSampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

        try
        {
            _asioOut = new AsioOut(driverName);

            ValidateChannelRange(_asioOut.DriverInputChannelCount);

            _asioOut.AudioAvailable += OnAsioAudioAvailable;
            _asioOut.InitRecordAndPlayback(null, inputChannelCount, desiredSampleRate);
            _cachedSampleRate = desiredSampleRate;
            _asioOut.Play();

            selectedDeviceName = driverName;
            isRecording = true;
        }
        catch (System.InvalidOperationException ex) when (ex.Message != null && ex.Message.Contains("busy"))
        {
            Debug.LogError($"[uLipSyncAsioInput] デバイスが使用中です: {driverName}");
            isRecording = false;
            DisposeAsioOut();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[uLipSyncAsioInput] ASIO デバイスの初期化に失敗しました: {ex.Message}");
            isRecording = false;
            DisposeAsioOut();
        }
    }

    public void StopRecord()
    {
        isRecording = false;
        DisposeAsioOut();
    }

    void DisposeAsioOut()
    {
        if (_asioOut == null) return;

        try
        {
            _asioOut.AudioAvailable -= OnAsioAudioAvailable;
            _asioOut.Stop();
        }
        finally
        {
            _asioOut.Dispose();
            _asioOut = null;
        }
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

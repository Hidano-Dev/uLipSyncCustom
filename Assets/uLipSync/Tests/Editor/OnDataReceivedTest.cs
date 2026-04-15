using NUnit.Framework;
using UnityEngine;
using System.Reflection;
using Unity.Collections;

namespace uLipSync.Tests
{

public class OnDataReceivedTest
{
    GameObject _go;
    uLipSync _lipSync;

    static int GetPrivateField<T>(object obj, string name)
    {
        var field = typeof(uLipSync).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return (T)field.GetValue(obj) is T val ? (int)(object)val : 0;
    }

    [SetUp]
    public void SetUp()
    {
        _go = new GameObject("TestLipSync");
        _lipSync = _go.AddComponent<uLipSync>();

        var profileAssets = Resources.FindObjectsOfTypeAll<Profile>();
        if (profileAssets.Length > 0)
        {
            _lipSync.profile = profileAssets[0];
        }
        else
        {
            var profile = ScriptableObject.CreateInstance<Profile>();
            _lipSync.profile = profile;
        }

        var onEnableMethod = typeof(uLipSync).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        onEnableMethod.Invoke(_lipSync, null);
    }

    [TearDown]
    public void TearDown()
    {
        var onDisableMethod = typeof(uLipSync).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);
        onDisableMethod.Invoke(_lipSync, null);
        Object.DestroyImmediate(_go);
    }

    [Test]
    public void OnDataReceived_StoresSampleRate()
    {
        int expectedSampleRate = 48000;
        float[] samples = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
        int channels = 1;

        _lipSync.OnDataReceived(samples, channels, expectedSampleRate);

        var field = typeof(uLipSync).GetField("_cachedSampleRate", BindingFlags.NonPublic | BindingFlags.Instance);
        int actualSampleRate = (int)field.GetValue(_lipSync);
        Assert.AreEqual(expectedSampleRate, actualSampleRate);
    }

    [Test]
    public void OnDataReceived_StoresDifferentSampleRate()
    {
        int expectedSampleRate = 96000;
        float[] samples = new float[] { 0.5f };
        int channels = 1;

        _lipSync.OnDataReceived(samples, channels, expectedSampleRate);

        var field = typeof(uLipSync).GetField("_cachedSampleRate", BindingFlags.NonPublic | BindingFlags.Instance);
        int actualSampleRate = (int)field.GetValue(_lipSync);
        Assert.AreEqual(expectedSampleRate, actualSampleRate);
    }

    [Test]
    public void OnDataReceived_WritesToRawInputData()
    {
        float[] samples = new float[] { 0.25f, 0.5f, 0.75f };
        int channels = 1;
        int sampleRate = 44100;

        _lipSync.OnDataReceived(samples, channels, sampleRate);

        var rawField = typeof(uLipSync).GetField("_rawInputData", BindingFlags.NonPublic | BindingFlags.Instance);
        var rawInputData = (NativeArray<float>)rawField.GetValue(_lipSync);
        Assert.IsTrue(rawInputData.IsCreated);
        Assert.AreEqual(0.25f, rawInputData[0], 0.001f);
        Assert.AreEqual(0.5f, rawInputData[1], 0.001f);
        Assert.AreEqual(0.75f, rawInputData[2], 0.001f);
    }

    [Test]
    public void OnDataReceived_ExtractsFirstChannelFromMultiChannel()
    {
        float[] samples = new float[] { 0.1f, 0.9f, 0.2f, 0.8f, 0.3f, 0.7f };
        int channels = 2;
        int sampleRate = 48000;

        _lipSync.OnDataReceived(samples, channels, sampleRate);

        var rawField = typeof(uLipSync).GetField("_rawInputData", BindingFlags.NonPublic | BindingFlags.Instance);
        var rawInputData = (NativeArray<float>)rawField.GetValue(_lipSync);
        Assert.AreEqual(0.1f, rawInputData[0], 0.001f);
        Assert.AreEqual(0.2f, rawInputData[1], 0.001f);
        Assert.AreEqual(0.3f, rawInputData[2], 0.001f);
    }

    [Test]
    public void OnDataReceived_SetsIsDataReceivedFlag()
    {
        float[] samples = new float[] { 0.1f };
        int channels = 1;
        int sampleRate = 44100;

        _lipSync.OnDataReceived(samples, channels, sampleRate);

        var field = typeof(uLipSync).GetField("_isDataReceived", BindingFlags.NonPublic | BindingFlags.Instance);
        bool isDataReceived = (bool)field.GetValue(_lipSync);
        Assert.IsTrue(isDataReceived);
    }

    [Test]
    public void OnDataReceived_LockContractPreventsDataCorruption()
    {
        int sampleRate = 48000;
        int channels = 1;
        bool corrupted = false;

        var rawField = typeof(uLipSync).GetField("_rawInputData", BindingFlags.NonPublic | BindingFlags.Instance);
        var lockField = typeof(uLipSync).GetField("_lockObject", BindingFlags.NonPublic | BindingFlags.Instance);
        var lockObj = lockField.GetValue(_lipSync);

        var thread = new System.Threading.Thread(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                float[] data = new float[] { i * 0.01f };
                _lipSync.OnDataReceived(data, channels, sampleRate);
            }
        });

        thread.Start();

        for (int i = 0; i < 100; i++)
        {
            lock (lockObj)
            {
                var rawInputData = (NativeArray<float>)rawField.GetValue(_lipSync);
                if (rawInputData.IsCreated && rawInputData.Length > 0)
                {
                    try
                    {
                        float _ = rawInputData[0];
                    }
                    catch
                    {
                        corrupted = true;
                    }
                }
            }
        }

        thread.Join();
        Assert.IsFalse(corrupted, "Data corruption detected during concurrent access");
    }

    [Test]
    public void MockAudioInputSource_CanControlRecordingState()
    {
        var mock = new MockAudioInputSource();

        Assert.IsFalse(mock.isRecording);
        mock.StartRecord();
        Assert.IsTrue(mock.isRecording);
        mock.StopRecord();
        Assert.IsFalse(mock.isRecording);
    }

    [Test]
    public void OnDataReceived_AcceptsDataFromMockSource()
    {
        var mock = new MockAudioInputSource();
        mock.StartRecord();

        float[] samples = new float[] { 0.3f, 0.6f };
        int channels = 1;
        int sampleRate = 44100;

        if (mock.isRecording)
        {
            _lipSync.OnDataReceived(samples, channels, sampleRate);
        }

        var sampleRateField = typeof(uLipSync).GetField("_cachedSampleRate", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.AreEqual(sampleRate, (int)sampleRateField.GetValue(_lipSync));

        var rawField = typeof(uLipSync).GetField("_rawInputData", BindingFlags.NonPublic | BindingFlags.Instance);
        var rawInputData = (NativeArray<float>)rawField.GetValue(_lipSync);
        Assert.AreEqual(0.3f, rawInputData[0], 0.001f);
        Assert.AreEqual(0.6f, rawInputData[1], 0.001f);

        mock.StopRecord();
    }
}

}

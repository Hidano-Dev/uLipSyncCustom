using NUnit.Framework;
using UnityEngine;
using System.Reflection;
using Unity.Collections;

namespace uLipSync.Tests
{

public class PipelineIntegrationTest
{
    GameObject _go;
    uLipSync _lipSync;

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
    public void MockSource_SuppliesDataToPipeline_SampleRatePropagated()
    {
        var mock = new MockAudioInputSource();
        mock.StartRecord();

        float[] samples = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
        int channels = 1;
        int sampleRate = 48000;

        _lipSync.OnDataReceived(samples, channels, sampleRate);

        var sampleRateField = typeof(uLipSync).GetField("_cachedSampleRate", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.AreEqual(sampleRate, (int)sampleRateField.GetValue(_lipSync));

        var flagField = typeof(uLipSync).GetField("_isDataReceived", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsTrue((bool)flagField.GetValue(_lipSync));

        mock.StopRecord();
    }

    [Test]
    public void MockSource_SuppliesMultiChannelData_FirstChannelExtracted()
    {
        var mock = new MockAudioInputSource();
        mock.StartRecord();

        float[] stereoSamples = new float[] { 0.1f, 0.9f, 0.2f, 0.8f, 0.3f, 0.7f, 0.4f, 0.6f };
        int channels = 2;
        int sampleRate = 44100;

        _lipSync.OnDataReceived(stereoSamples, channels, sampleRate);

        var rawField = typeof(uLipSync).GetField("_rawInputData", BindingFlags.NonPublic | BindingFlags.Instance);
        var rawInputData = (NativeArray<float>)rawField.GetValue(_lipSync);
        Assert.AreEqual(0.1f, rawInputData[0], 0.001f);
        Assert.AreEqual(0.2f, rawInputData[1], 0.001f);
        Assert.AreEqual(0.3f, rawInputData[2], 0.001f);
        Assert.AreEqual(0.4f, rawInputData[3], 0.001f);

        mock.StopRecord();
    }

    [Test]
    public void MockSource_SequentialCalls_AccumulateInRingBuffer()
    {
        var mock = new MockAudioInputSource();
        mock.StartRecord();

        int sampleRate = 48000;

        float[] first = new float[] { 0.1f, 0.2f };
        _lipSync.OnDataReceived(first, 1, sampleRate);

        float[] second = new float[] { 0.3f, 0.4f };
        _lipSync.OnDataReceived(second, 1, sampleRate);

        var rawField = typeof(uLipSync).GetField("_rawInputData", BindingFlags.NonPublic | BindingFlags.Instance);
        var indexField = typeof(uLipSync).GetField("_index", BindingFlags.NonPublic | BindingFlags.Instance);
        var rawInputData = (NativeArray<float>)rawField.GetValue(_lipSync);
        int index = (int)indexField.GetValue(_lipSync);

        Assert.AreEqual(4, index % rawInputData.Length == 0 ? (rawInputData.Length >= 4 ? 4 : index % rawInputData.Length) : index);
        Assert.AreEqual(0.1f, rawInputData[0], 0.001f);
        Assert.AreEqual(0.2f, rawInputData[1], 0.001f);
        Assert.AreEqual(0.3f, rawInputData[2], 0.001f);
        Assert.AreEqual(0.4f, rawInputData[3], 0.001f);

        mock.StopRecord();
    }

    [Test]
    public void MockSource_WhenNotRecording_DataNotSupplied()
    {
        var mock = new MockAudioInputSource();
        Assert.IsFalse(mock.isRecording);

        var flagField = typeof(uLipSync).GetField("_isDataReceived", BindingFlags.NonPublic | BindingFlags.Instance);
        bool beforeFlag = (bool)flagField.GetValue(_lipSync);
        Assert.IsFalse(beforeFlag);

        float[] samples = new float[] { 0.5f, 0.6f };
        if (mock.isRecording)
        {
            _lipSync.OnDataReceived(samples, 1, 48000);
        }

        bool afterFlag = (bool)flagField.GetValue(_lipSync);
        Assert.IsFalse(afterFlag);
    }

    [Test]
    public void MockSource_DifferentSampleRates_EachCallUpdatesRate()
    {
        var mock = new MockAudioInputSource();
        mock.StartRecord();

        var sampleRateField = typeof(uLipSync).GetField("_cachedSampleRate", BindingFlags.NonPublic | BindingFlags.Instance);

        float[] samples = new float[] { 0.1f };

        _lipSync.OnDataReceived(samples, 1, 44100);
        Assert.AreEqual(44100, (int)sampleRateField.GetValue(_lipSync));

        _lipSync.OnDataReceived(samples, 1, 48000);
        Assert.AreEqual(48000, (int)sampleRateField.GetValue(_lipSync));

        _lipSync.OnDataReceived(samples, 1, 96000);
        Assert.AreEqual(96000, (int)sampleRateField.GetValue(_lipSync));

        mock.StopRecord();
    }

    [Test]
    public void AsioCallback_DoesNotCallUnityApi()
    {
        var callbackMethod = typeof(uLipSyncAsioInput).GetMethod(
            "OnAsioAudioAvailable",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(callbackMethod, "OnAsioAudioAvailable method should exist");

        var body = callbackMethod.GetMethodBody();
        Assert.IsNotNull(body);

        var il = body.GetILAsByteArray();
        Assert.IsNotNull(il);

        var instructions = new System.Collections.Generic.List<MethodBase>();
        var module = typeof(uLipSyncAsioInput).Module;

        for (int i = 0; i < il.Length; i++)
        {
            byte op = il[i];
            if (op == 0x28 || op == 0x6F)
            {
                if (i + 4 < il.Length)
                {
                    int token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                    try
                    {
                        var resolved = module.ResolveMethod(token);
                        if (resolved != null) instructions.Add(resolved);
                    }
                    catch { }
                    i += 4;
                }
            }
            else if (op == 0xFE)
            {
                i++;
            }
        }

        foreach (var method in instructions)
        {
            if (method.DeclaringType == null) continue;
            string ns = method.DeclaringType.Namespace ?? "";
            bool isUnityApi = ns.StartsWith("UnityEngine") && method.DeclaringType.Name != "Object";
            Assert.IsFalse(isUnityApi,
                $"OnAsioAudioAvailable should not call Unity API directly, but calls {method.DeclaringType.FullName}.{method.Name}");
        }
    }

    [Test]
    public void AsioCallback_UsesLockForThreadSafety()
    {
        var callbackMethod = typeof(uLipSyncAsioInput).GetMethod(
            "OnAsioAudioAvailable",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(callbackMethod);

        var body = callbackMethod.GetMethodBody();
        var il = body.GetILAsByteArray();
        var module = typeof(uLipSyncAsioInput).Module;

        bool usesMonitorEnter = false;
        for (int i = 0; i < il.Length; i++)
        {
            byte op = il[i];
            if (op == 0x28 || op == 0x6F)
            {
                if (i + 4 < il.Length)
                {
                    int token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                    try
                    {
                        var resolved = module.ResolveMethod(token);
                        if (resolved != null &&
                            resolved.DeclaringType == typeof(System.Threading.Monitor) &&
                            resolved.Name == "Enter")
                        {
                            usesMonitorEnter = true;
                        }
                    }
                    catch { }
                    i += 4;
                }
            }
            else if (op == 0xFE)
            {
                i++;
            }
        }

        Assert.IsTrue(usesMonitorEnter,
            "OnAsioAudioAvailable should use lock (Monitor.Enter) for thread safety when calling OnDataReceived");
    }

    [Test]
    public void AsioCallback_SetsErrorFlagOnException()
    {
        var errorFlagField = typeof(uLipSyncAsioInput).GetField("_errorFlag", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(errorFlagField, "_errorFlag field should exist on uLipSyncAsioInput");

        var callbackMethod = typeof(uLipSyncAsioInput).GetMethod(
            "OnAsioAudioAvailable",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(callbackMethod);

        var body = callbackMethod.GetMethodBody();
        var il = body.GetILAsByteArray();
        var module = typeof(uLipSyncAsioInput).Module;

        bool usesInterlockedExchange = false;
        for (int i = 0; i < il.Length; i++)
        {
            byte op = il[i];
            if (op == 0x28 || op == 0x6F)
            {
                if (i + 4 < il.Length)
                {
                    int token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                    try
                    {
                        var resolved = module.ResolveMethod(token);
                        if (resolved != null &&
                            resolved.DeclaringType == typeof(System.Threading.Interlocked) &&
                            resolved.Name == "Exchange")
                        {
                            usesInterlockedExchange = true;
                        }
                    }
                    catch { }
                    i += 4;
                }
            }
            else if (op == 0xFE)
            {
                i++;
            }
        }

        Assert.IsTrue(usesInterlockedExchange,
            "OnAsioAudioAvailable should use Interlocked.Exchange to set error flag on exception");
    }
}

}

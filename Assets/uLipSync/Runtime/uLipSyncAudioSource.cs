using UnityEngine;

namespace uLipSync
{

[RequireComponent(typeof(AudioSource))]
public class uLipSyncAudioSource : MonoBehaviour
{
    public AudioFilterReadEvent onAudioFilterRead { get; private set; } = new AudioFilterReadEvent();

    [System.NonSerialized] volatile int _cachedOutputSampleRate = 0;

    void OnEnable()
    {
        _cachedOutputSampleRate = AudioSettings.outputSampleRate;
        AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
    }

    void OnDisable()
    {
        AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
    }

    void OnAudioConfigurationChanged(bool deviceWasChanged)
    {
        _cachedOutputSampleRate = AudioSettings.outputSampleRate;
    }

    void OnAudioFilterRead(float[] input, int channels)
    {
        if (onAudioFilterRead != null)
        {
            onAudioFilterRead.Invoke(input, channels, _cachedOutputSampleRate);
        }
    }
}

}

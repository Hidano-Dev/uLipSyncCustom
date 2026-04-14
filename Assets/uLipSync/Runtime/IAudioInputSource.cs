namespace uLipSync
{
    public interface IAudioInputSource
    {
        bool isRecording { get; }
        void StartRecord();
        void StopRecord();
    }
}

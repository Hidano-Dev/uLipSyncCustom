namespace uLipSync.Tests
{
    public class MockAudioInputSource : IAudioInputSource
    {
        public bool isRecording { get; private set; }

        public void StartRecord()
        {
            isRecording = true;
        }

        public void StopRecord()
        {
            isRecording = false;
        }
    }
}

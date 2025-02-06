using UnityEngine;

namespace MBaske.Sensors.Audio
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioSensorComponent : MonoBehaviour
    {
        private AudioSource audioSource;
        private AudioBuffer audioBuffer;

        void Start()
        {
            audioSource = GetComponent<AudioSource>();
            audioBuffer = new AudioBuffer(new SensorObservationShape(SignalType.Mono, 1024, 1, 1));
        }

        void Update()
        {
            CaptureAudio();
        }

        public void CaptureAudio()
        {
            // Capture audio data
            float[] samples = new float[audioSource.clip.samples * audioSource.clip.channels];
            audioSource.clip.GetData(samples, 0);

            // Process and buffer audio data
            audioBuffer.ProcessAudio(samples, audioSource.clip.channels);

            // Extract features and get the 3-D vector
            Vector3 audioFeatures = audioBuffer.ExtractFeatures();

            // Use the 3-D vector for your ML model or other purposes
            // Example: Debug.Log(audioFeatures);
        }
    }
}
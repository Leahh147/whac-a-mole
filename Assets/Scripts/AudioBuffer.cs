using System;
using UnityEngine;

namespace MBaske.Sensors.Audio
{
    public class AudioBuffer
    {
        protected int m_TotalChannels;
        protected int m_SignalChannels;
        protected int m_CurrentChannel;
        protected int m_SampleCount;
        protected float[,] m_Samples;

        public AudioBuffer(SensorObservationShape shape)
        {
            m_TotalChannels = shape.Channels;
            m_SignalChannels = shape.SignalChannels;
            m_Samples = new float[m_TotalChannels, shape.Width * shape.Height];
        }

        public void ProcessAudio(float[] samples, int channels)
        {
            // Convert the audio signal format as needed
            int sampleCount = samples.Length / channels;
            float[] monoSamples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float sum = 0;
                for (int j = 0; j < channels; j++)
                {
                    sum += samples[i * channels + j];
                }
                monoSamples[i] = sum / channels;
            }

            // Store the processed samples in the buffer
            m_Samples = new float[1, sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                m_Samples[0, i] = monoSamples[i];
            }
        }

        public Vector3 ExtractFeatures()
        {
            // Example feature extraction: Calculate mean, variance, and max amplitude
            float mean = 0;
            float variance = 0;
            float maxAmplitude = float.MinValue;
            int sampleCount = m_Samples.GetLength(1);

            for (int i = 0; i < sampleCount; i++)
            {
                float sample = m_Samples[0, i];
                mean += sample;
                variance += sample * sample;
                if (sample > maxAmplitude)
                {
                    maxAmplitude = sample;
                }
            }

            mean /= sampleCount;
            variance = variance / sampleCount - mean * mean;

            return new Vector3(mean, variance, maxAmplitude);
        }
    }
}
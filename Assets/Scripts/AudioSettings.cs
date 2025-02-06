using UnityEngine;

namespace MBaske.Sensors.Audio
{
    /// <summary>
    /// SignalType.Stereo samples left and right channel values separately.
    /// SignalType.Mono samples the mean values of left and right channels.
    /// </summary>
    public enum SignalType
    {
        Stereo, Mono
    }

    /// <summary>
    /// SampleType.Amplitude samples amplitude values <see cref="AudioListener.GetOutputData"/>.
    /// SampleType.Spectrum samples FFT band values <see cref="AudioListener.GetSpectrumData"/>.
    /// </summary>
    public enum SampleType
    {
        Amplitude, Spectrum
    }

    /// <summary>
    /// Observation shape of the <see cref="AudioSensor"/>.
    /// </summary>
    [System.Serializable]
    public struct SensorObservationShape
    {
        public SignalType SignalType;
        public int SignalChannels => SignalType == SignalType.Stereo ? 2 : 1;

        public int BufferLength
        {
            set { Channels = value * SignalChannels; }
        }

        public int Channels { get; private set; }
        public int Width { get; }
        public int Height { get; }

        public SensorObservationShape(SignalType signalType, int bufferLength, int width, int height)
        {
            SignalType = signalType;
            Width = width;
            Height = height;
            Channels = bufferLength * 1; // mono sound so channel number is one
        }

        public int[] ToArray() => new[] { Height, Width, Channels };
        public override string ToString() => $"Sensor shape: {Height} x {Width} x {Channels}";
    }
}
using System;
using NAudio.Wave;
using NAudio.Dsp;

namespace MediaOverlay.Services;

public class AudioVisualizerService : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private readonly float[] _fftBuffer;
    private readonly Complex[] _complexBuffer;
    private int _fftPos;
    private readonly int _fftLength = 1024;
    private readonly int _m = 10; // 2^10 = 1024

    public float[] SpectrumData { get; private set; }

    private static AudioVisualizerService? _instance;
    public static AudioVisualizerService Instance => _instance ??= new AudioVisualizerService();

    private AudioVisualizerService(int bands = 32)
    {
        SpectrumData = new float[bands];
        _fftBuffer = new float[_fftLength];
        _complexBuffer = new Complex[_fftLength];
    }

    public void Start()
    {
        if (_capture != null) return;
        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
        }
        catch (Exception)
        {
            // Fail silently if loopback is unavailable (e.g. no devices)
        }
    }

    public void Stop()
    {
        if (_capture == null) return;
        _capture.StopRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture == null) return;

        int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
        int channels = _capture.WaveFormat.Channels;

        for (int i = 0; i < e.BytesRecorded; i += bytesPerSample * channels)
        {
            if (i + 3 >= e.BytesRecorded) break;

            // Reading one channel (Left typically)
            float sample = BitConverter.ToSingle(e.Buffer, i);

            _fftBuffer[_fftPos] = sample;
            _fftPos++;

            if (_fftPos >= _fftLength)
            {
                ComputeFFT();
                _fftPos = 0; // overlap could be done here, but reset is fine for simple visualizer
            }
        }
    }

    private void ComputeFFT()
    {
        for (int i = 0; i < _fftLength; i++)
        {
            double window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (_fftLength - 1)));
            _complexBuffer[i].X = (float)(_fftBuffer[i] * window);
            _complexBuffer[i].Y = 0;
        }

        FastFourierTransform.FFT(true, _m, _complexBuffer);

        int usableBins = _fftLength / 2; // Nyquist limit
        int spectrumBands = SpectrumData.Length;

        // Discard DC offset and extremely high frequencies
        int startCutoff = 2; // Skip first few bins
        int endCutoff = usableBins / 2; // Only go up to ~11kHz
        int totalUsable = endCutoff - startCutoff;
        
        // Logarithmic scale for bins looks better
        for (int i = 0; i < spectrumBands; i++)
        {
            float maxBandVal = 0f;
            
            // Non-linear mapping so lower frequencies get more resolution
            double expRatioStart = Math.Pow((double)i / spectrumBands, 2);
            double expRatioEnd = Math.Pow((double)(i + 1) / spectrumBands, 2);
            
            int startBin = startCutoff + (int)(expRatioStart * totalUsable);
            int endBin = startCutoff + (int)(expRatioEnd * totalUsable);
            if (endBin <= startBin) endBin = startBin + 1;
            if (endBin > usableBins) endBin = usableBins;

            for (int j = startBin; j < endBin; j++)
            {
                float magnitude = (float)Math.Sqrt(_complexBuffer[j].X * _complexBuffer[j].X + _complexBuffer[j].Y * _complexBuffer[j].Y);
                if (magnitude > maxBandVal) maxBandVal = magnitude;
            }

            // Scale factor depending on frequency (higher frequencies need a boost)
            float freqBoost = 1.0f + (float)i / spectrumBands * 4.0f;
            float targetVal = maxBandVal * 100f * freqBoost;
            
            if (targetVal > 1.0f) targetVal = 1.0f;
            if (targetVal < 0.0f) targetVal = 0.0f;

            // Smoothing
            const float attack = 0.6f;
            const float decay = 0.15f;

            if (targetVal > SpectrumData[i])
                SpectrumData[i] += (targetVal - SpectrumData[i]) * attack;
            else
                SpectrumData[i] += (targetVal - SpectrumData[i]) * decay;
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _capture?.Dispose();
        _capture = null;
        
        // Auto restart after a small delay if it unexpectedly stopped (e.g. device switch)
        // using a timer or just leave it for now.
    }

    public void Dispose()
    {
        Stop();
        _capture?.Dispose();
        _capture = null;
    }
}

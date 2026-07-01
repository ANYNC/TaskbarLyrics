using System.Runtime.InteropServices;
using System.Threading;

namespace TaskbarLyrics.Light.App;

public sealed class SystemAudioSpectrumService : IDisposable
{
    private const int BarCount = 24;
    private const int RingBufferSize = 8192;
    private const int ClsctxAll = 23;
    private const int AudclntSharemodeShared = 0;
    private const int AudclntStreamflagsLoopback = 0x00020000;
    private const int AudclntBufferflagsSilent = 0x00000002;
    private const short WaveFormatPcm = 1;
    private const short WaveFormatIeeeFloat = 3;
    private const short WaveFormatExtensibleTag = -2;

    private static readonly Guid IAudioClientId = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static readonly Guid IAudioCaptureClientId = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly object _sync = new();
    private readonly float[] _ringBuffer = new float[RingBufferSize];
    private readonly float[] _smoothedBars = new float[BarCount];
    private CancellationTokenSource _cts = new();
    private Thread? _captureThread;
    private int _writeIndex;
    private int _sampleRate = 48000;
    private float _adaptivePeak = 0.035f;
    private SpectrumTuningSettings _tuningSettings = SpectrumTuningSettings.CreateDefault();
    private bool _isStarted;
    private volatile bool _isAvailable;
    private int _diagnosticSampleRate;
    private int _diagnosticChannels;
    private float _diagnosticInputPeak;
    private DateTimeOffset? _lastAudioUtc;
    private string _diagnosticFormat = "Waiting";
    private string _lastError = string.Empty;

    public static IReadOnlyList<float> Silence { get; } = new float[BarCount];

    public bool IsAvailable => _isAvailable;

    public bool IsStarted => _isStarted;

    public SpectrumCaptureDiagnostics GetDiagnostics()
    {
        lock (_sync)
        {
            return new SpectrumCaptureDiagnostics(
                _isAvailable,
                _diagnosticSampleRate,
                _diagnosticChannels,
                _diagnosticFormat,
                _diagnosticInputPeak,
                _lastAudioUtc,
                _lastError);
        }
    }

    public void ApplyTuning(SpectrumTuningSettings settings)
    {
        var snapshot = settings.Clone();
        snapshot.SampleWindow = CoerceSampleWindow(snapshot.SampleWindow);
        snapshot.UpdateIntervalMs = Math.Clamp(snapshot.UpdateIntervalMs, 16, 100);
        snapshot.MinFrequency = Math.Clamp(snapshot.MinFrequency, 20, 300);
        snapshot.MaxFrequency = Math.Clamp(snapshot.MaxFrequency, 2000, 20000);
        if (snapshot.MaxFrequency <= snapshot.MinFrequency)
        {
            snapshot.MaxFrequency = snapshot.MinFrequency + 1000;
        }

        snapshot.PeakInitial = Math.Clamp(snapshot.PeakInitial, 0.004, 0.2);
        snapshot.PeakDecay = Math.Clamp(snapshot.PeakDecay, 0.85, 0.995);
        snapshot.PeakFloor = Math.Clamp(snapshot.PeakFloor, 0.003, 0.08);
        snapshot.PeakCeiling = Math.Clamp(snapshot.PeakCeiling, snapshot.PeakFloor, 1.0);
        snapshot.NoiseFloor = Math.Clamp(snapshot.NoiseFloor, 0, 0.25);
        snapshot.OutputCurve = Math.Clamp(snapshot.OutputCurve, 0.25, 1.5);
        snapshot.LowBandGain = Math.Clamp(snapshot.LowBandGain, 0.2, 4);
        snapshot.BandGainStep = Math.Clamp(snapshot.BandGainStep, -0.05, 0.12);
        snapshot.FrequencyWeightBase = Math.Clamp(snapshot.FrequencyWeightBase, 0.2, 2.5);
        snapshot.FrequencyWeightSlope = Math.Clamp(snapshot.FrequencyWeightSlope, -0.1, 0.2);
        snapshot.BackendAttack = Math.Clamp(snapshot.BackendAttack, 0.05, 1);
        snapshot.BackendRelease = Math.Clamp(snapshot.BackendRelease, 0.02, 1);

        lock (_sync)
        {
            _tuningSettings = snapshot;
            _adaptivePeak = Math.Clamp(_adaptivePeak, (float)snapshot.PeakFloor, (float)snapshot.PeakCeiling);
        }
    }

    public void Start()
    {
        if (_isStarted)
        {
            return;
        }

        if (_cts.IsCancellationRequested)
        {
            _cts.Dispose();
            _cts = new CancellationTokenSource();
        }

        _isStarted = true;
        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "TaskbarLyrics Audio Spectrum"
        };
        _captureThread.SetApartmentState(ApartmentState.MTA);
        _captureThread.Start();
    }

    public void Stop()
    {
        if (!_isStarted)
        {
            return;
        }

        _isStarted = false;
        _isAvailable = false;
        _cts.Cancel();
        if (_captureThread is not null && _captureThread.IsAlive)
        {
            _captureThread.Join(500);
        }

        _captureThread = null;
        Array.Clear(_smoothedBars, 0, _smoothedBars.Length);
        lock (_sync)
        {
            Array.Clear(_ringBuffer, 0, _ringBuffer.Length);
            _writeIndex = 0;
        }
    }

    public float[] GetSpectrum()
    {
        SpectrumTuningSettings settings;
        lock (_sync)
        {
            settings = _tuningSettings.Clone();
        }

        var sampleWindow = CoerceSampleWindow(settings.SampleWindow);
        var samples = new float[sampleWindow];
        int sampleRate;

        lock (_sync)
        {
            sampleRate = _sampleRate;
            var start = (_writeIndex - sampleWindow + RingBufferSize) % RingBufferSize;
            for (var i = 0; i < sampleWindow; i++)
            {
                samples[i] = _ringBuffer[(start + i) % RingBufferSize];
            }
        }

        var bars = CalculateBars(samples, sampleRate, settings);
        lock (_sync)
        {
            for (var i = 0; i < BarCount; i++)
            {
                var attack = bars[i] > _smoothedBars[i]
                    ? (float)settings.BackendAttack
                    : (float)settings.BackendRelease;
                _smoothedBars[i] += (bars[i] - _smoothedBars[i]) * attack;
                bars[i] = Math.Clamp(_smoothedBars[i], 0f, 1f);
            }
        }

        return bars;
    }

    private void CaptureLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                RunCaptureSession();
            }
            catch (Exception ex)
            {
                _isAvailable = false;
                lock (_sync)
                {
                    _lastError = $"{ex.GetType().Name}: {ex.Message}";
                }
            }

            if (!_cts.IsCancellationRequested)
            {
                _cts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            }
        }
    }

    private void RunCaptureSession()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? audioClient = null;
        IAudioCaptureClient? captureClient = null;
        IntPtr mixFormatPtr = IntPtr.Zero;

        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device));
            var audioClientId = IAudioClientId;
            Marshal.ThrowExceptionForHR(device.Activate(ref audioClientId, ClsctxAll, IntPtr.Zero, out var audioClientObject));
            audioClient = (IAudioClient)audioClientObject;
            Marshal.ThrowExceptionForHR(audioClient.GetMixFormat(out mixFormatPtr));
            var format = AudioFormat.FromWaveFormat(mixFormatPtr);
            _sampleRate = format.SampleRate;
            lock (_sync)
            {
                _diagnosticSampleRate = format.SampleRate;
                _diagnosticChannels = format.Channels;
                _diagnosticFormat = $"{format.SampleRate} Hz, {format.Channels} ch, {format.BytesPerSample * 8}-bit" +
                    (format.IsFloat ? " float" : " pcm");
            }

            Marshal.ThrowExceptionForHR(audioClient.Initialize(
                AudclntSharemodeShared,
                AudclntStreamflagsLoopback,
                1_000_000,
                0,
                mixFormatPtr,
                IntPtr.Zero));

            var audioCaptureClientId = IAudioCaptureClientId;
            Marshal.ThrowExceptionForHR(audioClient.GetService(ref audioCaptureClientId, out var captureClientObject));
            captureClient = (IAudioCaptureClient)captureClientObject;
            Marshal.ThrowExceptionForHR(audioClient.Start());
            _isAvailable = true;
            lock (_sync)
            {
                _lastError = string.Empty;
            }

            while (!_cts.IsCancellationRequested)
            {
                DrainCaptureClient(captureClient, format);
                _cts.Token.WaitHandle.WaitOne(15);
            }
        }
        finally
        {
            _isAvailable = false;
            try
            {
                audioClient?.Stop();
            }
            catch
            {
            }

            if (mixFormatPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(mixFormatPtr);
            }

            ReleaseCom(captureClient);
            ReleaseCom(audioClient);
            ReleaseCom(device);
            ReleaseCom(enumerator);
        }
    }

    private void DrainCaptureClient(IAudioCaptureClient captureClient, AudioFormat format)
    {
        Marshal.ThrowExceptionForHR(captureClient.GetNextPacketSize(out var packetFrames));
        while (packetFrames > 0)
        {
            Marshal.ThrowExceptionForHR(captureClient.GetBuffer(
                out var data,
                out var frameCount,
                out var flags,
                out _,
                out _));

            try
            {
                if ((flags & AudclntBufferflagsSilent) != 0)
                {
                    AppendSilence(frameCount);
                }
                else
                {
                    AppendSamples(data, frameCount, format);
                }
            }
            finally
            {
                Marshal.ThrowExceptionForHR(captureClient.ReleaseBuffer(frameCount));
            }

            Marshal.ThrowExceptionForHR(captureClient.GetNextPacketSize(out packetFrames));
        }
    }

    private void AppendSilence(int frameCount)
    {
        lock (_sync)
        {
            _diagnosticInputPeak = 0;
            for (var i = 0; i < frameCount; i++)
            {
                _ringBuffer[_writeIndex] = 0;
                _writeIndex = (_writeIndex + 1) % RingBufferSize;
            }
        }
    }

    private void AppendSamples(IntPtr data, int frameCount, AudioFormat format)
    {
        var byteCount = frameCount * format.BlockAlign;
        var bytes = new byte[byteCount];
        Marshal.Copy(data, bytes, 0, byteCount);

        lock (_sync)
        {
            var peak = 0f;
            for (var frame = 0; frame < frameCount; frame++)
            {
                var sum = 0f;
                var frameOffset = frame * format.BlockAlign;
                for (var channel = 0; channel < format.Channels; channel++)
                {
                    sum += ReadSample(bytes, frameOffset + (channel * format.BytesPerSample), format);
                }

                var sample = sum / Math.Max(1, format.Channels);
                peak = Math.Max(peak, Math.Abs(sample));
                _ringBuffer[_writeIndex] = sample;
                _writeIndex = (_writeIndex + 1) % RingBufferSize;
            }

            _diagnosticInputPeak = peak;
            if (peak > 0.0001f)
            {
                _lastAudioUtc = DateTimeOffset.UtcNow;
            }
        }
    }

    private static float ReadSample(byte[] bytes, int offset, AudioFormat format)
    {
        if (format.IsFloat && format.BytesPerSample >= 4)
        {
            return Math.Clamp(BitConverter.ToSingle(bytes, offset), -1f, 1f);
        }

        return format.BytesPerSample switch
        {
            2 => Math.Clamp(BitConverter.ToInt16(bytes, offset) / 32768f, -1f, 1f),
            3 => Read24BitSample(bytes, offset),
            4 => Math.Clamp(BitConverter.ToInt32(bytes, offset) / 2147483648f, -1f, 1f),
            _ => 0f
        };
    }

    private static float Read24BitSample(byte[] bytes, int offset)
    {
        var sample = bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);
        if ((sample & 0x800000) != 0)
        {
            sample |= unchecked((int)0xFF000000);
        }

        return Math.Clamp(sample / 8388608f, -1f, 1f);
    }

    private float[] CalculateBars(float[] samples, int sampleRate, SpectrumTuningSettings settings)
    {
        var bars = new float[BarCount];
        var magnitudes = CalculateFftMagnitudes(samples);
        var nyquist = Math.Max(1000, sampleRate / 2);
        var minFrequency = Math.Clamp(settings.MinFrequency, 20, 300);
        var maxFrequency = Math.Min(Math.Clamp(settings.MaxFrequency, 2000, 20000), nyquist * 0.92);
        if (maxFrequency <= minFrequency)
        {
            maxFrequency = Math.Min(nyquist * 0.92, minFrequency + 1000);
        }

        var peak = 0f;

        for (var band = 0; band < BarCount; band++)
        {
            var startRatio = band / (double)BarCount;
            var endRatio = (band + 1) / (double)BarCount;
            var startFrequency = minFrequency * Math.Pow(maxFrequency / minFrequency, startRatio);
            var endFrequency = minFrequency * Math.Pow(maxFrequency / minFrequency, endRatio);
            var startBin = Math.Max(1, (int)Math.Floor(startFrequency * samples.Length / sampleRate));
            var endBin = Math.Min(magnitudes.Length - 1, (int)Math.Ceiling(endFrequency * samples.Length / sampleRate));
            var weightedSum = 0.0;
            var weightTotal = 0.0;

            for (var bin = startBin; bin <= endBin; bin++)
            {
                var frequency = bin * sampleRate / (double)samples.Length;
                var frequencyWeight = settings.FrequencyWeightBase +
                    (Math.Log2(Math.Max(2, frequency) / minFrequency) * settings.FrequencyWeightSlope);
                weightedSum += magnitudes[bin] * Math.Max(0.75, frequencyWeight);
                weightTotal += 1.0;
            }

            var magnitude = weightTotal > 0 ? (float)(weightedSum / weightTotal) : 0f;
            var bandGain = (float)(settings.LowBandGain + (band * settings.BandGainStep));
            bars[band] = magnitude * bandGain;
            peak = Math.Max(peak, bars[band]);
        }

        _adaptivePeak = Math.Clamp(
            Math.Max(peak, _adaptivePeak * (float)settings.PeakDecay),
            (float)settings.PeakFloor,
            (float)settings.PeakCeiling);
        for (var band = 0; band < BarCount; band++)
        {
            var normalized = Math.Clamp(bars[band] / _adaptivePeak, 0f, 1.35f);
            var noiseFloor = (float)settings.NoiseFloor;
            normalized = MathF.Max(0, normalized - noiseFloor) / MathF.Max(0.001f, 1 - noiseFloor);
            bars[band] = Math.Clamp(MathF.Pow(normalized, (float)settings.OutputCurve), 0f, 1f);
        }

        return bars;
    }

    private static int CoerceSampleWindow(int value)
    {
        return value switch
        {
            <= 512 => 512,
            <= 1024 => 1024,
            _ => 2048
        };
    }

    private static float[] CalculateFftMagnitudes(float[] samples)
    {
        var length = samples.Length;
        var real = new double[length];
        var imaginary = new double[length];

        for (var i = 0; i < samples.Length; i++)
        {
            var window = 0.5 - (0.5 * Math.Cos((2.0 * Math.PI * i) / (samples.Length - 1)));
            real[i] = samples[i] * window;
        }

        FastFourierTransform(real, imaginary);

        var magnitudes = new float[(length / 2) + 1];
        var scale = 2.0 / length;
        for (var i = 1; i < magnitudes.Length; i++)
        {
            magnitudes[i] = (float)(Math.Sqrt((real[i] * real[i]) + (imaginary[i] * imaginary[i])) * scale);
        }

        return magnitudes;
    }

    private static void FastFourierTransform(double[] real, double[] imaginary)
    {
        var length = real.Length;
        var j = 0;
        for (var i = 1; i < length; i++)
        {
            var bit = length >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }

            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        for (var size = 2; size <= length; size <<= 1)
        {
            var halfSize = size >> 1;
            var angle = -2.0 * Math.PI / size;
            var phaseStepReal = Math.Cos(angle);
            var phaseStepImaginary = Math.Sin(angle);

            for (var offset = 0; offset < length; offset += size)
            {
                var phaseReal = 1.0;
                var phaseImaginary = 0.0;
                for (var i = 0; i < halfSize; i++)
                {
                    var even = offset + i;
                    var odd = even + halfSize;
                    var oddReal = (real[odd] * phaseReal) - (imaginary[odd] * phaseImaginary);
                    var oddImaginary = (real[odd] * phaseImaginary) + (imaginary[odd] * phaseReal);

                    real[odd] = real[even] - oddReal;
                    imaginary[odd] = imaginary[even] - oddImaginary;
                    real[even] += oddReal;
                    imaginary[even] += oddImaginary;

                    var nextPhaseReal = (phaseReal * phaseStepReal) - (phaseImaginary * phaseStepImaginary);
                    phaseImaginary = (phaseReal * phaseStepImaginary) + (phaseImaginary * phaseStepReal);
                    phaseReal = nextPhaseReal;
                }
            }
        }
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_captureThread is not null && _captureThread.IsAlive)
        {
            _captureThread.Join(500);
        }

        _cts.Dispose();
    }

    private sealed class AudioFormat
    {
        private AudioFormat(int channels, int sampleRate, int blockAlign, int bytesPerSample, bool isFloat)
        {
            Channels = channels;
            SampleRate = sampleRate;
            BlockAlign = blockAlign;
            BytesPerSample = bytesPerSample;
            IsFloat = isFloat;
        }

        public int Channels { get; }
        public int SampleRate { get; }
        public int BlockAlign { get; }
        public int BytesPerSample { get; }
        public bool IsFloat { get; }

        public static AudioFormat FromWaveFormat(IntPtr ptr)
        {
            var waveFormat = Marshal.PtrToStructure<WaveFormatEx>(ptr);
            var isFloat = waveFormat.FormatTag == WaveFormatIeeeFloat;

            if (waveFormat.FormatTag == WaveFormatExtensibleTag && waveFormat.ExtraSize >= 22)
            {
                var extensible = Marshal.PtrToStructure<WaveFormatExtensible>(ptr);
                isFloat = extensible.SubFormat == IeeeFloatSubFormat;
            }

            return new AudioFormat(
                waveFormat.Channels,
                waveFormat.SamplesPerSec,
                waveFormat.BlockAlign,
                Math.Max(1, waveFormat.BitsPerSample / 8),
                isFloat);
        }
    }

    private enum EDataFlow
    {
        Render,
        Capture,
        All
    }

    private enum ERole
    {
        Console,
        Multimedia,
        Communications
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        int OpenPropertyStore(int access, out IntPtr properties);
        int GetId(out IntPtr id);
        int GetState(out int state);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);
        int GetBufferSize(out int bufferFrames);
        int GetStreamLatency(out long latency);
        int GetCurrentPadding(out int paddingFrames);
        int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        int GetMixFormat(out IntPtr deviceFormat);
        int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        int Start();
        int Stop();
        int Reset();
        int SetEventHandle(IntPtr eventHandle);
        int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        int GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long qpcPosition);
        int ReleaseBuffer(int frames);
        int GetNextPacketSize(out int frames);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public short BlockAlign;
        public short BitsPerSample;
        public short ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatExtensible
    {
        public WaveFormatEx Format;
        public short ValidBitsPerSample;
        public int ChannelMask;
        public Guid SubFormat;
    }
}

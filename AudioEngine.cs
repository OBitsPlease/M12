using System.Buffers.Binary;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MultibandCore;

public sealed class AudioEngine : IDisposable
{
    private readonly BandSettings[] _settings;
    private readonly object _playbackLock = new();
    private WasapiCapture? _capture;
    private WasapiOut? _playback;
    private BufferedWaveProvider? _outputBuffer;
    private MediaFoundationResampler? _resampler;
    private MMDevice? _inputEndpoint;
    private MMDevice? _outputEndpoint;
    private MultibandProcessor[] _processors = [];
    private WaveFormat? _captureFormat;
    private double[]? _pendingCrossovers;
    private byte[] _processingBuffer = [];
    private int _targetBufferedBytes;
    private int _driftToleranceBytes;
    private volatile bool _playbackStarted;

    public AudioEngine(BandSettings[] settings) => _settings = settings;

    public MultibandProcessor? Processor => _processors.FirstOrDefault();
    public bool IsRunning => _capture is not null;
    public int ConfiguredLatencyMilliseconds { get; private set; } = 10;
    public event EventHandler<string>? AudioError;

    public static IReadOnlyList<AudioDevice> GetInputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = new List<AudioDevice>();
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            devices.Add(new AudioDevice(endpoint.ID, $"PLAYBACK - {endpoint.FriendlyName}", AudioDeviceKind.Loopback));
        }
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            devices.Add(new AudioDevice(endpoint.ID, $"INPUT - {endpoint.FriendlyName}", AudioDeviceKind.Capture));
        }
        return devices;
    }

    public static IReadOnlyList<AudioDevice> GetOutputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(endpoint => new AudioDevice(endpoint.ID, endpoint.FriendlyName, AudioDeviceKind.Render))
            .ToArray();
    }

    public void Start(AudioDevice input, AudioDevice output, double[] crossoverFrequencies)
    {
        Stop();
        if (input.Kind == AudioDeviceKind.Loopback && input.Id == output.Id)
        {
            throw new InvalidOperationException("The playback source and output cannot be the same device because that would create an audio feedback loop. Select a different output device.");
        }

        using var enumerator = new MMDeviceEnumerator();
        _inputEndpoint = enumerator.GetDevice(input.Id);
        _outputEndpoint = enumerator.GetDevice(output.Id);
        ConfiguredLatencyMilliseconds = GetAdaptiveLatencyMilliseconds(_inputEndpoint, _outputEndpoint);
        _capture = input.Kind == AudioDeviceKind.Loopback
            ? new LowLatencyLoopbackCapture(_inputEndpoint, ConfiguredLatencyMilliseconds)
            : new WasapiCapture(_inputEndpoint, true, ConfiguredLatencyMilliseconds);
        _captureFormat = _capture.WaveFormat;

        var bytesPerSample = _captureFormat.BitsPerSample / 8;
        if (bytesPerSample is not (2 or 3 or 4))
        {
            throw new NotSupportedException($"The selected source uses an unsupported {_captureFormat.BitsPerSample}-bit format.");
        }

        _processors = Enumerable.Range(0, _captureFormat.Channels)
            .Select(_ => new MultibandProcessor(_captureFormat.SampleRate, _settings, crossoverFrequencies))
            .ToArray();
        _outputBuffer = new BufferedWaveProvider(_captureFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(1),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };
        _targetBufferedBytes = _captureFormat.AverageBytesPerSecond * Math.Max(60, ConfiguredLatencyMilliseconds * 4) / 1000;
        _driftToleranceBytes = _captureFormat.AverageBytesPerSecond / 100;
        _playback = new WasapiOut(_outputEndpoint, AudioClientShareMode.Shared, true, ConfiguredLatencyMilliseconds);
        var outputFormat = _outputEndpoint.AudioClient.MixFormat;
        if (FormatsMatch(_captureFormat, outputFormat))
        {
            _playback.Init(_outputBuffer);
        }
        else
        {
            _resampler = new MediaFoundationResampler(_outputBuffer, outputFormat) { ResamplerQuality = 60 };
            _playback.Init(_resampler);
        }

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
    }

    private static int GetAdaptiveLatencyMilliseconds(MMDevice input, MMDevice output)
    {
        var minimumPeriodTicks = Math.Max(
            input.AudioClient.MinimumDevicePeriod,
            output.AudioClient.MinimumDevicePeriod);
        var minimumMilliseconds = minimumPeriodTicks / (double)TimeSpan.TicksPerMillisecond;
        return Math.Clamp((int)Math.Ceiling(minimumMilliseconds + 10), 15, 25);
    }

    public void SetMasterControls(double knee, double outputGain, bool autoRelease)
    {
        foreach (var processor in _processors)
        {
            processor.Knee = knee;
            processor.OutputGain = outputGain;
            processor.AutoRelease = autoRelease;
        }
    }

    public void QueueCrossovers(double[] crossoverFrequencies) =>
        Interlocked.Exchange(ref _pendingCrossovers, crossoverFrequencies.ToArray());

    public void Stop()
    {
        var capture = _capture;
        _capture = null;
        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            capture.StopRecording();
            capture.Dispose();
        }
        lock (_playbackLock)
        {
            _playback?.Stop();
            _playback?.Dispose();
            _playback = null;
            _playbackStarted = false;
        }
        _resampler?.Dispose();
        _resampler = null;
        _outputBuffer = null;
        _targetBufferedBytes = 0;
        _driftToleranceBytes = 0;
        _processors = [];
        _captureFormat = null;
        _processingBuffer = [];
        _inputEndpoint?.Dispose();
        _inputEndpoint = null;
        _outputEndpoint?.Dispose();
        _outputEndpoint = null;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        var outputBuffer = _outputBuffer;
        var format = _captureFormat;
        if (outputBuffer is null || format is null || _processors.Length == 0)
        {
            return;
        }

        var crossoverUpdate = Interlocked.Exchange(ref _pendingCrossovers, null);
        if (crossoverUpdate is not null)
        {
            foreach (var processor in _processors)
            {
                processor.UpdateCrossovers(crossoverUpdate);
            }
        }
        foreach (var processor in _processors)
        {
            processor.PrepareBlock();
        }

        if (_processingBuffer.Length < eventArgs.BytesRecorded)
        {
            _processingBuffer = new byte[eventArgs.BytesRecorded];
        }
        var output = _processingBuffer;
        var bytesPerSample = format.BitsPerSample / 8;
        var sampleCount = eventArgs.BytesRecorded / bytesPerSample;
        var isFloat = IsFloatFormat(format);
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var offset = sampleIndex * bytesPerSample;
            var channel = sampleIndex % format.Channels;
            var inputSample = ReadSample(eventArgs.Buffer.AsSpan(offset, bytesPerSample), bytesPerSample, isFloat);
            var processed = _processors[channel].Process(inputSample);
            WriteSample(output.AsSpan(offset, bytesPerSample), processed, bytesPerSample, isFloat);
        }
        var frameBytes = bytesPerSample * format.Channels;
        var bufferOffset = 0;
        var count = eventArgs.BytesRecorded;
        if (_playbackStarted &&
            eventArgs.BytesRecorded >= frameBytes &&
            outputBuffer.BufferedBytes > _targetBufferedBytes + _driftToleranceBytes)
        {
            bufferOffset = frameBytes;
            count -= frameBytes;
        }
        outputBuffer.AddSamples(output, bufferOffset, count);
        if (!_playbackStarted && outputBuffer.BufferedBytes >= _targetBufferedBytes)
        {
            lock (_playbackLock)
            {
                if (_playback is not null && !_playbackStarted)
                {
                    _playback.Play();
                    _playbackStarted = true;
                }
            }
        }
        else if (_playbackStarted &&
                 eventArgs.BytesRecorded >= frameBytes &&
                 outputBuffer.BufferedBytes < _targetBufferedBytes - _driftToleranceBytes)
        {
            outputBuffer.AddSamples(output, eventArgs.BytesRecorded - frameBytes, frameBytes);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (eventArgs.Exception is not null)
        {
            AudioError?.Invoke(this, eventArgs.Exception.Message);
        }
    }

    private static bool IsFloatFormat(WaveFormat format) =>
        format.Encoding == WaveFormatEncoding.IeeeFloat ||
        format is WaveFormatExtensible extensible && extensible.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");

    private static bool FormatsMatch(WaveFormat source, WaveFormat destination) =>
        source.SampleRate == destination.SampleRate &&
        source.Channels == destination.Channels &&
        source.BitsPerSample == destination.BitsPerSample &&
        IsFloatFormat(source) == IsFloatFormat(destination);

    private static float ReadSample(ReadOnlySpan<byte> sample, int bytesPerSample, bool isFloat)
    {
        if (isFloat && bytesPerSample == 4)
        {
            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(sample));
        }
        if (bytesPerSample == 2)
        {
            return BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f;
        }
        if (bytesPerSample == 3)
        {
            var value = sample[0] | sample[1] << 8 | sample[2] << 16;
            if ((value & 0x800000) != 0)
            {
                value |= unchecked((int)0xff000000);
            }
            return value / 8388608f;
        }
        return BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f;
    }

    private static void WriteSample(Span<byte> destination, float sample, int bytesPerSample, bool isFloat)
    {
        sample = Math.Clamp(sample, -0.98f, 0.98f);
        if (isFloat && bytesPerSample == 4)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(sample));
        }
        else if (bytesPerSample == 2)
        {
            BinaryPrimitives.WriteInt16LittleEndian(destination, (short)(sample * 32767f));
        }
        else if (bytesPerSample == 3)
        {
            var value = (int)(sample * 8388607f);
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
            destination[2] = (byte)(value >> 16);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, (int)(sample * 2147483647f));
        }
    }
}

internal sealed class LowLatencyLoopbackCapture : WasapiCapture
{
    public LowLatencyLoopbackCapture(MMDevice device, int bufferMilliseconds)
        : base(device, true, bufferMilliseconds)
    {
    }

    protected override AudioClientStreamFlags GetAudioClientStreamFlags() => AudioClientStreamFlags.Loopback;
}

public enum AudioDeviceKind
{
    Capture,
    Loopback,
    Render
}

public sealed record AudioDevice(string Id, string Name, AudioDeviceKind Kind)
{
    public override string ToString() => Name;
}
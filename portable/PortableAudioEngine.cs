using MultibandCore;
using PortAudioSharp;
using PortAudioStream = PortAudioSharp.Stream;

namespace BitsPleaseYTM6.Portable;

public sealed class PortableAudioEngine : IDisposable
{
    private readonly BandSettings[] _settings;
    private PortAudioStream? _stream;
    private MultibandProcessor[] _processors = [];
    private bool _initialized;

    public PortableAudioEngine(BandSettings[] settings)
    {
        _settings = settings;
        PortAudio.Initialize();
        _initialized = true;
    }

    public bool IsRunning => _stream is not null;
    public double InputLevel => _processors.Length == 0 ? -60 : _processors.Max(processor => processor.InputLevel);
    public double OutputLevel => _processors.Length == 0 ? -60 : _processors.Max(processor => processor.OutputLevel);
    public int ConfiguredLatencyMilliseconds { get; private set; }

    public static IReadOnlyList<PortableDevice> GetInputDevices() => GetDevices(true);
    public static IReadOnlyList<PortableDevice> GetOutputDevices() => GetDevices(false);

    public void Start(PortableDevice input, PortableDevice output, double[] crossoverFrequencies)
    {
        Stop();
        const int sampleRate = 48000;
        var inputChannels = Math.Clamp(input.Channels, 1, 2);
        var outputChannels = Math.Clamp(output.Channels, 1, 2);
        ConfiguredLatencyMilliseconds = Math.Max(1, (int)Math.Ceiling((input.Latency + output.Latency) * 1000));
        _processors = Enumerable.Range(0, inputChannels)
            .Select(_ => new MultibandProcessor(sampleRate, _settings, crossoverFrequencies))
            .ToArray();

        var inputParameters = new StreamParameters
        {
            device = input.Index,
            channelCount = inputChannels,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = input.Latency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };
        var outputParameters = new StreamParameters
        {
            device = output.Index,
            channelCount = outputChannels,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = output.Latency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };

        _stream = new PortAudioStream(inputParameters, outputParameters, sampleRate, 0,
            StreamFlags.ClipOff, ProcessAudio, new StreamState(inputChannels, outputChannels));
        _stream.Start();
    }

    public void Stop()
    {
        _stream?.Stop();
        _stream?.Dispose();
        _stream = null;
        _processors = [];
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

    public void UpdateCrossovers(double[] crossoverFrequencies)
    {
        foreach (var processor in _processors)
        {
            processor.UpdateCrossovers(crossoverFrequencies);
        }
    }

    public void Dispose()
    {
        Stop();
        if (_initialized)
        {
            PortAudio.Terminate();
            _initialized = false;
        }
        GC.SuppressFinalize(this);
    }

    private unsafe StreamCallbackResult ProcessAudio(IntPtr input, IntPtr output, uint frameCount,
        ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData)
    {
        var state = PortAudioStream.GetUserData<StreamState>(userData);
        var inputSamples = (float*)input;
        var outputSamples = (float*)output;
        if (input == IntPtr.Zero)
        {
            new Span<float>(outputSamples, checked((int)frameCount * state.OutputChannels)).Clear();
            return StreamCallbackResult.Continue;
        }

        for (var frame = 0; frame < frameCount; frame++)
        {
            for (var channel = 0; channel < state.OutputChannels; channel++)
            {
                var sourceChannel = Math.Min(channel, state.InputChannels - 1);
                var sample = inputSamples[frame * state.InputChannels + sourceChannel];
                outputSamples[frame * state.OutputChannels + channel] = _processors[sourceChannel].Process(sample);
            }
        }
        return StreamCallbackResult.Continue;
    }

    private static IReadOnlyList<PortableDevice> GetDevices(bool input)
    {
        var devices = new List<PortableDevice>();
        for (var index = 0; index < PortAudio.DeviceCount; index++)
        {
            var info = PortAudio.GetDeviceInfo(index);
            var channels = input ? info.maxInputChannels : info.maxOutputChannels;
            if (channels > 0)
            {
                var latency = input ? info.defaultLowInputLatency : info.defaultLowOutputLatency;
                devices.Add(new PortableDevice(index, info.name, channels, latency));
            }
        }
        return devices;
    }

    private sealed record StreamState(int InputChannels, int OutputChannels);
}

public sealed record PortableDevice(int Index, string Name, int Channels, double Latency)
{
    public override string ToString() => Name;
}
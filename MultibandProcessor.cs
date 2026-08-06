using NAudio.Dsp;

namespace MultibandCore;

public sealed class MultibandProcessor
{
    private readonly BandSettings[] _settings;
    private readonly Crossover[] _crossovers;
    private readonly double[] _envelopes;
    private readonly double[] _meterLevels;
    private readonly double[] _meterReduction;
    private readonly float[] _bandSamples;
    private readonly int _sampleRate;

    public MultibandProcessor(int sampleRate, BandSettings[] settings, double[] crossoverFrequencies)
    {
        if (settings.Length != crossoverFrequencies.Length + 1)
        {
            throw new ArgumentException("The compressor needs one more band than crossover frequency.");
        }

        _sampleRate = sampleRate;
        _settings = settings;
        _envelopes = new double[settings.Length];
        _meterLevels = new double[settings.Length];
        _meterReduction = new double[settings.Length];
        _bandSamples = new float[settings.Length];
        _crossovers = crossoverFrequencies.Select(frequency => new Crossover(sampleRate, frequency)).ToArray();
    }

    public bool AutoRelease { get; set; } = true;
    public double Knee { get; set; } = 6;
    public double OutputGain { get; set; }
    public double InputLevel { get; private set; } = -60;
    public double OutputLevel { get; private set; } = -60;

    public void UpdateCrossovers(double[] frequencies)
    {
        for (var index = 0; index < _crossovers.Length; index++)
        {
            _crossovers[index].SetFrequency(_sampleRate, frequencies[index]);
        }
    }

    public float Process(float input)
    {
        var previousLow = _crossovers[0].LowPass(input);
        _bandSamples[0] = previousLow;
        for (var index = 1; index < _crossovers.Length; index++)
        {
            var low = _crossovers[index].LowPass(input);
            _bandSamples[index] = low - previousLow;
            previousLow = low;
        }
        _bandSamples[^1] = input - previousLow;

        var anySolo = _settings.Any(setting => setting.IsSolo);
        var output = 0.0;

        for (var index = 0; index < _settings.Length; index++)
        {
            var setting = _settings[index];
            var sample = _bandSamples[index];
            var detector = Math.Abs(sample);
            var attackCoefficient = TimeCoefficient(setting.Attack);
            var releaseMs = AutoRelease
                ? setting.Release * (1.0 + Math.Clamp(_envelopes[index] * 2.0, 0.0, 2.0))
                : setting.Release;
            var releaseCoefficient = TimeCoefficient(releaseMs);
            var coefficient = detector > _envelopes[index] ? attackCoefficient : releaseCoefficient;
            _envelopes[index] = coefficient * _envelopes[index] + (1.0 - coefficient) * detector;

            var levelDb = LinearToDb(_envelopes[index]);
            var reductionDb = setting.IsBypassed ? 0.0 : CalculateReduction(levelDb, setting);
            var gain = DbToLinear(setting.MakeupGain - reductionDb);

            _meterLevels[index] = SmoothMeter(_meterLevels[index], levelDb);
            _meterReduction[index] = SmoothMeter(_meterReduction[index], reductionDb);

            if (!anySolo || setting.IsSolo)
            {
                output += setting.IsBypassed ? sample : sample * gain;
            }
        }

        output *= DbToLinear(OutputGain);
        output = Math.Clamp(output, -0.98, 0.98);
        InputLevel = SmoothMeter(InputLevel, LinearToDb(Math.Abs(input)));
        OutputLevel = SmoothMeter(OutputLevel, LinearToDb(Math.Abs(output)));
        return (float)output;
    }

    public (double Level, double Reduction) GetMeter(int bandIndex) =>
        (_meterLevels[bandIndex], _meterReduction[bandIndex]);

    private double CalculateReduction(double levelDb, BandSettings setting)
    {
        var over = levelDb - setting.Threshold;
        var knee = Math.Max(0.0, Knee);
        double compressedOver;

        if (knee > 0 && over > -knee / 2.0 && over < knee / 2.0)
        {
            var kneePosition = over + knee / 2.0;
            compressedOver = kneePosition * kneePosition / (2.0 * knee);
        }
        else
        {
            compressedOver = Math.Max(0.0, over);
        }

        var reduction = compressedOver * (1.0 - 1.0 / Math.Max(1.0, setting.Ratio));
        return Math.Clamp(reduction, 0.0, setting.Range);
    }

    private double TimeCoefficient(double milliseconds) =>
        Math.Exp(-1.0 / (_sampleRate * Math.Max(0.1, milliseconds) / 1000.0));

    private static double LinearToDb(double value) => 20.0 * Math.Log10(Math.Max(0.000001, value));
    private static double DbToLinear(double value) => Math.Pow(10.0, value / 20.0);
    private static double SmoothMeter(double current, double target) => current * 0.9 + target * 0.1;

    private sealed class Crossover
    {
        private BiQuadFilter _lowPassOne = null!;
        private BiQuadFilter _lowPassTwo = null!;
        private BiQuadFilter _highPassOne = null!;
        private BiQuadFilter _highPassTwo = null!;
        private double _frequency;

        public Crossover(int sampleRate, double frequency) => SetFrequency(sampleRate, frequency);

        public void SetFrequency(int sampleRate, double frequency)
        {
            if (Math.Abs(_frequency - frequency) < 0.1)
            {
                return;
            }

            _frequency = frequency;
            const float q = 0.70710678f;
            _lowPassOne = BiQuadFilter.LowPassFilter(sampleRate, (float)frequency, q);
            _lowPassTwo = BiQuadFilter.LowPassFilter(sampleRate, (float)frequency, q);
            _highPassOne = BiQuadFilter.HighPassFilter(sampleRate, (float)frequency, q);
            _highPassTwo = BiQuadFilter.HighPassFilter(sampleRate, (float)frequency, q);
        }

        public float LowPass(float sample) => _lowPassTwo.Transform(_lowPassOne.Transform(sample));
        public float HighPass(float sample) => _highPassTwo.Transform(_highPassOne.Transform(sample));
    }
}
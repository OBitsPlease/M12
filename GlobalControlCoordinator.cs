namespace MultibandCore;

internal sealed class GlobalControlCoordinator
{
    private readonly Dictionary<string, double[]> _baselines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _offsets = new(StringComparer.Ordinal);

    public void Reset(IReadOnlyList<BandSettings> bands)
    {
        foreach (var parameter in Parameters)
        {
            Capture(parameter, bands);
            _offsets[parameter] = 0;
        }
    }

    public void Apply(string parameter, double normalizedOffset, IReadOnlyList<BandSettings> bands)
    {
        if (!_offsets.TryGetValue(parameter, out var previousOffset) || Math.Abs(previousOffset) < 0.000001)
        {
            Capture(parameter, bands);
        }

        var (minimum, maximum) = GetRange(parameter);
        var baseline = _baselines[parameter];
        var adjustment = normalizedOffset * (maximum - minimum);
        for (var index = 0; index < bands.Count; index++)
        {
            SetValue(bands[index], parameter, Math.Clamp(baseline[index] + adjustment, minimum, maximum));
        }
        _offsets[parameter] = normalizedOffset;
    }

    private void Capture(string parameter, IReadOnlyList<BandSettings> bands) =>
        _baselines[parameter] = bands.Select(band => GetValue(band, parameter)).ToArray();

    private static double GetValue(BandSettings band, string parameter) => parameter switch
    {
        "Threshold" => band.Threshold,
        "Ratio" => band.Ratio,
        "Range" => band.Range,
        "MakeupGain" => band.MakeupGain,
        "Attack" => band.Attack,
        "Release" => band.Release,
        _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter, "Unknown Global control.")
    };

    private static void SetValue(BandSettings band, string parameter, double value)
    {
        switch (parameter)
        {
            case "Threshold":
                band.Threshold = value;
                break;
            case "Ratio":
                band.Ratio = value;
                break;
            case "Range":
                band.Range = value;
                break;
            case "MakeupGain":
                band.MakeupGain = value;
                break;
            case "Attack":
                band.Attack = value;
                break;
            case "Release":
                band.Release = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(parameter), parameter, "Unknown Global control.");
        }
    }

    private static (double Minimum, double Maximum) GetRange(string parameter) => parameter switch
    {
        "Threshold" => (-60, 0),
        "Ratio" => (1, 20),
        "Range" => (0, 30),
        "MakeupGain" => (-12, 12),
        "Attack" => (0.5, 100),
        "Release" => (20, 1000),
        _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter, "Unknown Global control.")
    };

    private static readonly string[] Parameters =
    [
        "Threshold",
        "Ratio",
        "Range",
        "MakeupGain",
        "Attack",
        "Release"
    ];
}

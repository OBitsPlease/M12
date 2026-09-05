namespace MultibandCore;

internal sealed record PresetStore(string LastPreset, UserPreset[] Presets);

internal sealed record UserPreset(
    string Name,
    double[] Crossovers,
    BandPreset[] Bands,
    double Knee,
    double OutputGain,
    bool AutoRelease,
    string Behavior);

internal sealed record BandPreset(
    double Threshold,
    double Ratio,
    double Range,
    double Attack,
    double Release,
    double MakeupGain,
    bool IsSolo,
    bool IsBypassed);

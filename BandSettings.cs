using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DiscordMultiband;

public sealed class BandSettings : INotifyPropertyChanged
{
    private double _threshold = -24;
    private double _ratio = 4;
    private double _range = 12;
    private double _attack = 10;
    private double _release = 160;
    private double _makeupGain;
    private string _frequencyLabel = string.Empty;
    private bool _isSolo;
    private bool _isBypassed;
    private double _gainReduction;
    private double _level = -60;

    public required string Name { get; init; }
    public required string FrequencyLabel { get => _frequencyLabel; set => SetField(ref _frequencyLabel, value); }
    public required string Color { get; init; }

    public double Threshold { get => _threshold; set => SetField(ref _threshold, value); }
    public double Ratio { get => _ratio; set => SetField(ref _ratio, value); }
    public double Range { get => _range; set => SetField(ref _range, value); }
    public double Attack { get => _attack; set => SetField(ref _attack, value); }
    public double Release { get => _release; set => SetField(ref _release, value); }
    public double MakeupGain { get => _makeupGain; set => SetField(ref _makeupGain, value); }
    public bool IsSolo { get => _isSolo; set => SetField(ref _isSolo, value); }
    public bool IsBypassed { get => _isBypassed; set => SetField(ref _isBypassed, value); }
    public double GainReduction { get => _gainReduction; set => SetField(ref _gainReduction, value); }
    public double Level { get => _level; set => SetField(ref _level, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
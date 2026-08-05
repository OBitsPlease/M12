using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DiscordMultiband;

public sealed class CrossoverSetting : INotifyPropertyChanged
{
    private double _frequency;

    public CrossoverSetting(string name, double frequency, double minimum, double maximum)
    {
        Name = name;
        _frequency = frequency;
        Minimum = minimum;
        Maximum = maximum;
    }

    public string Name { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public double Frequency
    {
        get => _frequency;
        set
        {
            if (Math.Abs(_frequency - value) < 0.1)
            {
                return;
            }

            _frequency = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Frequency)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
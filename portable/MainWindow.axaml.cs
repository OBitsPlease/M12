using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MultibandCore;
using PortAudioSharp;

namespace BitsPleaseYTM6.Portable;

public partial class MainWindow : Window
{
    private readonly PortableAudioEngine _audio;
    private readonly DispatcherTimer _meterTimer;
    private readonly Dictionary<string, double> _globalControlValues = new(StringComparer.Ordinal);
    private bool _isUiReady;
    private bool _loadingPreset;
    private int _meterTicks;

    public MainWindow()
    {
        Bands = CreateBands();
        Crossovers = CreateCrossovers();
        InitializeComponent();
        DataContext = this;
        _audio = new PortableAudioEngine(Bands.ToArray());
        foreach (var crossover in Crossovers)
        {
            crossover.PropertyChanged += Crossover_PropertyChanged;
        }
        RefreshDevices();
        PresetBox.SelectedItem = "Broadcast Voice";
        ApplyPreset("Broadcast Voice");
        _isUiReady = true;
        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _meterTimer.Tick += (_, _) =>
        {
            InputMeter.Value = _audio.InputLevel;
            OutputMeter.Value = _audio.OutputLevel;
            for (var index = 0; index < Bands.Count; index++)
            {
                var meter = _audio.GetMeter(index);
                Bands[index].Level = meter.Level;
                Bands[index].GainReduction = meter.Reduction;
            }
            _audio.SetMasterControls(KneeSlider.Value, OutputSlider.Value, AutoReleaseSwitch.IsChecked == true);
            if (++_meterTicks % 4 == 0)
            {
                DrawGraph();
            }
        };
        _meterTimer.Start();
        Closing += (_, _) =>
        {
            _meterTimer.Stop();
            _audio.Dispose();
        };
    }

    public ObservableCollection<BandSettings> Bands { get; }
    public ObservableCollection<CrossoverSetting> Crossovers { get; }
    public ObservableCollection<string> PresetNames { get; } =
    [
        "Broadcast Voice",
        "Smooth Voice",
        "Tight Voice",
        "Reset"
    ];

    private void GraphCanvas_SizeChanged(object? sender, SizeChangedEventArgs e) => DrawGraph();

    private void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            _audio.RefreshDeviceList();
            RefreshDevices();
            StatusText.Text = "CoreAudio devices refreshed.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or PortAudioException)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void RoutingHelpButton_Click(object? sender, RoutedEventArgs e)
    {
        var closeButton = new Button
        {
            Content = "CLOSE",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Thickness(18, 7)
        };
        var helpWindow = new Window
        {
            Title = "M12 macOS audio routing",
            Width = 620,
            Height = 430,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#181B20")),
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = "USING M12 WITH OTHER MAC APPS",
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#F25FCA"))
                    },
                    new TextBlock
                    {
                        Text = "macOS does not expose YouTube, Discord, or other app playback as a normal audio input. Install a virtual CoreAudio device such as BlackHole, route the app or a Multi-Output Device into it, then select that virtual device as M12's SOURCE. In Audio MIDI Setup, enable Drift Correction for the secondary device when combining devices; unsynchronized device clocks cause crackling and dropouts over time.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "To send processed microphone audio into Discord, select your microphone or external interface as SOURCE and a virtual device as PROCESSED OUTPUT. Select that virtual device as Discord's microphone.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "EXTERNAL SOUND CARDS",
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#74E062"))
                    },
                    new TextBlock
                    {
                        Text = "Connect and power on the USB or Thunderbolt interface, allow microphone access for M12 in Privacy & Security, then click REFRESH. Every input and output reported by CoreAudio will be listed with its channel count.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    closeButton
                }
            }
        };
        closeButton.Click += (_, _) => helpWindow.Close();
        _ = helpWindow.ShowDialog(this);
    }

    private void RefreshDevices()
    {
        var selectedInput = InputBox.SelectedItem as PortableDevice;
        var selectedOutput = OutputBox.SelectedItem as PortableDevice;
        InputBox.ItemsSource = PortableAudioEngine.GetInputDevices();
        OutputBox.ItemsSource = PortableAudioEngine.GetOutputDevices();
        InputBox.SelectedIndex = FindDeviceIndex(InputBox, selectedInput);
        OutputBox.SelectedIndex = FindDeviceIndex(OutputBox, selectedOutput);
    }

    private static int FindDeviceIndex(ComboBox box, PortableDevice? selectedDevice)
    {
        if (selectedDevice is not null)
        {
            for (var index = 0; index < box.ItemCount; index++)
            {
                if (box.Items[index] is PortableDevice device &&
                    device.Index == selectedDevice.Index &&
                    device.Name == selectedDevice.Name)
                {
                    return index;
                }
            }
            for (var index = 0; index < box.ItemCount; index++)
            {
                if (box.Items[index] is PortableDevice device &&
                    device.Name == selectedDevice.Name &&
                    device.Channels == selectedDevice.Channels)
                {
                    return index;
                }
            }
        }
        return box.ItemCount > 0 ? 0 : -1;
    }

    private void DrawGraph()
    {
        if (GraphCanvas.Bounds.Width < 1 || GraphCanvas.Bounds.Height < 1)
        {
            return;
        }

        var width = GraphCanvas.Bounds.Width;
        var height = GraphCanvas.Bounds.Height;
        GraphCanvas.Children.Clear();
        for (var index = 0; index <= 6; index++)
        {
            var y = index * height / 6;
            GraphCanvas.Children.Add(new Line { StartPoint = new Point(0, y), EndPoint = new Point(width, y), Stroke = new SolidColorBrush(Color.Parse("#30343B")), StrokeThickness = 1 });
        }
        foreach (var crossover in Crossovers)
        {
            var x = Math.Log10(crossover.Frequency / 20.0) / 3.0 * width;
            GraphCanvas.Children.Add(new Line { StartPoint = new Point(x, 0), EndPoint = new Point(x, height), Stroke = new SolidColorBrush(Color.Parse("#4B515C")), StrokeThickness = 1 });
        }

        var response = new Polyline { Stroke = new SolidColorBrush(Color.Parse("#F25FCA")), StrokeThickness = 2.5 };
        for (var pixel = 0; pixel <= width; pixel += 3)
        {
            var frequency = 20 * Math.Pow(1000, pixel / width);
            var gain = 0.0;
            var totalWeight = 0.0;
            for (var index = 0; index < Bands.Count; index++)
            {
                var lower = index == 0 ? 20.0 : Crossovers[index - 1].Frequency;
                var upper = index == Bands.Count - 1 ? 20000.0 : Crossovers[index].Frequency;
                var center = Math.Sqrt(lower * upper);
                var distance = Math.Log(frequency / center, 2);
                var weight = Math.Exp(-distance * distance / 1.3);
                gain += (Bands[index].MakeupGain - Bands[index].GainReduction) * weight;
                totalWeight += weight;
            }
            response.Points.Add(new Point(pixel, height / 2 - gain / Math.Max(0.001, totalWeight) / 36 * height));
        }
        GraphCanvas.Children.Add(response);
    }

    private void StartButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_audio.IsRunning)
        {
            _audio.Stop();
            StartButton.Content = "START AUDIO";
            StatusText.Text = "Audio stopped.";
            InputBox.IsEnabled = true;
            OutputBox.IsEnabled = true;
            return;
        }

        if (InputBox.SelectedItem is not PortableDevice input || OutputBox.SelectedItem is not PortableDevice output)
        {
            StatusText.Text = "Select both input and output devices.";
            return;
        }

        try
        {
            _audio.Start(input, output, GetCrossoverFrequencies());
            _audio.SetMasterControls(KneeSlider.Value, OutputSlider.Value, AutoReleaseSwitch.IsChecked == true);
            StartButton.Content = "STOP AUDIO";
            StatusText.Text = $"LIVE · AUTO {_audio.ConfiguredLatencyMilliseconds} ms I/O: {input.Name} to {output.Name}";
            InputBox.IsEnabled = false;
            OutputBox.IsEnabled = false;
        }
        catch (Exception exception)
        {
            _audio.Stop();
            StatusText.Text = exception.Message;
        }
    }

    private static ObservableCollection<BandSettings> CreateBands() =>
    [
        new() { Name = "SUB", FrequencyLabel = "20 - 60 Hz", Color = "#E8C94A", MakeupGain = -2 },
        new() { Name = "BASS", FrequencyLabel = "60 - 120 Hz", Color = "#E89A4A", Threshold = -27, Ratio = 4.5, Range = 12, MakeupGain = -1.5 },
        new() { Name = "LOW", FrequencyLabel = "120 - 250 Hz", Color = "#55C8E8", Threshold = -30, Ratio = 5, Range = 14, MakeupGain = -1 },
        new() { Name = "LOW MID", FrequencyLabel = "250 - 400 Hz", Color = "#45D7B0", Threshold = -29, Ratio = 4.5, Range = 13 },
        new() { Name = "BODY", FrequencyLabel = "400 - 700 Hz", Color = "#A8D72E", Threshold = -27, Ratio = 4, Range = 12, MakeupGain = 1 },
        new() { Name = "MID", FrequencyLabel = "700 Hz - 1.2 kHz", Color = "#E0D94A", Threshold = -26, Ratio = 4.5, Range = 13, MakeupGain = 1.5 },
        new() { Name = "UPPER MID", FrequencyLabel = "1.2 - 2.2 kHz", Color = "#E557C3", Threshold = -25, Ratio = 5, Range = 14, MakeupGain = 2 },
        new() { Name = "PRESENCE", FrequencyLabel = "2.2 - 3.5 kHz", Color = "#F27A9D", Threshold = -24, Ratio = 4.5, Range = 12, MakeupGain = 1.5 },
        new() { Name = "CLARITY", FrequencyLabel = "3.5 - 5.5 kHz", Color = "#E8E8E8", Threshold = -22, Ratio = 4, Range = 10, MakeupGain = 1 },
        new() { Name = "HIGH", FrequencyLabel = "5.5 - 8 kHz", Color = "#A6B9FF", Threshold = -21, Ratio = 3.5, Range = 9, MakeupGain = 0.5 },
        new() { Name = "BRILLIANCE", FrequencyLabel = "8 - 12 kHz", Color = "#6C93F0", Threshold = -20, Ratio = 3, Range = 8 },
        new() { Name = "AIR", FrequencyLabel = "12 - 20 kHz", Color = "#4D8DE8", Threshold = -19, Ratio = 3, Range = 8 }
    ];

    private static ObservableCollection<CrossoverSetting> CreateCrossovers() =>
    [
        new("Sub", 60, 40, 90), new("Bass", 120, 80, 180), new("Low", 250, 160, 320),
        new("Low Mid", 400, 280, 550), new("Body", 700, 450, 900), new("Mid", 1200, 750, 1600),
        new("Upper Mid", 2200, 1300, 2800), new("Presence", 3500, 2200, 4500),
        new("Clarity", 5500, 3500, 7000), new("High", 8000, 5500, 11000),
        new("Brilliance", 12000, 8500, 16000)
    ];

    private static (double Threshold, double Ratio, double Range, double Attack, double Release, double Gain)[] ExpandPreset(
        (double Threshold, double Ratio, double Range, double Attack, double Release, double Gain)[] anchors) =>
        Enumerable.Range(0, 12).Select(index =>
        {
            var position = index * (anchors.Length - 1.0) / 11.0;
            var lower = Math.Min((int)position, anchors.Length - 1);
            var upper = Math.Min(lower + 1, anchors.Length - 1);
            var amount = position - lower;
            return (
                Lerp(anchors[lower].Threshold, anchors[upper].Threshold, amount),
                Lerp(anchors[lower].Ratio, anchors[upper].Ratio, amount),
                Lerp(anchors[lower].Range, anchors[upper].Range, amount),
                Lerp(anchors[lower].Attack, anchors[upper].Attack, amount),
                Lerp(anchors[lower].Release, anchors[upper].Release, amount),
                Lerp(anchors[lower].Gain, anchors[upper].Gain, amount));
        }).ToArray();

    private static double Lerp(double start, double end, double amount) => start + (end - start) * amount;

    private double[] GetCrossoverFrequencies() => Crossovers.Select(crossover => crossover.Frequency).ToArray();

    private void Crossover_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CrossoverSetting.Frequency))
        {
            _audio.UpdateCrossovers(GetCrossoverFrequencies());
            UpdateBandLabels();
            DrawGraph();
        }
    }

    private void GlobalControl_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isUiReady || sender is not RotaryKnob { Tag: string parameter })
        {
            return;
        }

        var previousValue = _globalControlValues.GetValueOrDefault(parameter);
        var delta = e.NewValue - previousValue;
        _globalControlValues[parameter] = e.NewValue;
        if (Math.Abs(delta) < 0.0001)
        {
            return;
        }

        foreach (var band in Bands)
        {
            switch (parameter)
            {
                case "Threshold":
                    band.Threshold = Math.Clamp(band.Threshold + delta, -60, 0);
                    break;
                case "Ratio":
                    band.Ratio = Math.Clamp(band.Ratio + delta, 1, 20);
                    break;
                case "Range":
                    band.Range = Math.Clamp(band.Range + delta, 0, 30);
                    break;
                case "MakeupGain":
                    band.MakeupGain = Math.Clamp(band.MakeupGain + delta, -12, 12);
                    break;
                case "Attack":
                    band.Attack = Math.Clamp(band.Attack + delta, 0.5, 100);
                    break;
                case "Release":
                    band.Release = Math.Clamp(band.Release + delta, 20, 1000);
                    break;
            }
        }
        DrawGraph();
    }

    private void PresetBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady || _loadingPreset || PresetBox.SelectedItem is not string preset)
        {
            return;
        }
        ApplyPreset(preset);
    }

    private void ApplyPreset(string preset)
    {
        _loadingPreset = true;
        ResetGlobalControls();
        var values = preset switch
        {
            "Smooth Voice" => ExpandPreset([(-20d, 2.5, 6d, 20d, 260d, 0d), (-24d, 3d, 8d, 15d, 220d, -1d), (-22d, 3d, 8d, 12d, 220d, 0d), (-21d, 2.5, 7d, 10d, 200d, 1d), (-18d, 2d, 5d, 8d, 180d, 0d), (-16d, 2d, 4d, 6d, 160d, 1d)]),
            "Tight Voice" => ExpandPreset([(-30d, 6d, 16d, 5d, 100d, -2d), (-34d, 7d, 18d, 4d, 90d, -2d), (-30d, 6d, 16d, 3d, 90d, 1d), (-28d, 7d, 18d, 2d, 80d, 2d), (-25d, 5d, 12d, 2d, 75d, 1d), (-22d, 4d, 10d, 1d, 70d, 0d)]),
            "Reset" => Enumerable.Repeat((-24d, 4d, 12d, 10d, 160d, 0d), 12).ToArray(),
            _ => ExpandPreset([(-24d, 4d, 10d, 12d, 180d, -2d), (-30d, 5d, 14d, 8d, 150d, -1d), (-27d, 4d, 12d, 6d, 140d, 1d), (-25d, 5d, 14d, 4d, 120d, 2d), (-22d, 4d, 10d, 3d, 110d, 1d), (-20d, 3d, 8d, 2d, 100d, 0d)])
        };

        for (var index = 0; index < Bands.Count; index++)
        {
            var (threshold, ratio, range, attack, release, gain) = values[index];
            Bands[index].Threshold = threshold;
            Bands[index].Ratio = ratio;
            Bands[index].Range = range;
            Bands[index].Attack = attack;
            Bands[index].Release = release;
            Bands[index].MakeupGain = gain;
            Bands[index].IsSolo = false;
            Bands[index].IsBypassed = false;
        }
        _loadingPreset = false;
        DrawGraph();
    }

    private void ResetGlobalControls()
    {
        foreach (var knob in new[]
                 {
                     GlobalThresholdKnob,
                     GlobalRatioKnob,
                     GlobalRangeKnob,
                     GlobalGainKnob,
                     GlobalAttackKnob,
                     GlobalReleaseKnob
                 })
        {
            knob.Value = 0;
            if (knob.Tag is string parameter)
            {
                _globalControlValues[parameter] = 0;
            }
        }
    }

    private void BehaviorBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady || _loadingPreset || BehaviorBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var behavior = item.Content?.ToString();
        KneeSlider.Value = behavior == "Opto" ? 12 : behavior == "Punch" ? 2 : 6;
        AutoReleaseSwitch.IsChecked = behavior == "Opto";
    }

    private void UpdateBandLabels()
    {
        static string Format(double value) => value >= 1000 ? $"{value / 1000:0.#} kHz" : $"{value:0} Hz";
        var points = GetCrossoverFrequencies();
        Bands[0].FrequencyLabel = $"20 - {Format(points[0])}";
        for (var index = 1; index < Bands.Count - 1; index++)
        {
            Bands[index].FrequencyLabel = $"{Format(points[index - 1])} - {Format(points[index])}";
        }
        Bands[^1].FrequencyLabel = $"{Format(points[^1])} - 20 kHz";
    }
}
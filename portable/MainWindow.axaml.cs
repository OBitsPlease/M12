using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MultibandCore;

namespace BitsPleaseYTM6.Portable;

public partial class MainWindow : Window
{
    private readonly PortableAudioEngine _audio;
    private readonly DispatcherTimer _meterTimer;

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
        InputBox.ItemsSource = PortableAudioEngine.GetInputDevices();
        OutputBox.ItemsSource = PortableAudioEngine.GetOutputDevices();
        InputBox.SelectedIndex = InputBox.ItemCount > 0 ? 0 : -1;
        OutputBox.SelectedIndex = OutputBox.ItemCount > 0 ? 0 : -1;
        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _meterTimer.Tick += (_, _) =>
        {
            InputMeter.Value = _audio.InputLevel;
            OutputMeter.Value = _audio.OutputLevel;
            _audio.SetMasterControls(KneeSlider.Value, OutputSlider.Value, AutoReleaseSwitch.IsChecked == true);
            DrawGraph();
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

    private void GraphCanvas_SizeChanged(object? sender, SizeChangedEventArgs e) => DrawGraph();

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

    private double[] GetCrossoverFrequencies() => Crossovers.Select(crossover => crossover.Frequency).ToArray();

    private void Crossover_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CrossoverSetting.Frequency))
        {
            _audio.UpdateCrossovers(GetCrossoverFrequencies());
            DrawGraph();
        }
    }
}
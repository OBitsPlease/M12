using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BitsPleaseYT.SuiteIpc;

namespace MultibandCore;

public partial class MainWindow : Window
{
    private const int BandCount = 12;
    private readonly AudioEngine _audioEngine;
    private readonly DispatcherTimer _meterTimer;
    private readonly SuiteTelemetryReader? _suiteTelemetry = App.IsSuiteControlMode ? new("M12") : null;
    private readonly string _routeSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BitsPleaseYT M12",
        "route.json");
    private readonly string _presetSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BitsPleaseYT M12",
        "presets.json");
    private readonly string _suiteStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BitsPleaseYT M12",
        "suite-state.json");
    private readonly Dictionary<string, UserPreset> _userPresets = new(StringComparer.OrdinalIgnoreCase);
    private bool _loadingPreset;
    private bool _resettingGlobalControls;
    private bool _isUiReady;
    private RouteSettings? _savedRoute;

    public MainWindow()
    {
        Bands = CreateBands();
        Crossovers = CreateCrossovers();
        _audioEngine = new AudioEngine(Bands.ToArray());
        _audioEngine.AudioError += AudioEngine_AudioError;

        InitializeComponent();
        DataContext = this;
        ResponseGraph.Bands = Bands;
        ResponseGraph.Crossovers = Crossovers;

        foreach (var crossover in Crossovers)
        {
            crossover.PropertyChanged += Crossover_PropertyChanged;
        }
        foreach (var band in Bands)
        {
            band.PropertyChanged += Band_PropertyChanged;
        }

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _meterTimer.Tick += MeterTimer_Tick;
        _meterTimer.Start();
        _savedRoute = LoadRouteSettings();
        RefreshDevices();
        var lastPreset = LoadPresetStore();
        var initialPreset = PresetNames.Contains(lastPreset) ? lastPreset : "Broadcast Voice";
        PresetBox.SelectedItem = initialPreset;
        ApplyPreset(initialPreset);
        PresetNameBox.Text = _userPresets.ContainsKey(initialPreset) ? initialPreset : string.Empty;
        _isUiReady = true;
        SaveSuiteState();
        if (App.IsSuiteControlMode)
        {
            InputDeviceBox.IsEnabled = false;
            OutputDeviceBox.IsEnabled = false;
            StartButton.IsEnabled = false;
            StartButton.Content = "SUITE ROUTING";
            StatusText.Text = "CONTROLLED BY SUITE HUB";
        }
        Loaded += (_, _) => Dispatcher.BeginInvoke(TryStartSavedRoute, DispatcherPriority.ContextIdle);
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

    private static ObservableCollection<BandSettings> CreateBands() =>
    [
        new() { Name = "SUB", FrequencyLabel = "20 - 60 Hz", Color = "#E8C94A" },
        new() { Name = "BASS", FrequencyLabel = "60 - 120 Hz", Color = "#E89A4A" },
        new() { Name = "LOW", FrequencyLabel = "120 - 250 Hz", Color = "#55C8E8" },
        new() { Name = "LOW MID", FrequencyLabel = "250 - 400 Hz", Color = "#45D7B0" },
        new() { Name = "BODY", FrequencyLabel = "400 - 700 Hz", Color = "#A8D72E" },
        new() { Name = "MID", FrequencyLabel = "700 Hz - 1.2 kHz", Color = "#E0D94A" },
        new() { Name = "UPPER MID", FrequencyLabel = "1.2 - 2.2 kHz", Color = "#E557C3" },
        new() { Name = "PRESENCE", FrequencyLabel = "2.2 - 3.5 kHz", Color = "#F27A9D" },
        new() { Name = "CLARITY", FrequencyLabel = "3.5 - 5.5 kHz", Color = "#E8E8E8" },
        new() { Name = "HIGH", FrequencyLabel = "5.5 - 8 kHz", Color = "#A6B9FF" },
        new() { Name = "BRILLIANCE", FrequencyLabel = "8 - 12 kHz", Color = "#6C93F0" },
        new() { Name = "AIR", FrequencyLabel = "12 - 20 kHz", Color = "#4D8DE8" }
    ];

    private static ObservableCollection<CrossoverSetting> CreateCrossovers() =>
    [
        new("Sub", 60, 40, 90),
        new("Bass", 120, 80, 180),
        new("Low", 250, 160, 320),
        new("Low Mid", 400, 280, 550),
        new("Body", 700, 450, 900),
        new("Mid", 1200, 750, 1600),
        new("Upper Mid", 2200, 1300, 2800),
        new("Presence", 3500, 2200, 4500),
        new("Clarity", 5500, 3500, 7000),
        new("High", 8000, 5500, 11000),
        new("Brilliance", 12000, 8500, 16000)
    ];

    private static (double Threshold, double Ratio, double Range, double Attack, double Release, double Gain)[] ExpandPreset(
        (double Threshold, double Ratio, double Range, double Attack, double Release, double Gain)[] anchors) =>
        Enumerable.Range(0, BandCount).Select(index =>
        {
            var position = index * (anchors.Length - 1.0) / (BandCount - 1.0);
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

    private void RefreshDevices_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private void RefreshDevices()
    {
        var currentInputId = (InputDeviceBox.SelectedItem as AudioDevice)?.Id ?? _savedRoute?.InputId;
        var currentOutputId = (OutputDeviceBox.SelectedItem as AudioDevice)?.Id ?? _savedRoute?.OutputId;
        InputDeviceBox.ItemsSource = AudioEngine.GetInputDevices();
        OutputDeviceBox.ItemsSource = AudioEngine.GetOutputDevices();
        InputDeviceBox.SelectedIndex = FindDeviceIndex(InputDeviceBox, currentInputId);

        var outputIndex = FindDeviceIndex(OutputDeviceBox, currentOutputId);
        if (outputIndex < 0)
        {
            for (var index = 0; index < OutputDeviceBox.Items.Count; index++)
            {
                if (OutputDeviceBox.Items[index] is AudioDevice output &&
                    InputDeviceBox.SelectedItem is AudioDevice input && output.Id != input.Id)
                {
                    outputIndex = index;
                    break;
                }
            }
        }
        OutputDeviceBox.SelectedIndex = outputIndex >= 0 ? outputIndex : OutputDeviceBox.Items.Count > 0 ? 0 : -1;
    }

    private static int FindDeviceIndex(ComboBox deviceBox, string? deviceId)
    {
        if (deviceId is not null)
        {
            for (var index = 0; index < deviceBox.Items.Count; index++)
            {
                if (deviceBox.Items[index] is AudioDevice device && device.Id == deviceId)
                {
                    return index;
                }
            }
        }
        return deviceBox.Items.Count > 0 ? 0 : -1;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.IsSuiteControlMode)
        {
            return;
        }
        if (_audioEngine.IsRunning)
        {
            StopAudio();
            return;
        }

        if (InputDeviceBox.SelectedItem is not AudioDevice input || OutputDeviceBox.SelectedItem is not AudioDevice output)
        {
            MessageBox.Show("Select both an audio source and an output device.", "Audio devices", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            _audioEngine.Start(input, output, GetCrossoverFrequencies());
            ApplyMasterControls();
            StartButton.Content = "STOP AUDIO";
            StartButton.Background = new SolidColorBrush(Color.FromRgb(229, 87, 195));
            StatusLight.Fill = new SolidColorBrush(Color.FromRgb(168, 215, 46));
            StatusText.Text = $"LIVE · AUTO {_audioEngine.ConfiguredLatencyMilliseconds} ms";
            InputDeviceBox.IsEnabled = false;
            OutputDeviceBox.IsEnabled = false;
            SaveRouteSettings(input, output, true);
        }
        catch (Exception exception)
        {
            _audioEngine.Stop();
            MessageBox.Show(exception.Message, "Unable to start audio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopAudio()
    {
        _audioEngine.Stop();
        StartButton.Content = "START AUDIO";
        StartButton.Background = new SolidColorBrush(Color.FromRgb(168, 215, 46));
        StatusLight.Fill = new SolidColorBrush(Color.FromRgb(85, 90, 100));
        StatusText.Text = "IDLE";
        InputDeviceBox.IsEnabled = true;
        OutputDeviceBox.IsEnabled = true;
        if (InputDeviceBox.SelectedItem is AudioDevice input && OutputDeviceBox.SelectedItem is AudioDevice output)
        {
            SaveRouteSettings(input, output, false);
        }
    }

    private RouteSettings? LoadRouteSettings()
    {
        try
        {
            return File.Exists(_routeSettingsPath)
                ? JsonSerializer.Deserialize<RouteSettings>(File.ReadAllText(_routeSettingsPath))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private void SaveRouteSettings(AudioDevice input, AudioDevice output, bool autoStart)
    {
        try
        {
            _savedRoute = new RouteSettings(input.Id, output.Id, autoStart);
            Directory.CreateDirectory(Path.GetDirectoryName(_routeSettingsPath)!);
            File.WriteAllText(_routeSettingsPath, JsonSerializer.Serialize(_savedRoute));
        }
        catch
        {
            // Audio routing remains usable even if the user profile is read-only.
        }
    }

    private void TryStartSavedRoute()
    {
        if (!App.IsSuiteControlMode && _savedRoute?.AutoStart == true &&
            InputDeviceBox.SelectedItem is AudioDevice input && input.Id == _savedRoute.InputId &&
            OutputDeviceBox.SelectedItem is AudioDevice output && output.Id == _savedRoute.OutputId)
        {
            StartButton_Click(StartButton, new RoutedEventArgs());
        }
    }

    private void Crossover_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CrossoverSetting.Frequency))
        {
            return;
        }

        UpdateBandLabels();
        _audioEngine.QueueCrossovers(GetCrossoverFrequencies());
        ResponseGraph.InvalidateVisual();
        SaveSuiteState();
    }

    private void Band_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BandSettings.Level) or nameof(BandSettings.GainReduction))
        {
            return;
        }
        SaveSuiteState();
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

    private double[] GetCrossoverFrequencies() => Crossovers.Select(crossover => crossover.Frequency).ToArray();

    private void MeterTimer_Tick(object? sender, EventArgs e)
    {
        if (_suiteTelemetry?.TryRead(out var suite) == true)
        {
            for (var index = 0; index < Math.Min(Bands.Count, suite.BandLevels.Length); index++)
            {
                Bands[index].Level = suite.BandLevels[index];
                Bands[index].GainReduction = suite.BandReductions[index];
            }
            InputMeter.Value = suite.InputLevel;
            OutputMeter.Value = suite.OutputLevel;
            StatusText.Text = suite.Active ? "SUITE · PROCESSING" : "CONTROLLED BY SUITE HUB";
            ResponseGraph.InvalidateVisual();
            return;
        }
        var processor = _audioEngine.Processor;
        if (processor is not null)
        {
            for (var index = 0; index < Bands.Count; index++)
            {
                var meter = processor.GetMeter(index);
                Bands[index].Level = meter.Level;
                Bands[index].GainReduction = meter.Reduction;
            }
            InputMeter.Value = processor.InputLevel;
            OutputMeter.Value = processor.OutputLevel;
        }
        ResponseGraph.InvalidateVisual();
    }

    private void MasterControl_Changed(object sender, RoutedEventArgs e) => ApplyMasterControls();

    private void GlobalControl_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isUiReady || _resettingGlobalControls || sender is not RotaryKnob { Tag: string parameter })
        {
            return;
        }

        foreach (var band in Bands)
        {
            switch (parameter)
            {
                case "Threshold":
                    band.Threshold = e.NewValue;
                    break;
                case "Ratio":
                    band.Ratio = e.NewValue;
                    break;
                case "Range":
                    band.Range = e.NewValue;
                    break;
                case "MakeupGain":
                    band.MakeupGain = e.NewValue;
                    break;
                case "Attack":
                    band.Attack = e.NewValue;
                    break;
                case "Release":
                    band.Release = e.NewValue;
                    break;
            }
        }
        ResponseGraph.InvalidateVisual();
    }

    private void ApplyMasterControls()
    {
        if (_audioEngine is null || KneeSlider is null || OutputSlider is null)
        {
            return;
        }
        _audioEngine.SetMasterControls(KneeSlider.Value, OutputSlider.Value, AutoReleaseCheck.IsChecked == true);
        SaveSuiteState();
    }

    private void SaveSuiteState()
    {
        if (!_isUiReady)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_suiteStatePath)!);
            var state = new SuiteState(
                Crossovers.Select(crossover => crossover.Frequency).ToArray(),
                Bands.Select(band => new SuiteBandState(
                    band.Threshold, band.Ratio, band.Range, band.Attack, band.Release,
                    band.MakeupGain, band.IsSolo, band.IsBypassed)).ToArray(),
                KneeSlider.Value, OutputSlider.Value, AutoReleaseCheck.IsChecked == true);
            var temporaryPath = _suiteStatePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state));
            File.Move(temporaryPath, _suiteStatePath, true);
        }
        catch
        {
            // Suite control remains usable with the last successfully published state.
        }
    }

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady || _loadingPreset || sender is not ComboBox { SelectedItem: string preset })
        {
            return;
        }
        ApplyPreset(preset);
        PresetNameBox.Text = _userPresets.ContainsKey(preset) ? preset : string.Empty;
        SavePresetStore(preset, false);
    }

    private void ApplyPreset(string preset)
    {
        _loadingPreset = true;
        if (_userPresets.TryGetValue(preset, out var userPreset))
        {
            ApplyUserPreset(userPreset);
            SyncGlobalControls();
            _loadingPreset = false;
            ResponseGraph?.InvalidateVisual();
            return;
        }

        var values = preset switch
        {
            "Smooth Voice" => ExpandPreset([(-20d, 2.5, 6d, 20d, 260d, 0d), (-24d, 3d, 8d, 15d, 220d, -1d), (-22d, 3d, 8d, 12d, 220d, 0d), (-21d, 2.5, 7d, 10d, 200d, 1d), (-18d, 2d, 5d, 8d, 180d, 0d), (-16d, 2d, 4d, 6d, 160d, 1d)]),
            "Tight Voice" => ExpandPreset([(-30d, 6d, 16d, 5d, 100d, -2d), (-34d, 7d, 18d, 4d, 90d, -2d), (-30d, 6d, 16d, 3d, 90d, 1d), (-28d, 7d, 18d, 2d, 80d, 2d), (-25d, 5d, 12d, 2d, 75d, 1d), (-22d, 4d, 10d, 1d, 70d, 0d)]),
            "Reset" => Enumerable.Repeat((-24d, 4d, 12d, 10d, 160d, 0d), BandCount).ToArray(),
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
        SyncGlobalControls();
        _loadingPreset = false;
        ResponseGraph?.InvalidateVisual();
    }

    private void SyncGlobalControls()
    {
        _resettingGlobalControls = true;
        GlobalThresholdKnob.Value = Bands.Average(band => band.Threshold);
        GlobalRatioKnob.Value = Bands.Average(band => band.Ratio);
        GlobalRangeKnob.Value = Bands.Average(band => band.Range);
        GlobalGainKnob.Value = Bands.Average(band => band.MakeupGain);
        GlobalAttackKnob.Value = Bands.Average(band => band.Attack);
        GlobalReleaseKnob.Value = Bands.Average(band => band.Release);
        _resettingGlobalControls = false;
    }

    private void ApplyUserPreset(UserPreset preset)
    {
        for (var index = 0; index < Crossovers.Count; index++)
        {
            Crossovers[index].Frequency = preset.Crossovers[index];
        }
        for (var index = 0; index < Bands.Count; index++)
        {
            var savedBand = preset.Bands[index];
            Bands[index].Threshold = savedBand.Threshold;
            Bands[index].Ratio = savedBand.Ratio;
            Bands[index].Range = savedBand.Range;
            Bands[index].Attack = savedBand.Attack;
            Bands[index].Release = savedBand.Release;
            Bands[index].MakeupGain = savedBand.MakeupGain;
            Bands[index].IsSolo = savedBand.IsSolo;
            Bands[index].IsBypassed = savedBand.IsBypassed;
        }
        KneeSlider.Value = preset.Knee;
        OutputSlider.Value = preset.OutputGain;
        AutoReleaseCheck.IsChecked = preset.AutoRelease;
        foreach (var item in BehaviorBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), preset.Behavior, StringComparison.Ordinal))
            {
                BehaviorBox.SelectedItem = item;
                break;
            }
        }
        ApplyMasterControls();
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var name = PresetNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Enter a name for this preset.", "Save preset", MessageBoxButton.OK, MessageBoxImage.Information);
            PresetNameBox.Focus();
            return;
        }
        if (PresetNames.Take(4).Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show("Choose a different name. Built-in preset names cannot be replaced.", "Save preset", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var behavior = (BehaviorBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Clean";
        var preset = new UserPreset(
            name,
            Crossovers.Select(crossover => crossover.Frequency).ToArray(),
            Bands.Select(band => new BandPreset(
                band.Threshold,
                band.Ratio,
                band.Range,
                band.Attack,
                band.Release,
                band.MakeupGain,
                band.IsSolo,
                band.IsBypassed)).ToArray(),
            KneeSlider.Value,
            OutputSlider.Value,
            AutoReleaseCheck.IsChecked == true,
            behavior);

        var isNew = !_userPresets.ContainsKey(name);
        _userPresets[name] = preset;
        if (isNew)
        {
            PresetNames.Add(name);
        }
        _loadingPreset = true;
        PresetBox.SelectedItem = name;
        _loadingPreset = false;
        SavePresetStore(name, true);
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not string name || !_userPresets.ContainsKey(name))
        {
            MessageBox.Show("Select a saved user preset to delete.", "Delete preset", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Delete the preset '{name}'?", "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _userPresets.Remove(name);
        PresetNames.Remove(name);
        PresetNameBox.Clear();
        PresetBox.SelectedItem = "Broadcast Voice";
    }

    private string LoadPresetStore()
    {
        try
        {
            if (!File.Exists(_presetSettingsPath))
            {
                return "Broadcast Voice";
            }
            var store = JsonSerializer.Deserialize<PresetStore>(File.ReadAllText(_presetSettingsPath));
            if (store is null)
            {
                return "Broadcast Voice";
            }
            foreach (var preset in store.Presets.Where(preset =>
                         preset.Bands.Length == Bands.Count && preset.Crossovers.Length == Crossovers.Count))
            {
                _userPresets[preset.Name] = preset;
                PresetNames.Add(preset.Name);
            }
            return store.LastPreset;
        }
        catch
        {
            return "Broadcast Voice";
        }
    }

    private void SavePresetStore(string lastPreset, bool showError)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_presetSettingsPath)!);
            var store = new PresetStore(lastPreset, _userPresets.Values.OrderBy(preset => preset.Name).ToArray());
            File.WriteAllText(_presetSettingsPath, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) when (!showError)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Unable to save preset", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BehaviorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady || _loadingPreset || sender is not ComboBox { SelectedItem: ComboBoxItem item })
        {
            return;
        }

        var behavior = item.Content?.ToString();
        KneeSlider.Value = behavior == "Opto" ? 12 : behavior == "Punch" ? 2 : 6;
        AutoReleaseCheck.IsChecked = behavior == "Opto";
    }

    private void AudioEngine_AudioError(object? sender, string error)
    {
        Dispatcher.Invoke(() =>
        {
            StopAudio();
            MessageBox.Show(error, "Audio stopped", MessageBoxButton.OK, MessageBoxImage.Error);
        });
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _meterTimer.Stop();
        _suiteTelemetry?.Dispose();
        _audioEngine.Dispose();
    }

    private sealed record RouteSettings(string InputId, string OutputId, bool AutoStart);
    private sealed record PresetStore(string LastPreset, UserPreset[] Presets);
    private sealed record UserPreset(
        string Name,
        double[] Crossovers,
        BandPreset[] Bands,
        double Knee,
        double OutputGain,
        bool AutoRelease,
        string Behavior);
    private sealed record BandPreset(
        double Threshold,
        double Ratio,
        double Range,
        double Attack,
        double Release,
        double MakeupGain,
        bool IsSolo,
        bool IsBypassed);
    private sealed record SuiteState(
        double[] Crossovers,
        SuiteBandState[] Bands,
        double Knee,
        double OutputGain,
        bool AutoRelease);
    private sealed record SuiteBandState(
        double Threshold,
        double Ratio,
        double Range,
        double Attack,
        double Release,
        double MakeupGain,
        bool IsSolo,
        bool IsBypassed);
}
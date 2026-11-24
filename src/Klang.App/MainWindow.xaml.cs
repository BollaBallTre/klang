using Klang.Audio;
using Klang.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NAudio.CoreAudioApi;
using Point = Windows.Foundation.Point;

namespace Klang.App;

public sealed partial class MainWindow : Window
{
    private readonly SettingsStore _store = new(SettingsStore.DefaultPath);
    private readonly KlangSettings _settings;
    private readonly EqEngine _engine = new();
    private readonly AppRouter _router = new();
    private readonly Slider[] _sliders = new Slider[Bands.Count];
    private readonly TextBlock[] _values = new TextBlock[Bands.Count];
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly DispatcherQueueTimer _pollTimer;

    private MMDevice? _cableInput;
    private IReadOnlyList<AudioApp> _apps = Array.Empty<AudioApp>();
    private AudioApp? _current;
    private string? _routedKey;
    private readonly HashSet<int> _routedPids = new();
    private bool _suppress;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _store.Load();
        Log.Info("---- Klang starter ----");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(980, 820));

        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => SaveNow();

        _pollTimer = DispatcherQueue.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(3);
        _pollTimer.Tick += (_, _) => Poll();

        _engine.Faulted += msg => DispatcherQueue.TryEnqueue(() => ShowEngineError(msg));
        Root.ActualThemeChanged += (_, _) => DrawCurve();
        Closed += OnClosed;

        BuildBands();
        BuildPresetList();
        CheckSetup();
        LoadSpeakers();
        StartEngine();
        RefreshApps(selectKey: _settings.LastApp);
        _pollTimer.Start();
    }

    // ---------- Oppsett ----------

    private void CheckSetup()
    {
        _cableInput = AudioDevices.FindCableInput();
        var cableOutput = AudioDevices.FindCableOutput();
        if (_cableInput is null || cableOutput is null)
        {
            SetupBar.Title = "VB-Cable er ikke installert";
            SetupBar.Message = "Klang trenger VB-Cable for å hente lyden fra ett program. Installer det, start PC-en på nytt og åpne Klang igjen.";
            SetupLink.Content = "Last ned VB-Cable";
            SetupLink.NavigateUri = new Uri("https://vb-audio.com/Cable/");
            SetupBar.Severity = InfoBarSeverity.Error;
            SetupBar.IsOpen = true;
            Log.Info("VB-Cable ikke funnet.");
            return;
        }
        Log.Info($"VB-Cable funnet: '{_cableInput.FriendlyName}' / '{cableOutput.FriendlyName}'. Ruting støttet: {_router.IsSupported}");
        if (AudioDevices.DefaultOutputIsCable())
        {
            SetupBar.Title = "All lyd går til VB-Cable";
            SetupBar.Message = "Windows bruker CABLE Input som standard avspillingsenhet. Velg høyttalerne dine som standard i lydinnstillingene, så får bare programmet du velger EQ.";
            SetupLink.Content = "Åpne lydinnstillinger";
            SetupLink.NavigateUri = new Uri("ms-settings:sound");
            SetupBar.Severity = InfoBarSeverity.Warning;
            SetupBar.IsOpen = true;
            return;
        }
        SetupBar.IsOpen = false;
    }

    private void LoadSpeakers()
    {
        _suppress = true;
        var speakers = AudioDevices.Speakers();
        SpeakerCombo.ItemsSource = speakers;
        SpeakerCombo.DisplayMemberPath = nameof(DeviceInfo.Name);
        var chosen = AudioDevices.ResolveSpeaker(_settings.OutputDeviceId);
        SpeakerCombo.SelectedItem = speakers.FirstOrDefault(s => s.Id == chosen?.ID);
        _suppress = false;
    }

    private void StartEngine()
    {
        EngineBar.IsOpen = false;
        var cableOutput = AudioDevices.FindCableOutput();
        var speaker = AudioDevices.ResolveSpeaker(_settings.OutputDeviceId);
        if (cableOutput is null) return; // SetupBar forklarer allerede.
        if (speaker is null)
        {
            ShowEngineError("Fant ingen høyttaler eller hodetelefoner å spille lyden på.");
            return;
        }
        try
        {
            _engine.Start(cableOutput, speaker);
        }
        catch (Exception ex)
        {
            Log.Info($"Motor feilet ved start: {ex}");
            ShowEngineError(ex.Message);
        }
        UpdateFooter();
    }

    private void ShowEngineError(string message)
    {
        EngineBar.Title = "Lyden stoppet";
        EngineBar.Severity = InfoBarSeverity.Error;
        EngineBar.Message = $"Klang klarte ikke å spille lyden videre. {message}";
        EngineBar.IsOpen = true;
        UpdateFooter();
    }

    private void RestartEngine_Click(object sender, RoutedEventArgs e)
    {
        CheckSetup();
        LoadSpeakers();
        StartEngine();
    }

    // ---------- Programmer ----------

    private void RefreshApps(string? selectKey = null)
    {
        _apps = AudioApps.List();
        _suppress = true;
        AppCombo.ItemsSource = _apps;
        var key = selectKey ?? _current?.Key;
        var match = _apps.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
        AppCombo.SelectedItem = match;
        _suppress = false;
        if (match is not null && !ReferenceEquals(match, _current)) SelectApp(match);
        UpdateAppStatus();
    }

    private void Poll()
    {
        var fresh = AudioApps.List();
        bool changed = fresh.Count != _apps.Count || fresh.Zip(_apps).Any(p => p.First.Key != p.Second.Key);
        _apps = fresh;
        if (changed)
        {
            _suppress = true;
            AppCombo.ItemsSource = _apps;
            AppCombo.SelectedItem = _apps.FirstOrDefault(a => a.Key == _current?.Key);
            _suppress = false;
        }

        var now = _apps.FirstOrDefault(a => a.Key == _current?.Key);
        if (now is not null && _routedKey == now.Key)
        {
            // Programmet kan ha startet nye prosesser, f.eks. etter omstart.
            var newPids = now.ProcessIds.Where(p => !_routedPids.Contains(p)).ToList();
            if (newPids.Count > 0 && _cableInput is not null && _router.Route(newPids, _cableInput.ID))
                foreach (var p in newPids) _routedPids.Add(p);
        }

        // Rydd opp etter en tidligere krasj: programmer som fortsatt peker til VB-Cable uten å være valgt.
        foreach (var stale in _settings.RoutedApps.Where(k => k != _routedKey).ToList())
        {
            var app = _apps.FirstOrDefault(a => string.Equals(a.Key, stale, StringComparison.OrdinalIgnoreCase));
            if (app is not null && _router.Reset(app.ProcessIds))
            {
                _settings.RoutedApps.Remove(stale);
                ScheduleSave();
            }
        }

        UpdateAppStatus();
        UpdateFooter();

        var (peak, bytes) = _engine.TakeInputStats();
        bool signal = peak > 0.0005f;
        LevelText.Text = !_engine.IsRunning ? "" : bytes == 0 ? "Ingen data fra VB-Cable" : signal ? "Lyd kommer inn fra VB-Cable" : "Stille fra VB-Cable";
        Log.Info($"Status: motor={_engine.IsRunning} byte inn={bytes} topp={peak:0.0000} program={_current?.Key} rutet={_routedKey} pids=[{string.Join(",", _routedPids)}]");
    }

    private void AppCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (AppCombo.SelectedItem is AudioApp app) SelectApp(app);
    }

    private void RefreshApps_Click(object sender, RoutedEventArgs e) => RefreshApps();

    private void SelectApp(AudioApp app)
    {
        // Flytt forrige program tilbake til vanlig lyd.
        if (_routedKey is not null && _routedKey != app.Key)
        {
            var prev = _apps.FirstOrDefault(a => a.Key == _routedKey);
            var pids = prev?.ProcessIds ?? _routedPids.ToList();
            if (_router.Reset(pids)) _settings.RoutedApps.Remove(_routedKey);
            _routedKey = null;
            _routedPids.Clear();
        }

        _current = app;
        _settings.LastApp = app.Key;
        Log.Info($"Valgte program {app.Key} ({app.DisplayName}), pids [{string.Join(",", app.ProcessIds)}]");
        RouteCurrent();
        LoadAppState();
        UpdateAppStatus();
        UpdateFooter();
        ScheduleSave();
    }

    private void RouteCurrent()
    {
        RouteBar.IsOpen = false;
        if (_current is null || _cableInput is null) return;
        if (_router.IsSupported && _router.Route(_current.ProcessIds, _cableInput.ID))
        {
            _routedKey = _current.Key;
            _routedPids.Clear();
            foreach (var p in _current.ProcessIds) _routedPids.Add(p);
            if (!_settings.RoutedApps.Contains(_current.Key, StringComparer.OrdinalIgnoreCase)) _settings.RoutedApps.Add(_current.Key);
            // Noen programmer (f.eks. VLC) holder fast på lydenheten til avspillingen startes på nytt.
            RouteBar.Severity = InfoBarSeverity.Informational;
            RouteBar.Title = "Hører du ingenting?";
            RouteBar.Message = $"Stopp avspillingen i {_current.DisplayName} og start den igjen, så tar Klang over lyden.";
            OpenMixerButton.Visibility = Visibility.Collapsed;
            RouteBar.IsOpen = true;
            return;
        }
        RouteBar.Severity = InfoBarSeverity.Warning;
        OpenMixerButton.Visibility = Visibility.Visible;
        RouteBar.Title = "Gjør ett steg selv";
        RouteBar.Message = $"Klang fikk ikke flyttet lyden fra {_current.DisplayName} automatisk. Åpne volumblanderen, finn {_current.DisplayName} og sett «Utdataenhet» til «CABLE Input».";
        RouteBar.IsOpen = true;
    }

    private void OpenMixer_Click(object sender, RoutedEventArgs e) =>
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:apps-volume"));

    private void UpdateAppStatus()
    {
        var now = _current is null ? null : _apps.FirstOrDefault(a => a.Key == _current.Key);
        string key = now?.IsPlaying == true ? "SystemFillColorSuccessBrush" : "TextFillColorTertiaryBrush";
        PlayingDot.Fill = Res(key);
        AppStatusText.Text = _current is null
            ? (_apps.Count == 0 ? "Ingen programmer spiller lyd. Start avspilling og trykk Oppdater." : "Velg et program")
            : now is null ? "Programmet er lukket"
            : now.IsPlaying ? "Spiller lyd nå" : "Spiller ikke lyd akkurat nå";
    }

    // ---------- Innstilling per program ----------

    private AppEqState CurrentState()
    {
        var key = _current?.Key ?? "";
        if (!_settings.Apps.TryGetValue(key, out var s))
        {
            s = new AppEqState { PresetName = Presets.FlatName, Gains = Bands.Flat(), Enabled = true };
            if (_current is not null) _settings.Apps[key] = s;
        }
        return s;
    }

    private void LoadAppState()
    {
        var s = CurrentState();
        _engine.SetGains(s.Gains);
        _engine.SetEnabled(s.Enabled);
        _suppress = true;
        PowerToggle.IsOn = s.Enabled;
        SelectPresetInCombo(s.PresetName);
        for (int i = 0; i < Bands.Count; i++) _sliders[i].Value = s.Gains[i];
        _suppress = false;
        Refresh();
    }

    // ---------- Forhåndsinnstillinger ----------

    private void BuildPresetList()
    {
        _suppress = true;
        PresetCombo.Items.Clear();
        PresetCombo.Items.Add(Header("Ferdige"));
        foreach (var p in Presets.BuiltIn) PresetCombo.Items.Add(Item(p.Name));
        PresetCombo.Items.Add(Header(_settings.CustomPresets.Count == 0 ? "Mine (ingen ennå)" : "Mine"));
        foreach (var c in _settings.CustomPresets.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)) PresetCombo.Items.Add(Item(c.Name));
        PresetCombo.Items.Add(Header("Annet"));
        PresetCombo.Items.Add(Item(Presets.FlatName, "Flat (ingen endring)"));
        _suppress = false;

        static ComboBoxItem Header(string text) => new() { Content = text, IsEnabled = false, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        static ComboBoxItem Item(string name, string? display = null) => new() { Content = display ?? name, Tag = name, Padding = new Thickness(20, 6, 12, 6) };
    }

    private void SelectPresetInCombo(string name)
    {
        var item = PresetCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag is string t && string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
        PresetCombo.SelectedItem = item;
        PresetCombo.PlaceholderText = item is null ? "Egen justering" : "";
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _current is null) return;
        if (PresetCombo.SelectedItem is not ComboBoxItem { Tag: string name }) return;
        var gains = SettingsStore.GainsFor(_settings, name);
        if (gains is null) return;
        ApplyPreset(name, gains);
    }

    private void ApplyPreset(string name, double[] gains)
    {
        var s = CurrentState();
        s.PresetName = name;
        s.Gains = gains;
        _engine.SetGains(gains);
        _suppress = true;
        SelectPresetInCombo(name);
        for (int i = 0; i < Bands.Count; i++) _sliders[i].Value = gains[i];
        _suppress = false;
        Refresh();
        ScheduleSave();
    }

    private void Flat_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        ApplyPreset(Presets.FlatName, Bands.Flat());
    }

    private void PowerToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppress || _current is null) return;
        var s = CurrentState();
        s.Enabled = PowerToggle.IsOn;
        _engine.SetEnabled(s.Enabled);
        Refresh();
        ScheduleSave();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var s = CurrentState();
        var box = new TextBox
        {
            Header = "Navn",
            PlaceholderText = "F.eks. Kveldsfilm",
            MaxLength = SettingsStore.MaxNameLength,
            Text = _settings.CustomPresets.Any(c => string.Equals(c.Name, s.PresetName, StringComparison.OrdinalIgnoreCase)) ? s.PresetName : "",
        };
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Res("SystemFillColorCautionBrush"), Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Lagre innstilling",
            Content = new StackPanel { Spacing = 8, Children = { box, hint } },
            PrimaryButtonText = "Lagre",
            CloseButtonText = "Avbryt",
            DefaultButton = ContentDialogButton.Primary,
        };
        void Validate()
        {
            var problem = SettingsStore.ValidateName(box.Text);
            bool exists = _settings.CustomPresets.Any(c => string.Equals(c.Name, box.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            hint.Text = problem switch
            {
                SaveNameProblem.Empty => "",
                SaveNameProblem.ReservedName => "Det navnet brukes av en ferdig innstilling. Velg et annet.",
                SaveNameProblem.TooLong => $"Navnet kan ha maks {SettingsStore.MaxNameLength} tegn.",
                _ => exists ? "Du har allerede en innstilling med dette navnet. Den blir overskrevet." : "",
            };
            dialog.IsPrimaryButtonEnabled = problem == SaveNameProblem.None;
        }
        box.TextChanged += (_, _) => Validate();
        Validate();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var saved = SettingsStore.Upsert(_settings, box.Text, s.Gains);
        s.PresetName = saved.Name;
        BuildPresetList();
        _suppress = true;
        SelectPresetInCombo(saved.Name);
        _suppress = false;
        Refresh();
        SaveNow();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var s = CurrentState();
        var name = s.PresetName;
        if (!_settings.CustomPresets.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) return;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = $"Slette «{name}»?",
            Content = "Bryterne blir stående som de er nå. Du kan lagre dem på nytt senere.",
            PrimaryButtonText = "Slett",
            CloseButtonText = "Avbryt",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        SettingsStore.Delete(_settings, name);
        BuildPresetList();
        _suppress = true;
        SelectPresetInCombo("");
        _suppress = false;
        Refresh();
        SaveNow();
    }

    // ---------- Skyvebrytere ----------

    private void BuildBands()
    {
        BandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var scale = new Grid { Margin = new Thickness(0, 24, 4, 26), Height = 180, VerticalAlignment = VerticalAlignment.Top };
        foreach (var (text, align) in new[] { ("+12", VerticalAlignment.Top), ("0", VerticalAlignment.Center), ("−12", VerticalAlignment.Bottom) })
            scale.Children.Add(new TextBlock { Text = text, FontSize = 10, VerticalAlignment = align, HorizontalAlignment = HorizontalAlignment.Right, Foreground = Res("TextFillColorTertiaryBrush") });
        BandGrid.Children.Add(scale);

        for (int i = 0; i < Bands.Count; i++)
        {
            BandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int band = i;
            var value = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, Text = "0 dB" };
            var slider = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = Bands.MinGainDb,
                Maximum = Bands.MaxGainDb,
                StepFrequency = Bands.GainStepDb,
                SmallChange = Bands.GainStepDb,
                LargeChange = 3,
                TickFrequency = 6,
                TickPlacement = Microsoft.UI.Xaml.Controls.Primitives.TickPlacement.Outside,
                Height = 180,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, $"{Bands.Labels[i]} hertz");
            slider.ValueChanged += (_, e) => OnBandChanged(band, e.NewValue);
            var label = new TextBlock { Text = Bands.Labels[i], HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11, Foreground = Res("TextFillColorSecondaryBrush") };
            var col = new StackPanel { Spacing = 4, Children = { value, slider, label } };
            Grid.SetColumn(col, i + 1);
            BandGrid.Children.Add(col);
            _sliders[i] = slider;
            _values[i] = value;
        }
    }

    private void OnBandChanged(int band, double value)
    {
        _values[band].Text = FormatDb(value);
        if (_suppress || _current is null) return;
        var s = CurrentState();
        s.Gains[band] = Bands.Clamp(value);
        _engine.SetGains(s.Gains);
        Refresh();
        ScheduleSave();
    }

    private static string FormatDb(double v) =>
        (v > 0 ? "+" : v < 0 ? "−" : "") + Math.Abs(v).ToString("0.#") + " dB";

    // ---------- Visning ----------

    private void Refresh()
    {
        var s = CurrentState();
        for (int i = 0; i < Bands.Count; i++) _values[i].Text = FormatDb(s.Gains[i]);
        var baseGains = SettingsStore.GainsFor(_settings, s.PresetName);
        DirtyBadge.Visibility = baseGains is not null && !baseGains.SequenceEqual(s.Gains) ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.IsEnabled = _settings.CustomPresets.Any(c => string.Equals(c.Name, s.PresetName, StringComparison.OrdinalIgnoreCase));
        var cut = FrequencyResponse.AutoPreampCut(s.Gains);
        PreampText.Text = s.Enabled && cut > 0 ? $"Volumvern −{cut:0.#} dB" : "";
        bool hasApp = _current is not null;
        PresetCombo.IsEnabled = SaveButton.IsEnabled = FlatButton.IsEnabled = PowerToggle.IsEnabled = hasApp;
        foreach (var sl in _sliders) sl.IsEnabled = hasApp;
        DrawCurve();
    }

    private void UpdateFooter()
    {
        var name = _current?.DisplayName ?? "Ingen program valgt";
        RouteText.Text = _engine.IsRunning
            ? $"{name} → CABLE Input → Klang → {_engine.SpeakerName}"
            : $"{name} → Klang er stoppet";
        LatencyText.Text = _engine.IsRunning ? $"Forsinkelse ca. {_engine.EstimatedLatencyMs} ms" : "";
    }

    private void SpeakerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || SpeakerCombo.SelectedItem is not DeviceInfo d) return;
        _settings.OutputDeviceId = d.Id;
        SaveNow();
        StartEngine();
    }

    private void CurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawCurve();

    private void DrawCurve()
    {
        var canvas = CurveCanvas;
        canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w < 40 || h < 40) return;

        const double padL = 30, padB = 16, top = 6, range = 15;
        double Fx(double f) => padL + Math.Log10(f / 20) / Math.Log10(1000) * (w - padL - 6);
        double Fy(double db) => top + (range - Math.Clamp(db, -range, range)) / (2 * range) * (h - padB - top);

        var grid = Res("DividerStrokeColorDefaultBrush");
        var zero = Res("TextFillColorTertiaryBrush");
        var labelBrush = Res("TextFillColorTertiaryBrush");

        foreach (var db in new[] { -12, -6, 0, 6, 12 })
        {
            canvas.Children.Add(new Line { X1 = padL, X2 = w, Y1 = Fy(db), Y2 = Fy(db), Stroke = db == 0 ? zero : grid, StrokeThickness = 1 });
            AddLabel(db == 0 ? "0" : db > 0 ? $"+{db}" : $"−{-db}", padL - 24, Fy(db) - 7);
        }
        foreach (var (f, text) in new[] { (100.0, "100 Hz"), (1000.0, "1k Hz"), (10000.0, "10k Hz") })
        {
            canvas.Children.Add(new Line { X1 = Fx(f), X2 = Fx(f), Y1 = top, Y2 = h - padB, Stroke = grid, StrokeThickness = 1 });
            AddLabel(text, Fx(f) - 16, h - padB + 1);
        }

        var s = CurrentState();
        var curve = FrequencyResponse.Curve(s.Gains);
        var accent = s.Enabled ? Res("AccentFillColorDefaultBrush") : Res("TextFillColorTertiaryBrush");
        var line = new Polyline { Stroke = accent, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        var area = new Polygon { Fill = accent, Opacity = s.Enabled ? 0.15 : 0 };
        foreach (var (f, db) in curve)
        {
            var p = new Point(Fx(f), Fy(db));
            line.Points.Add(p);
            area.Points.Add(p);
        }
        area.Points.Add(new Point(Fx(curve[^1].Frequency), Fy(0)));
        area.Points.Add(new Point(Fx(curve[0].Frequency), Fy(0)));
        canvas.Children.Add(area);
        canvas.Children.Add(line);

        void AddLabel(string text, double x, double y)
        {
            var t = new TextBlock { Text = text, FontSize = 10, Foreground = labelBrush };
            Canvas.SetLeft(t, x);
            Canvas.SetTop(t, y);
            canvas.Children.Add(t);
        }
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    // ---------- Lagring og avslutning ----------

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        try { _store.Save(_settings); }
        catch (Exception ex)
        {
            EngineBar.Title = "Kunne ikke lagre";
            EngineBar.Message = $"Innstillingene ble ikke lagret: {ex.Message}";
            EngineBar.Severity = InfoBarSeverity.Warning;
            EngineBar.IsOpen = true;
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _pollTimer.Stop();
        // Gi programmet vanlig lyd tilbake, ellers blir det stille når Klang ikke kjører.
        if (_routedKey is not null)
        {
            var app = _apps.FirstOrDefault(a => a.Key == _routedKey);
            if (_router.Reset(app?.ProcessIds ?? _routedPids.ToList())) _settings.RoutedApps.Remove(_routedKey);
        }
        SaveNow();
        _engine.Dispose();
        _router.Dispose();
    }
}

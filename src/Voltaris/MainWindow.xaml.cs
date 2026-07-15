using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Voltaris.Models;
using Voltaris.Services;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Voltaris;

public partial class MainWindow : Window
{
    private readonly BatteryTelemetryService _telemetry = new();
    private readonly CapacityTestEngine _test = new();
    private readonly StateStore _store = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Queue<double> _powerHistory = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly PersistedState _state;
    private BatteryStaticInfo? _staticInfo;
    private BatteryReading? _lastReading;
    private Forms.NotifyIcon? _tray;
    private bool _polling;
    private int _ticksSinceSave;

    public MainWindow()
    {
        InitializeComponent();
        _state = _store.Load();
        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
        _timer.Tick += PollBattery;
        SetupTrayIcon();
        UpdateSavedResults();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _staticInfo = await _telemetry.ReadStaticInfoAsync(_shutdown.Token);
        UpdateStaticInfo();
        await PollOnce();
        _timer.Start();
    }

    private async void PollBattery(object? sender, EventArgs e) => await PollOnce();

    private async Task PollOnce()
    {
        if (_polling || _shutdown.IsCancellationRequested) return;
        _polling = true;
        try
        {
            var reading = await _telemetry.ReadAsync(_staticInfo, _shutdown.Token);
            UpdateObservedCycles(reading);
            _lastReading = reading;
            UpdateLiveUi(reading);
            UpdateTest(reading);

            if (++_ticksSinceSave >= 60)
            {
                _ticksSinceSave = 0;
                _store.Save(_state);
            }
        }
        catch (OperationCanceledException) { }
        finally { _polling = false; }
    }

    private void UpdateLiveUi(BatteryReading reading)
    {
        LastUpdatedText.Text = $"обновлено {reading.Timestamp:HH:mm:ss}";
        LiveStateText.Text = reading.IsPresent ? "Данные поступают" : "Батарея не найдена";
        LiveDot.Fill = Brush(reading.IsPresent ? "#43E6A4" : "#FF7777");
        StatusText.Text = reading.StateText;
        BatteryStateText.Text = reading.StateText;
        PowerDot.Fill = Brush(reading.IsCharging ? "#63E5FF" : reading.IsDischarging ? "#FFBE5C" : "#43E6A4");
        BatteryPercentText.Text = reading.IsPresent ? $"{reading.Percent:0}%" : "--%";
        RemainingText.Text = reading.RemainingCapacityMwh is { } remaining ? $"{remaining:N0} мВт·ч осталось" : "Остаток не сообщается";
        RateText.Text = reading.RateMw is { } rate ? $"{rate / 1000d:+0.0;-0.0;0.0} Вт" : "Нет данных";
        CurrentText.Text = reading.CurrentMa is { } current ? $"{current / 1000d:+0.00;-0.00;0.00} А" : "Нет данных";
        VoltageText.Text = reading.VoltageMv is { } voltage ? $"{voltage / 1000d:0.00} В" : "Нет данных";
        TemperatureText.Text = reading.TemperatureC is { } temp ? $"{temp:0.0} °C" : "Нет датчика";
        TemperatureHintText.Text = reading.TemperatureC is null ? "Не передаётся прошивкой" : "Датчик аккумулятора";
        PowerSourceText.Text = reading.IsPowerOnline ? "Сеть / адаптер" : "Аккумулятор";
        SetBatteryArc(reading.Percent);

        _powerHistory.Enqueue(Math.Abs(reading.RateMw ?? 0) / 1000d);
        while (_powerHistory.Count > 90) _powerHistory.Dequeue();
        DrawChart();
    }

    private void UpdateStaticInfo()
    {
        var info = _staticInfo;
        if (info is null) return;
        BatteryNameText.Text = $"{info.Name}  ·  {info.Manufacturer}";
        DesignEnergyText.Text = info.DesignCapacityMwh is { } design ? $"{design / 1000d:N2} Вт·ч" : "Не сообщается";
        FullEnergyText.Text = info.FullChargeCapacityMwh is { } full ? $"{full / 1000d:N2} Вт·ч" : "Не сообщается";
        HealthText.Text = info.HealthPercent is { } health ? $"{health:N1}%" : "Нет данных";
        ChemistryText.Text = FriendlyChemistry(info.Chemistry);
        FirmwareCyclesText.Text = info.FirmwareCycleCount is { } cycles ? cycles.ToString("N0") : "Не сообщаются";
        CycleOemText.Text = info.FirmwareCycleCount?.ToString("N0") ?? "—";
        OriginalCapacityText.Text = info.DesignCapacityMah is { } mah ? $"{mah:N0} мА·ч" : "Нет данных";
        OriginalEnergyHint.Text = info.DesignCapacityMwh is { } mwh ? $"{mwh / 1000d:N2} Вт·ч по паспорту" : "По паспорту Windows";
    }

    private void UpdateTest(BatteryReading reading)
    {
        if (!_test.IsArmed) return;
        _test.Add(reading);
        ActiveTestPanel.Visibility = Visibility.Visible;
        FinishTestButton.IsEnabled = true;

        if (!_test.IsRunning)
        {
            TestStateText.Text = "Ожидание начала зарядки";
            TestProgressText.Text = "Подключите адаптер: отсчёт начнётся при появлении положительного тока";
            FinishTestButton.Content = "Отменить";
            return;
        }

        TestStateText.Text = "Идёт точный замер";
        TestProgressText.Text = $"Пройдено {_test.CoveredPercent:0.0}% · принято {_test.ChargedMah:N0} мА·ч · {_test.SampleCount:N0} точек";
        FinishTestButton.Content = _test.CanFinish ? "Завершить частичный тест" : "Отменить";
        if (_test.ShouldAutoComplete) CompleteTest();
    }

    private void OpenTestWizard_Click(object sender, RoutedEventArgs e)
    {
        if (_test.IsArmed) return;
        var wizard = new CapacityTestWindow { Owner = this };
        if (wizard.ShowDialog() != true || !wizard.Confirmed) return;

        _test.Arm(_staticInfo);
        PowerAwake.KeepSystemAwake(true);
        ActiveTestPanel.Visibility = Visibility.Visible;
        TestStateText.Text = "Ожидание начала зарядки";
        TestProgressText.Text = "Подключите зарядное устройство";
        FinishTestButton.IsEnabled = true;
        FinishTestButton.Content = "Отменить";
        TestButton.IsEnabled = false;
        TestButton.Content = "Замер запущен";
        if (_lastReading is not null) UpdateTest(_lastReading);
    }

    private void FinishTest_Click(object sender, RoutedEventArgs e)
    {
        if (_test.CanFinish)
            CompleteTest();
        else
        {
            _test.Cancel();
            ResetTestUi();
        }
    }

    private void CompleteTest()
    {
        var result = _test.Finish(DateTimeOffset.Now);
        if (result is null) return;
        _state.Measurements.Insert(0, result);
        if (_state.Measurements.Count > 50) _state.Measurements.RemoveRange(50, _state.Measurements.Count - 50);
        _store.Save(_state);
        UpdateSavedResults();
        ResetTestUi();
        new TestResultWindow(result) { Owner = this }.ShowDialog();
    }

    private void ResetTestUi()
    {
        PowerAwake.KeepSystemAwake(false);
        ActiveTestPanel.Visibility = Visibility.Collapsed;
        TestButton.IsEnabled = true;
        TestButton.Content = "Проверить реальную ёмкость";
    }

    private void UpdateSavedResults()
    {
        ObservedCyclesText.Text = _state.ObservedEquivalentCycles.ToString("0.00", CultureInfo.CurrentCulture);
        var latest = _state.Measurements.OrderByDescending(x => x.CompletedAt).FirstOrDefault();
        if (latest is null)
        {
            MeasuredCapacityText.Text = "Тест не проводился";
            MeasuredDateText.Text = "Запустите первый замер";
            return;
        }
        MeasuredCapacityText.Text = $"{latest.MeasuredCapacityMah:N0} мА·ч";
        MeasuredDateText.Text = $"{latest.CompletedAt:dd.MM.yyyy HH:mm} · точность: {latest.Accuracy.ToLowerInvariant()}";
    }

    private void UpdateObservedCycles(BatteryReading reading)
    {
        if (_lastReading is { IsPresent: true, IsDischarging: true } previous && reading.IsDischarging)
        {
            var drop = previous.Percent - reading.Percent;
            if (drop is > 0 and < 5)
                _state.ObservedDischargePercent += drop;
        }
        ObservedCyclesText.Text = _state.ObservedEquivalentCycles.ToString("0.00", CultureInfo.CurrentCulture);
    }

    private void SetBatteryArc(double percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        const double center = 109;
        const double radius = 91;
        var start = PointOnCircle(center, center, radius, -90);
        var endAngle = -90 + Math.Min(percent * 3.6, 359.99);
        var end = PointOnCircle(center, center, radius, endAngle);
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, percent > 50, SweepDirection.Clockwise, true));
        BatteryArc.Data = new PathGeometry([figure]);
    }

    private static Point PointOnCircle(double cx, double cy, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180d;
        return new Point(cx + radius * Math.Cos(radians), cy + radius * Math.Sin(radians));
    }

    private void DrawChart()
    {
        if (ChartCanvas.ActualWidth <= 1 || ChartCanvas.ActualHeight <= 1 || _powerHistory.Count == 0) return;
        var values = _powerHistory.ToArray();
        var max = Math.Max(10, values.Max() * 1.12);
        var points = new PointCollection(values.Select((value, index) => new Point(
            values.Length == 1 ? 0 : index * ChartCanvas.ActualWidth / (values.Length - 1),
            ChartCanvas.ActualHeight - Math.Clamp(value / max, 0, 1) * (ChartCanvas.ActualHeight - 8) - 4)));
        ChartLine.Points = points;
        ChartLineGlow.Points = points;
        ChartPeakText.Text = $"Пик {values.Max():0.0} Вт";
    }

    private static string FriendlyChemistry(string chemistry) => chemistry.Trim().ToUpperInvariant() switch
    {
        "LION" or "LI-I" or "LIPO" => "Li-ion",
        "PBAC" => "Свинцовая",
        "NIMH" => "NiMH",
        "NICD" => "NiCd",
        var raw when string.IsNullOrWhiteSpace(raw) => "Не сообщается",
        var raw => raw
    };

    private void ExportHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_state.Measurements.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "История пока пуста. Сначала проведите хотя бы один тест.", "Voltaris", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Экспорт истории Voltaris",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"voltaris-history-{DateTime.Now:yyyy-MM-dd}.csv"
        };
        if (dialog.ShowDialog(this) != true) return;

        var csv = new StringBuilder("Дата;Начало_% ;Конец_% ;Ёмкость_мАч;Энергия_мВтч;Длительность_мин;Точность;Циклы_OEM\r\n".Replace(" ", ""));
        foreach (var item in _state.Measurements.OrderByDescending(x => x.CompletedAt))
            csv.AppendLine(string.Join(';', item.CompletedAt.ToString("s"), item.StartPercent.ToString("0.0", CultureInfo.InvariantCulture), item.EndPercent.ToString("0.0", CultureInfo.InvariantCulture), item.MeasuredCapacityMah.ToString("0.0", CultureInfo.InvariantCulture), item.MeasuredCapacityMwh.ToString("0.0", CultureInfo.InvariantCulture), item.DurationMinutes.ToString("0.0", CultureInfo.InvariantCulture), item.Accuracy, item.FirmwareCycleCount?.ToString() ?? ""));
        File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
    }

    private void SetupTrayIcon()
    {
        _tray = new Forms.NotifyIcon
        {
            Text = "Voltaris Battery Lab",
            Visible = true,
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        _tray.ContextMenuStrip.Items.Add("Открыть Voltaris", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        _tray.ContextMenuStrip.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(Close));
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized) return;
        Hide();
        ShowInTaskbar = false;
        _tray?.ShowBalloonTip(1800, "Voltaris работает в фоне", "Показания аккумулятора продолжают обновляться.", Forms.ToolTipIcon.Info);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _timer.Stop();
        _shutdown.Cancel();
        PowerAwake.KeepSystemAwake(false);
        try { _store.Save(_state); } catch { }
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
    }

    private static SolidColorBrush Brush(string color) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();
    private void ScrollToTest_Click(object sender, RoutedEventArgs e) => TestComparisonSection.BringIntoView();
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) ToggleMaximize(); else if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

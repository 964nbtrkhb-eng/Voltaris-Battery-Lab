using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Voltaris;

public partial class CapacityTestWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch _stopwatch = new();

    public bool Confirmed { get; private set; }

    public CapacityTestWindow()
    {
        InitializeComponent();
        _timer.Tick += Animate;
        Loaded += (_, _) => { _stopwatch.Start(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void Animate(object? sender, EventArgs e)
    {
        var seconds = Math.Min(7, _stopwatch.Elapsed.TotalSeconds);
        int percent;
        if (seconds < 2.2)
        {
            var t = seconds / 2.2;
            percent = (int)Math.Round(18 - 17 * t);
            DemoStageText.Text = "1 · Разряд до старта";
            DemoStageHint.Text = "Подготовка аккумулятора";
            DemoVoltageText.Text = $"{15.4 - t * 0.8:0.0} В";
            DemoCurrentText.Text = "−1,28 А";
            DemoPowerText.Text = "−19,5 Вт";
            StageDot1.Fill = Brush("#63E5FF");
        }
        else if (seconds < 5.8)
        {
            var t = (seconds - 2.2) / 3.6;
            percent = (int)Math.Round(1 + 99 * t);
            DemoStageText.Text = "2 · Контролируемая зарядка";
            DemoStageHint.Text = "Сбор показаний каждую секунду";
            DemoVoltageText.Text = $"{15.1 + t * 1.8:0.0} В";
            DemoCurrentText.Text = $"{2.65 - t * 1.2:0.00} А";
            DemoPowerText.Text = $"{40.0 - t * 14:0.0} Вт";
            StageDot2.Fill = Brush("#63E5FF");
        }
        else
        {
            percent = 100;
            DemoStageText.Text = "3 · Готовый результат";
            DemoStageHint.Text = "Реальная ёмкость: 4 382 мА·ч";
            DemoVoltageText.Text = "16,4 В";
            DemoCurrentText.Text = "0,18 А";
            DemoPowerText.Text = "3,0 Вт";
            StageDot3.Fill = Brush("#43E6A4");
        }

        DemoPercentText.Text = $"{percent}%";
        DemoBatteryFill.Width = Math.Max(10, 188 * percent / 100d);
        DemoBatteryFill.Background = percent < 15 ? Brush("#FFBE5C") : percent >= 100 ? Brush("#43E6A4") : Brush("#4FDDF5");
        DemoTimeText.Text = $"00:0{Math.Min(7, (int)seconds)}";
        if (seconds >= 7) _timer.Stop();
    }

    private static SolidColorBrush Brush(string color) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    private void Start_Click(object sender, RoutedEventArgs e) { Confirmed = true; DialogResult = true; }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}

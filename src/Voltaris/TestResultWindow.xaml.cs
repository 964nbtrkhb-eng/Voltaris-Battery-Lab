using System.Windows;
using System.Windows.Input;
using Voltaris.Models;

namespace Voltaris;

public partial class TestResultWindow : Window
{
    public TestResultWindow(CapacityMeasurement result)
    {
        InitializeComponent();
        CapacityText.Text = $"{result.MeasuredCapacityMah:N0} мА·ч";
        AccuracyText.Text = $"Точность: {result.Accuracy.ToLowerInvariant()}";
        EnergyText.Text = $"{result.MeasuredCapacityMwh / 1000d:N2} Вт·ч";
        DurationText.Text = result.DurationMinutes >= 60
            ? $"{(int)(result.DurationMinutes / 60)} ч {result.DurationMinutes % 60:0} мин"
            : $"{result.DurationMinutes:0} мин";
        RangeText.Text = $"{result.StartPercent:0}% → {result.EndPercent:0}%";
        AvgVoltageText.Text = $"{result.AverageVoltageMv / 1000d:N2} В";
        AvgPowerText.Text = $"{result.AverageChargeRateMw / 1000d:N1} Вт";
        PeakPowerText.Text = $"{result.PeakChargeRateMw / 1000d:N1} Вт";
        SamplesText.Text = $"{result.SampleCount:N0}";
        HealthText.Text = result.HealthPercent is { } health ? $"{health:N1}%" : "Нет паспорта";
        TypeText.Text = $"{(result.IsPartial ? "Частичный" : "Полный")} · {result.MeasurementMethod}";
        ResultHintText.Text = result.CoveredPercent < 20
            ? "Диапазон меньше 20%: используйте результат только как ориентир и повторите более длинный тест."
            : "Результат сохранён локально и появился на главном экране.";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}

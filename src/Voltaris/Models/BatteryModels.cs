namespace Voltaris.Models;

public sealed record BatteryStaticInfo(
    string Name,
    string Manufacturer,
    string SerialNumber,
    string Chemistry,
    double? DesignCapacityMwh,
    double? FullChargeCapacityMwh,
    double? DesignVoltageMv,
    int? FirmwareCycleCount)
{
    public double? DesignCapacityMah => ToMah(DesignCapacityMwh, DesignVoltageMv);
    public double? FullChargeCapacityMah => ToMah(FullChargeCapacityMwh, DesignVoltageMv);
    public double? HealthPercent => DesignCapacityMwh > 0 && FullChargeCapacityMwh > 0
        ? FullChargeCapacityMwh / DesignCapacityMwh * 100d
        : null;

    private static double? ToMah(double? energyMwh, double? voltageMv) =>
        energyMwh > 0 && voltageMv > 0 ? energyMwh * 1000d / voltageMv : null;
}

public sealed record BatteryReading(
    DateTimeOffset Timestamp,
    bool IsPresent,
    bool IsPowerOnline,
    bool IsCharging,
    bool IsDischarging,
    bool IsCritical,
    double Percent,
    double? RemainingCapacityMwh,
    double? VoltageMv,
    double? RateMw,
    double? CurrentMa,
    double? TemperatureC)
{
    public string StateText => !IsPresent ? "Батарея не найдена"
        : IsCharging ? "Идёт зарядка"
        : IsDischarging ? "Работа от батареи"
        : IsPowerOnline ? "Подключено к сети"
        : "Ожидание";
}

public sealed class CapacityMeasurement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public double StartPercent { get; set; }
    public double EndPercent { get; set; }
    public double ChargedMah { get; set; }
    public double ChargedMwh { get; set; }
    public double MeasuredCapacityMah { get; set; }
    public double MeasuredCapacityMwh { get; set; }
    public double? DesignCapacityMah { get; set; }
    public double? DesignCapacityMwh { get; set; }
    public double AverageVoltageMv { get; set; }
    public double AverageChargeRateMw { get; set; }
    public double PeakChargeRateMw { get; set; }
    public int SampleCount { get; set; }
    public int? FirmwareCycleCount { get; set; }
    public bool IsPartial { get; set; }
    public string Accuracy { get; set; } = "Низкая";
    public string MeasurementMethod { get; set; } = "Интеграл тока";
    public double DurationMinutes => (CompletedAt - StartedAt).TotalMinutes;
    public double CoveredPercent => Math.Max(0, EndPercent - StartPercent);
    public double? HealthPercent => DesignCapacityMah > 0 ? MeasuredCapacityMah / DesignCapacityMah * 100d : null;
}

public sealed class PersistedState
{
    public List<CapacityMeasurement> Measurements { get; set; } = [];
    public double ObservedDischargePercent { get; set; }
    public double ObservedEquivalentCycles => ObservedDischargePercent / 100d;
}

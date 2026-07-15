using Voltaris.Models;

namespace Voltaris.Services;

public sealed class CapacityTestEngine
{
    private BatteryStaticInfo? _staticInfo;
    private BatteryReading? _previous;
    private DateTimeOffset _startedAt;
    private double _startPercent;
    private double _endPercent;
    private double _chargedMah;
    private double _chargedMwh;
    private double _voltageHours;
    private double _rateHours;
    private double _activeHours;
    private double _peakRate;
    private int _fullCalmSamples;
    private double? _initialRemainingMwh;
    private double? _latestRemainingMwh;

    public bool IsArmed { get; private set; }
    public bool IsRunning { get; private set; }
    public int SampleCount { get; private set; }
    public double CoveredPercent => Math.Max(0, _endPercent - _startPercent);
    public double ChargedMah => _chargedMah;
    public bool CanFinish => IsRunning && CoveredPercent >= 5 && (_chargedMah > 0 || GaugeDeltaMwh > 0);
    public bool ShouldAutoComplete => CanFinish && _fullCalmSamples >= 30;
    private double GaugeDeltaMwh => _initialRemainingMwh is { } first && _latestRemainingMwh is { } last
        ? Math.Max(0, last - first)
        : 0;

    public void Arm(BatteryStaticInfo? staticInfo)
    {
        Reset();
        _staticInfo = staticInfo;
        IsArmed = true;
    }

    public void Add(BatteryReading reading)
    {
        if (!IsArmed || !reading.IsPresent) return;

        if (!IsRunning)
        {
            if (!reading.IsCharging || (reading.RateMw is not > 0 && reading.RemainingCapacityMwh is null)) return;
            IsRunning = true;
            _startedAt = reading.Timestamp;
            _startPercent = reading.Percent;
            _endPercent = reading.Percent;
            _previous = reading;
            _initialRemainingMwh = reading.RemainingCapacityMwh;
            _latestRemainingMwh = reading.RemainingCapacityMwh;
            SampleCount = 1;
            return;
        }

        var previous = _previous;
        _previous = reading;
        _endPercent = Math.Max(_endPercent, reading.Percent);
        _latestRemainingMwh = reading.RemainingCapacityMwh ?? _latestRemainingMwh;
        SampleCount++;
        if (previous is null) return;

        var hours = (reading.Timestamp - previous.Timestamp).TotalHours;
        if (hours <= 0 || hours > 5d / 3600d) return;

        var currentA = Math.Max(0, previous.CurrentMa ?? 0);
        var currentB = Math.Max(0, reading.CurrentMa ?? 0);
        var rateA = Math.Max(0, previous.RateMw ?? 0);
        var rateB = Math.Max(0, reading.RateMw ?? 0);
        var voltageA = previous.VoltageMv ?? reading.VoltageMv ?? 0;
        var voltageB = reading.VoltageMv ?? previous.VoltageMv ?? 0;
        var avgCurrent = (currentA + currentB) / 2d;
        var avgRate = (rateA + rateB) / 2d;
        var avgVoltage = (voltageA + voltageB) / 2d;
        if (previous.IsCharging || reading.IsCharging)
        {
            _voltageHours += avgVoltage * hours;
            _activeHours += hours;
            if (avgCurrent > 0) _chargedMah += avgCurrent * hours;
            if (avgRate > 0)
            {
                _chargedMwh += avgRate * hours;
                _rateHours += avgRate * hours;
                _peakRate = Math.Max(_peakRate, Math.Max(rateA, rateB));
            }
        }

        if (reading.Percent >= 99 && (!reading.IsCharging || reading.RateMw is < 3000))
            _fullCalmSamples++;
        else
            _fullCalmSamples = 0;
    }

    public CapacityMeasurement? Finish(DateTimeOffset completedAt)
    {
        if (!CanFinish) return null;
        var covered = CoveredPercent;
        var averageVoltage = _activeHours > 0 ? _voltageHours / _activeHours : _staticInfo?.DesignVoltageMv ?? 0;
        var gaugeFallback = _chargedMah <= 0;
        var chargedMwh = _chargedMwh > 0 ? _chargedMwh : GaugeDeltaMwh;
        var chargedMah = _chargedMah > 0 ? _chargedMah : averageVoltage > 0 ? chargedMwh * 1000d / averageVoltage : 0;
        if (chargedMah <= 0) return null;
        var result = new CapacityMeasurement
        {
            StartedAt = _startedAt,
            CompletedAt = completedAt,
            StartPercent = _startPercent,
            EndPercent = _endPercent,
            ChargedMah = chargedMah,
            ChargedMwh = chargedMwh,
            MeasuredCapacityMah = chargedMah * 100d / covered,
            MeasuredCapacityMwh = chargedMwh * 100d / covered,
            DesignCapacityMah = _staticInfo?.DesignCapacityMah,
            DesignCapacityMwh = _staticInfo?.DesignCapacityMwh,
            AverageVoltageMv = averageVoltage,
            AverageChargeRateMw = _activeHours > 0 ? (_rateHours > 0 ? _rateHours : chargedMwh) / _activeHours : 0,
            PeakChargeRateMw = _peakRate,
            SampleCount = SampleCount,
            FirmwareCycleCount = _staticInfo?.FirmwareCycleCount,
            IsPartial = _endPercent < 99,
            Accuracy = gaugeFallback
                ? covered >= 80 ? "Хорошая" : covered >= 50 ? "Оценочная" : "Низкая"
                : covered >= 80 ? "Высокая" : covered >= 50 ? "Хорошая" : covered >= 20 ? "Оценочная" : "Низкая",
            MeasurementMethod = gaugeFallback ? "Изменение энергии ACPI" : "Интеграл тока"
        };
        Reset();
        return result;
    }

    public void Cancel() => Reset();

    private void Reset()
    {
        IsArmed = false;
        IsRunning = false;
        _previous = null;
        _startedAt = default;
        _startPercent = 0;
        _endPercent = 0;
        _chargedMah = 0;
        _chargedMwh = 0;
        _voltageHours = 0;
        _rateHours = 0;
        _activeHours = 0;
        _peakRate = 0;
        _fullCalmSamples = 0;
        _initialRemainingMwh = null;
        _latestRemainingMwh = null;
        SampleCount = 0;
    }
}

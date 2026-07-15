using Voltaris.Models;
using Voltaris.Services;

var engine = new CapacityTestEngine();
var info = new BatteryStaticInfo("Test battery", "Voltaris", "1", "LION", 15_000, 15_000, 15_000, 10);
var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
engine.Arm(info);

for (var second = 0; second <= 3600; second++)
{
    var percent = second / 36d;
    engine.Add(new BatteryReading(
        start.AddSeconds(second), true, true, true, false, false,
        percent, percent * 150, 15_000, 15_000, 1_000, 30));
}

var result = engine.Finish(start.AddHours(1)) ?? throw new Exception("Measurement did not finish.");
if (Math.Abs(result.MeasuredCapacityMah - 1000) > 0.5)
    throw new Exception($"Expected 1000 mAh, got {result.MeasuredCapacityMah:F3} mAh.");
if (result.Accuracy != "Высокая")
    throw new Exception($"Expected high accuracy, got {result.Accuracy}.");

Console.WriteLine($"Capacity integration OK: {result.MeasuredCapacityMah:F2} mAh, {result.SampleCount} samples.");

var fallback = new CapacityTestEngine();
fallback.Arm(info);
for (var second = 0; second <= 3600; second++)
{
    var percent = second / 36d;
    fallback.Add(new BatteryReading(
        start.AddSeconds(second), true, true, true, false, false,
        percent, percent * 150, 15_000, null, null, null));
}

var fallbackResult = fallback.Finish(start.AddHours(1)) ?? throw new Exception("Fallback measurement did not finish.");
if (Math.Abs(fallbackResult.MeasuredCapacityMah - 1000) > 0.5 || fallbackResult.MeasurementMethod != "Изменение энергии ACPI")
    throw new Exception($"ACPI fallback failed: {fallbackResult.MeasuredCapacityMah:F3} mAh, {fallbackResult.MeasurementMethod}.");
Console.WriteLine($"ACPI energy fallback OK: {fallbackResult.MeasuredCapacityMah:F2} mAh.");

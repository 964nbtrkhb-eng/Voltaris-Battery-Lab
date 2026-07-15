using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Xml.Linq;
using Voltaris.Models;

namespace Voltaris.Services;

public sealed class BatteryTelemetryService
{
    private const uint Unknown = uint.MaxValue;

    public async Task<BatteryStaticInfo> ReadStaticInfoAsync(CancellationToken cancellationToken = default)
    {
        var report = await ReadPowerCfgReportAsync(cancellationToken);
        var wmi = await Task.Run(ReadWin32Fallback, cancellationToken);

        return new BatteryStaticInfo(
            report.Name ?? wmi.Name ?? "Батарея ноутбука",
            report.Manufacturer ?? "Не сообщается",
            report.SerialNumber ?? "Не сообщается",
            report.Chemistry ?? "Не сообщается",
            report.DesignCapacityMwh,
            report.FullChargeCapacityMwh ?? ReadFullChargeCapacity(),
            wmi.DesignVoltageMv,
            report.CycleCount is > 0 ? report.CycleCount : ReadCycleCount());
    }

    public Task<BatteryReading> ReadAsync(BatteryStaticInfo? staticInfo, CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadCore(staticInfo), cancellationToken);

    private static BatteryReading ReadCore(BatteryStaticInfo? staticInfo)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\root\wmi"),
                new ObjectQuery("SELECT Active, ChargeRate, Charging, Critical, DischargeRate, Discharging, PowerOnline, RemainingCapacity, Voltage FROM BatteryStatus WHERE Active=True"));
            using var results = searcher.Get();
            var status = results.Cast<ManagementObject>().FirstOrDefault();
            if (status is null)
                return MissingReading();

            var remaining = Number(status["RemainingCapacity"]);
            var voltage = Number(status["Voltage"]);
            var charging = Flag(status["Charging"]);
            var discharging = Flag(status["Discharging"]);
            var chargeRate = Number(status["ChargeRate"]);
            var dischargeRate = Number(status["DischargeRate"]);
            var signedRate = charging ? chargeRate : discharging ? -dischargeRate : 0d;
            var full = staticInfo?.FullChargeCapacityMwh;
            var percent = remaining is not null && full > 0
                ? Math.Clamp(remaining.Value / full.Value * 100d, 0, 100)
                : ReadPercentFallback();
            double? current = voltage > 0 && signedRate is not null
                ? signedRate.Value * 1000d / voltage.Value
                : null;

            return new BatteryReading(
                DateTimeOffset.Now,
                true,
                Flag(status["PowerOnline"]),
                charging,
                discharging,
                Flag(status["Critical"]),
                percent,
                remaining,
                voltage,
                signedRate,
                current,
                ReadTemperature());
        }
        catch
        {
            return MissingReading();
        }
    }

    private static BatteryReading MissingReading() =>
        new(DateTimeOffset.Now, false, false, false, false, false, 0, null, null, null, null, null);

    private static double ReadPercentFallback()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining FROM Win32_Battery");
            using var results = searcher.Get();
            return Number(results.Cast<ManagementObject>().FirstOrDefault()?["EstimatedChargeRemaining"]) ?? 0;
        }
        catch { return 0; }
    }

    private static double? ReadTemperature()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\root\wmi"),
                new ObjectQuery("SELECT Temperature FROM BatteryTemperature"));
            using var results = searcher.Get();
            var raw = Number(results.Cast<ManagementObject>().FirstOrDefault()?["Temperature"]);
            if (raw is null or <= 0) return null;
            var celsius = raw.Value / 10d - 273.15d;
            return celsius is > -30 and < 120 ? celsius : null;
        }
        catch { return null; }
    }

    private static double? ReadFullChargeCapacity()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\root\wmi"),
                new ObjectQuery("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity WHERE Active=True"));
            using var results = searcher.Get();
            return Number(results.Cast<ManagementObject>().FirstOrDefault()?["FullChargedCapacity"]);
        }
        catch { return null; }
    }

    private static int? ReadCycleCount()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\root\wmi"),
                new ObjectQuery("SELECT CycleCount FROM BatteryCycleCount WHERE Active=True"));
            using var results = searcher.Get();
            var value = Number(results.Cast<ManagementObject>().FirstOrDefault()?["CycleCount"]);
            return value is > 0 and < int.MaxValue ? (int)value.Value : null;
        }
        catch { return null; }
    }

    private static (string? Name, double? DesignVoltageMv) ReadWin32Fallback()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DesignVoltage FROM Win32_Battery");
            using var results = searcher.Get();
            var battery = results.Cast<ManagementObject>().FirstOrDefault();
            return (battery?["Name"]?.ToString(), Number(battery?["DesignVoltage"]));
        }
        catch { return (null, null); }
    }

    private static async Task<ReportBattery> ReadPowerCfgReportAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"voltaris-{Guid.NewGuid():N}.xml");
        try
        {
            var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("/batteryreport");
            startInfo.ArgumentList.Add("/xml");
            startInfo.ArgumentList.Add("/duration");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("/output");
            startInfo.ArgumentList.Add(path);
            using var process = Process.Start(startInfo);
            if (process is null) return new();
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0 || !File.Exists(path)) return new();

            var document = XDocument.Load(path);
            var battery = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "Battery");
            if (battery is null) return new();
            string? Text(string name) => battery.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim();
            double? Double(string name) => double.TryParse(Text(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
            int? Int(string name) => int.TryParse(Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

            return new ReportBattery
            {
                Name = Text("Id"),
                Manufacturer = Text("Manufacturer"),
                SerialNumber = Text("SerialNumber"),
                Chemistry = Text("Chemistry"),
                DesignCapacityMwh = Double("DesignCapacity"),
                FullChargeCapacityMwh = Double("FullChargeCapacity"),
                CycleCount = Int("CycleCount")
            };
        }
        catch { return new(); }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static bool Flag(object? value) => value is bool flag && flag;

    private static double? Number(object? value)
    {
        if (value is null) return null;
        try
        {
            var number = Convert.ToUInt32(value, CultureInfo.InvariantCulture);
            return number == Unknown ? null : number;
        }
        catch { return null; }
    }

    private sealed class ReportBattery
    {
        public string? Name { get; init; }
        public string? Manufacturer { get; init; }
        public string? SerialNumber { get; init; }
        public string? Chemistry { get; init; }
        public double? DesignCapacityMwh { get; init; }
        public double? FullChargeCapacityMwh { get; init; }
        public int? CycleCount { get; init; }
    }
}

using Devicer.Core.Models;
using Devicer.Core.Services;

// Devicer.Smoke — exercises Devicer.Core against whatever device is currently connected,
// independently of the WPF shell. Run from repo root: `dotnet run --project tools/Devicer.Smoke`.

var shell = new ShellRunner();
var adb = new AdbService(shell);
var fastboot = new FastbootService(shell);
var probe = new DeviceProbeService(adb, fastboot);

Console.WriteLine($"adb available:      {await adb.IsAvailableAsync()}");
Console.WriteLine($"fastboot available: {await fastboot.IsAvailableAsync()}");
Console.WriteLine();

var result = await probe.ProbeAsync();
if (result.Diagnostic is { } d)
{
    Console.WriteLine($"[!] {d}");
    Console.WriteLine();
}

Console.WriteLine($"=== {result.Devices.Count} device(s) ===");
foreach (var dev in result.Devices)
{
    Console.WriteLine();
    Console.WriteLine($"Serial          : {dev.Serial}");
    Console.WriteLine($"State           : {dev.ConnectionState}");
    Console.WriteLine($"Display         : {dev.DisplayName}");
    Console.WriteLine($"Manufacturer    : {dev.Manufacturer}");
    Console.WriteLine($"Brand           : {dev.Brand}");
    Console.WriteLine($"Model           : {dev.Model}");
    Console.WriteLine($"Codename        : {dev.Codename}");
    Console.WriteLine($"Android         : {dev.AndroidVersion} (SDK {dev.AndroidSdk})");
    Console.WriteLine($"Build FP        : {dev.BuildFingerprint}");
    Console.WriteLine($"Security Patch  : {dev.SecurityPatch}");
    Console.WriteLine($"CSC             : {dev.Csc} / {dev.CscCountry}");
    Console.WriteLine($"Bootloader      : {dev.BootloaderVersion}");
    Console.WriteLine($"Baseband        : {dev.BasebandVersion}");
    Console.WriteLine($"Slot            : {dev.CurrentSlot} (A/B: {dev.IsAbDevice})");
    Console.WriteLine($"Encryption      : {dev.EncryptionState}");
    Console.WriteLine($"OEM Unlock      : {dev.OemUnlockSupported}");
    Console.WriteLine($"Knox bit        : {dev.KnoxWarrantyBit}");
    Console.WriteLine($"Samsung         : {dev.IsSamsung}");
    Console.WriteLine($"Root            : {dev.Root.Kind} {dev.Root.Version}");

    if (dev.IsSamsung && !string.IsNullOrWhiteSpace(dev.Model) && !string.IsNullOrWhiteSpace(dev.Csc))
    {
        Console.WriteLine();
        Console.WriteLine($"--- Samsung OTA latest for {dev.Model} / {dev.Csc} ---");
        using var fw = new FirmwareCheckService();
        try
        {
            var latest = await fw.GetLatestAsync(dev.Model, dev.Csc);
            if (latest is null)
            {
                Console.WriteLine("(no firmware feed returned)");
            }
            else
            {
                Console.WriteLine($"Latest PDA      : {latest.Latest.Pda}");
                Console.WriteLine($"Latest CSC      : {latest.Latest.Csc}");
                Console.WriteLine($"Latest CP       : {latest.Latest.Cp}");
                Console.WriteLine($"History count   : {latest.UpgradeHistory.Count}");
                var installedPda = dev.SamsungPda ?? dev.BuildId;
                if (!string.IsNullOrWhiteSpace(installedPda))
                {
                    var diff = FirmwareVersion.ComparePda(latest.Latest.Pda, installedPda);
                    var status = diff > 0 ? "BEHIND (update available)" : diff < 0 ? "ahead (rare)" : "current";
                    Console.WriteLine($"You are         : {status}  (installed PDA: {installedPda})");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OTA query failed: {ex.Message}");
        }
    }
}

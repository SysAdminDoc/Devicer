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
}

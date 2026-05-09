using Devicer.Core.Models;
using Devicer.Core.Services;

// Devicer.Smoke — exercises Devicer.Core against whatever device is currently connected,
// independently of the WPF shell. Run from repo root: `dotnet run --project tools/Devicer.Smoke`.
//
// Flags:
//   --inform                       Probe + run a FUS BinaryInform on the connected Samsung's latest firmware.
//                                  Verifies auth crypto end-to-end without downloading the blob.
//   --inform <model> <csc> <pda>   Same, but with explicit model/CSC/PDA — useful when no device is connected.

if (args.Length > 0 && args[0] == "--inform" && args.Length >= 5)
{
    await InformOnlyAsync(args[1], args[2], args[3], args[4]);
    return;
}

if (args.Length > 0 && args[0] == "--crypto-self-test")
{
    Devicer.Smoke.CryptoSelfTest.RoundTrip();
    return;
}

if (args.Length >= 2 && args[0] == "--roms")
{
    var codename = args[1];
    using var agg = new RomAggregatorService();
    var romResult = await agg.SearchAsync(codename);
    Console.WriteLine($"Codename : {codename}");
    Console.WriteLine($"Sources  : queried {romResult.SourcesQueried.Count}, with-results {romResult.SourcesWithResults.Count}");
    Console.WriteLine($"Builds   : {romResult.Entries.Count}");
    foreach (var e in romResult.Entries.Take(10))
    {
        Console.WriteLine();
        Console.WriteLine($"  [{e.SourceDisplay} {e.KindDisplay}] {e.Version}  {e.SizeDisplay}  ({e.BuildDate:yyyy-MM-dd})");
        Console.WriteLine($"    {e.FileName}");
        Console.WriteLine($"    {e.DownloadUrl}");
        if (!string.IsNullOrEmpty(e.Sha256)) Console.WriteLine($"    sha256: {e.Sha256}");
    }
    if (romResult.Entries.Count > 10) Console.WriteLine($"  …and {romResult.Entries.Count - 10} more.");
    return;
}

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
    Console.WriteLine($"IMEI            : {(string.IsNullOrWhiteSpace(dev.Imei) ? "(not available)" : dev.Imei)}");

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

                if (args.Contains("--inform"))
                {
                    Console.WriteLine();
                    if (string.IsNullOrWhiteSpace(dev.Imei))
                    {
                        Console.WriteLine("--- FUS BinaryInform: SKIPPED (no IMEI — Samsung requires a real one as of late 2024) ---");
                    }
                    else
                    {
                        Console.WriteLine("--- FUS BinaryInform (auth + metadata only, no download) ---");
                        await InformOnlyAsync(dev.Model, dev.Csc, latest.Latest.Normalized, dev.Imei);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OTA query failed: {ex.Message}");
        }
    }
}

static async Task InformOnlyAsync(string model, string csc, string version, string imei)
{
    using var svc = new FirmwareDownloadService();
    try
    {
        var info = await svc.GetBinaryInfoAsync(model, csc, version, imei);
        Console.WriteLine($"Binary name     : {info.BinaryName}");
        Console.WriteLine($"Binary size     : {info.BinaryByteSize} bytes ({info.BinaryByteSize / (1024.0 * 1024 * 1024):0.00} GB)");
        Console.WriteLine($"V4 (.enc4)      : {info.IsV4}");
        Console.WriteLine($"Latest FW ver   : {info.LatestFwVersion}");
        Console.WriteLine($"Logic factory   : {(string.IsNullOrEmpty(info.LogicValueFactory) ? "(none)" : info.LogicValueFactory[..Math.Min(8, info.LogicValueFactory.Length)] + "…")}");
    }
    catch (FusProtocolException ex)
    {
        Console.WriteLine($"FUS error: {ex.Message}");
        if (!string.IsNullOrWhiteSpace(ex.ResponseBody))
            Console.WriteLine($"Body: {ex.ResponseBody[..Math.Min(400, ex.ResponseBody.Length)]}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"BinaryInform failed: {ex.Message}");
    }
}

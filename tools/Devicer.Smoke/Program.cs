using Devicer.Core.Models;
using Devicer.Core.Services;

// Devicer.Smoke — exercises Devicer.Core against whatever device is currently connected,
// independently of the WPF shell. Run from repo root: `dotnet run --project tools/Devicer.Smoke`.
//
// Flags:
//   --inform                       Probe + run a FUS BinaryInform on the connected Samsung's latest firmware.
//                                  Verifies auth crypto end-to-end without downloading the blob.
//   --inform <model> <csc> <pda>   Same, but with explicit model/CSC/PDA — useful when no device is connected.
//   --firmware-regions <model> <csc...>
//                                  Query several Samsung CSC feeds for one model.

if (args.Length > 0 && args[0] == "--inform" && args.Length >= 5)
{
    await InformOnlyAsync(args[1], args[2], args[3], args[4]);
    return;
}

if (args.Length >= 5 && args[0] == "--download-headers")
{
    // Probes the full FUS pipeline through the download-init step and prints the
    // exact URL we'd hit, including MODEL_PATH. Does NOT actually download bytes.
    await DownloadHeaderProbeAsync(args[1], args[2], args[3], args[4]);
    return;
}

if (args.Length >= 3 && args[0] == "--firmware-regions")
{
    await FirmwareRegionsAsync(args[1], args.Skip(2));
    return;
}

if (args.Length > 0 && args[0] == "--crypto-self-test")
{
    Devicer.Smoke.CryptoSelfTest.RoundTrip();
    return;
}

if (args.Length >= 3 && args[0] == "--flash-fastboot")
{
    var fbSerial = args[1];
    var entries = new List<FastbootFlashEntry>();
    for (int i = 2; i < args.Length; i++)
    {
        var eq = args[i].IndexOf('=');
        if (eq <= 0) { Console.WriteLine($"Invalid format: '{args[i]}'. Expected partition=image.img"); return; }
        entries.Add(new FastbootFlashEntry(args[i][..eq], args[i][(eq + 1)..]));
    }
    var fbShell = new ShellRunner();
    var fb = new FastbootService(fbShell);
    var fbFlash = new FastbootFlashService(fb);
    Console.WriteLine($"Batch fastboot flash: {entries.Count} partition(s) on {fbSerial}");
    if (args.Contains("--dry-run"))
    {
        var plan = await fbFlash.GeneratePlanAsync(fbSerial, entries, null, false);
        Console.WriteLine(plan);
        return;
    }
    var progress = new Progress<FastbootFlashProgress>(p => Console.WriteLine($"  [{p.Phase}] {p.Message}"));
    var fbResult = await fbFlash.FlashAsync(fbSerial, entries, null, false, progress);
    Console.WriteLine($"Result: {fbResult.SucceededPartitions}/{fbResult.TotalPartitions} succeeded");
    foreach (var w in fbResult.WarningMessages) Console.WriteLine($"  WARN: {w}");
    Environment.ExitCode = fbResult.FailedPartitions.Count > 0 ? 1 : 0;
    return;
}

if (args.Length >= 2 && args[0] == "--flash-thor")
{
    var tarPath = args[1];
    var efsClear = args.Contains("--efs-clear");
    var toolShell = new ShellRunner();
    var toolMgr = new ToolManager();
    var thor = new ThorService(toolShell, toolMgr);
    if (!thor.IsAvailable) { Console.WriteLine("Thor not found on PATH or in tools cache."); Environment.ExitCode = 1; return; }
    Console.WriteLine($"Thor flash: {tarPath} (EFS-Clear: {efsClear})");
    var progress = new Progress<ThorFlashProgress>(p => Console.WriteLine($"  [{p.Phase}] {p.Message}"));
    var thorResult = await thor.FlashArchiveAsync(tarPath, null, efsClear, progress);
    Console.WriteLine(thorResult.Success ? "Flash succeeded." : $"Flash failed. {thorResult.Warnings.Count} warning(s).");
    foreach (var w in thorResult.Warnings) Console.WriteLine($"  WARN: {w}");
    Environment.ExitCode = thorResult.Success ? 0 : 1;
    return;
}

if (args.Length >= 2 && args[0] == "--backup")
{
    var bkSerial = args[1];
    var bkShell = new ShellRunner();
    var bkAdb = new AdbService(bkShell);
    var bkSvc = new BackupService(bkAdb);
    Console.WriteLine($"Listing partitions on {bkSerial}…");
    var parts = await bkAdb.ListPartitionsAsync(bkSerial);
    var critical = parts.Where(p => p.IsCritical).ToList();
    Console.WriteLine($"Found {parts.Count} partitions, {critical.Count} critical.");
    Console.WriteLine("Backing up critical partitions…");
    var progress = new Progress<BackupProgress>(p => Console.WriteLine($"  [{p.Phase}] {p.Message}"));
    var bkResult = await bkSvc.RunAsync(bkSerial, null, critical, progress);
    Console.WriteLine($"Saved {bkResult.Manifest.Partitions.Count}/{critical.Count} to {bkResult.FolderPath}");
    foreach (var w in bkResult.WarningMessages) Console.WriteLine($"  WARN: {w}");
    return;
}

if (args.Length >= 2 && args[0] == "--partitions")
{
    var serialArg = args[1];
    var ashell = new ShellRunner();
    var aadb = new AdbService(ashell);
    var parts = await aadb.ListPartitionsAsync(serialArg);
    Console.WriteLine($"{parts.Count} partition(s):");
    foreach (var p in parts.Take(60))
        Console.WriteLine($"  {(p.IsCritical ? "[CRIT]" : "      ")} {p.Name,-20} {p.SizeDisplay,12}  {p.BlockPath}");
    if (parts.Count > 60) Console.WriteLine($"  …and {parts.Count - 60} more.");
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

static async Task FirmwareRegionsAsync(string model, IEnumerable<string> regions)
{
    using var fw = new FirmwareCheckService();
    var results = await fw.GetLatestAcrossRegionsAsync(model, regions);
    Console.WriteLine($"Model  : {model}");
    Console.WriteLine($"Regions: {results.Count}");
    foreach (var r in results)
    {
        Console.WriteLine();
        Console.WriteLine($"[{r.Csc}]");
        if (r.Firmware is null)
        {
            Console.WriteLine(string.IsNullOrWhiteSpace(r.Error) ? "  no firmware feed" : $"  error: {r.Error}");
            continue;
        }

        Console.WriteLine($"  latest : {r.Firmware.Latest.Raw}");
        Console.WriteLine($"  history: {r.Firmware.UpgradeHistory.Count}");
    }
}

static async Task DownloadHeaderProbeAsync(string model, string csc, string version, string imei)
{
    using var fus = new FusClient();
    using var svc = new FirmwareDownloadService(fus);
    try
    {
        Console.WriteLine($"--- BinaryInform ---");
        var info = await svc.GetBinaryInfoAsync(model, csc, version, imei);
        Console.WriteLine($"BinaryName    : {info.BinaryName}");
        Console.WriteLine($"BinaryByteSize: {info.BinaryByteSize}");
        Console.WriteLine($"ModelPath     : '{info.ModelPath}' (len {info.ModelPath?.Length ?? 0})");
        Console.WriteLine($"LatestFwVer   : {info.LatestFwVersion}");
        Console.WriteLine($"LogicValueFac : '{(string.IsNullOrEmpty(info.LogicValueFactory) ? "(none)" : info.LogicValueFactory)}'");

        Console.WriteLine($"--- BinaryInitForMass + first 64 bytes via Range header ---");
        // Reach in via a tiny range request to confirm the cloud-host URL works without burning bandwidth.
        var remotePath = (info.ModelPath ?? string.Empty) + info.BinaryName;
        Console.WriteLine($"Remote path   : '{remotePath}'");
        // The fwv-slice algorithm is `binary_filename.split('.')[0][-16:]` — split on FIRST
        // dot, take last 16 of the leading chunk. The earlier `LastIndexOf` formulation was
        // buggy: for any filename with multiple dots (every modern .zip.enc4 name) it
        // included `.zip` in the slice and on short stems it threw ArgumentOutOfRange.
        var fwvSlice = FirmwareDownloadService.ExtractFwvSlice(info.BinaryName);
        var initXml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><FUSMsg><FUSHdr><ProtoVer>1.0</ProtoVer></FUSHdr><FUSBody><Put><BINARY_FILE_NAME><Data>{info.BinaryName}</Data></BINARY_FILE_NAME><LOGIC_CHECK><Data>{FusCrypto.GetLogicCheck(fwvSlice, fus.Nonce!)}</Data></LOGIC_CHECK></Put></FUSBody></FUSMsg>";
        var initResp = await fus.PostXmlAsync("/NF_DownloadBinaryInitForMass.do", initXml);
        Console.WriteLine($"Init response : {initResp.Substring(0, Math.Min(400, initResp.Length))}");

        // 1-byte Range probe of the actual download — succeeds without burning the full 8 GB.
        using var dlResp = await fus.StartDownloadAsync(remotePath, 0L);
        Console.WriteLine($"Download HTTP : {(int)dlResp.StatusCode} {dlResp.ReasonPhrase}");
        Console.WriteLine($"Content-Length: {dlResp.Content.Headers.ContentLength}");
        Console.WriteLine($"Content-Type  : {dlResp.Content.Headers.ContentType}");
    }
    catch (FusProtocolException ex)
    {
        Console.WriteLine($"FUS error: {ex.Message}");
        if (!string.IsNullOrEmpty(ex.ResponseBody)) Console.WriteLine($"Body: {ex.ResponseBody[..Math.Min(800, ex.ResponseBody.Length)]}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Probe failed: {ex.Message}");
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

using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Devicer.MarketingCapture;

internal static class Program
{
    private const uint DesktopAccess = 0x000F01FF;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint WaitTimeout = 0x00000102;

    private static readonly (string View, string FileName)[] Views =
    [
        ("device", "01-device.png"),
        ("firmware", "02-firmware.png"),
        ("roms", "03-roms.png"),
        ("backup", "04-backup.png"),
        ("flash", "05-flash-safety.png"),
        ("settings", "06-settings.png"),
    ];

    private static int Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            var executable = Path.GetFullPath(options["app"]);
            var outputDirectory = Path.GetFullPath(options["output"]);
            if (!File.Exists(executable))
                throw new FileNotFoundException("The Devicer executable was not found.", executable);

            Directory.CreateDirectory(outputDirectory);
            SetProcessDpiAwarenessContext(new IntPtr(-4));

            var captures = new List<CaptureResult>();
            foreach (var (view, fileName) in Views)
            {
                var outputPath = Path.Combine(outputDirectory, fileName);
                RunCapture(executable, outputDirectory, view);
                captures.Add(InspectCapture(view, outputPath));
            }

            var reportPath = Path.Combine(outputDirectory, "capture-report.json");
            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(
                    new
                    {
                        executable = Path.GetFileName(executable),
                        isolatedDesktop = true,
                        dpiAware = true,
                        screenshots = captures,
                    },
                    new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

            Console.WriteLine($"Captured {captures.Count} Devicer views on isolated desktops.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException("Usage: Devicer.MarketingCapture --app <exe> --output <directory>");
            options[args[index][2..]] = args[++index];
        }

        if (!options.ContainsKey("app") || !options.ContainsKey("output"))
            throw new ArgumentException("Usage: Devicer.MarketingCapture --app <exe> --output <directory>");
        return options;
    }

    private static void RunCapture(string executable, string outputDirectory, string view)
    {
        var desktopName = $"DevicerCapture-{Environment.ProcessId}-{view}-{Guid.NewGuid():N}";
        var desktop = CreateDesktop(desktopName, null, IntPtr.Zero, 0, DesktopAccess, IntPtr.Zero);
        if (desktop == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create an isolated desktop.");

        ProcessInformation processInfo = default;
        IntPtr environmentBlock = IntPtr.Zero;
        try
        {
            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                Desktop = $"winsta0\\{desktopName}",
            };
            var commandLine = new StringBuilder($"\"{executable}\"");
            environmentBlock = BuildEnvironmentBlock(outputDirectory, view);
            if (!CreateProcess(
                    executable,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateUnicodeEnvironment,
                    environmentBlock,
                    Path.GetDirectoryName(executable),
                    ref startup,
                    out processInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not start the {view} capture.");
            }

            CloseHandle(processInfo.Thread);
            processInfo.Thread = IntPtr.Zero;
            if (WaitForSingleObject(processInfo.Process, 45_000) == WaitTimeout)
            {
                TerminateProcess(processInfo.Process, 124);
                throw new TimeoutException($"The {view} capture timed out.");
            }
            if (!GetExitCodeProcess(processInfo.Process, out var exitCode))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the capture exit code.");
            if (exitCode != 0)
                throw new InvalidOperationException($"The {view} capture exited with code {exitCode}.");
        }
        finally
        {
            if (environmentBlock != IntPtr.Zero)
                Marshal.FreeHGlobal(environmentBlock);
            if (processInfo.Thread != IntPtr.Zero)
                CloseHandle(processInfo.Thread);
            if (processInfo.Process != IntPtr.Zero)
            {
                if (WaitForSingleObject(processInfo.Process, 0) == WaitTimeout)
                    TerminateProcess(processInfo.Process, 1);
                CloseHandle(processInfo.Process);
            }
            CloseDesktop(desktop);
        }
    }

    private static IntPtr BuildEnvironmentBlock(string outputDirectory, string view)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
                variables[key] = value;
        }
        variables["DEVICER_MARKETING_CAPTURE"] = "1";
        variables["DEVICER_MARKETING_OUTPUT"] = outputDirectory;
        variables["DEVICER_MARKETING_VIEW"] = view;
        variables["DOTNET_CLI_UI_LANGUAGE"] = "en-US";

        var block = string.Join('\0', variables.Select(pair => $"{pair.Key}={pair.Value}")) + "\0\0";
        return Marshal.StringToHGlobalUni(block);
    }

    private static CaptureResult InspectCapture(string view, string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"The {view} screenshot was not created.", path);
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 24 || !bytes.AsSpan(1, 3).SequenceEqual("PNG"u8))
            throw new InvalidDataException($"The {view} output is not a PNG file.");

        var width = ReadBigEndianInt32(bytes, 16);
        var height = ReadBigEndianInt32(bytes, 20);
        if (width < 1200 || height < 700 || bytes.Length < 20_000)
            throw new InvalidDataException($"The {view} screenshot looks incomplete ({width}x{height}, {bytes.Length} bytes).");

        return new CaptureResult(
            view,
            Path.GetFileName(path),
            width,
            height,
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private sealed record CaptureResult(string View, string File, int Width, int Height, int Bytes, string Sha256);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(
        string name,
        string? device,
        IntPtr deviceMode,
        uint flags,
        uint desiredAccess,
        IntPtr attributes);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}

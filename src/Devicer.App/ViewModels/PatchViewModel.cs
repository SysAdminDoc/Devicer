using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.Core.Models;
using Devicer.Core.Services;
using Microsoft.Win32;

namespace Devicer.App.ViewModels;

public partial class PatchViewModel : ObservableObject
{
    private readonly IBootPatchService _patcher;
    private readonly IPcPatchService _pcPatcher;
    private DeviceInfo? _device;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string? _serial;

    [ObservableProperty]
    private string? _model;

    [ObservableProperty]
    private string? _rootKindDisplay;

    [ObservableProperty]
    private string? _patchTargetHint;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(PcPatchCommand))]
    private bool _hasRoot;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(PcPatchCommand))]
    private string? _bootImagePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(PatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(PcPatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isPatching;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private string? _diagnostic;

    [ObservableProperty]
    private string? _outputPath;

    [ObservableProperty]
    private string? _outputSha256;

    public bool IsIdle => !IsPatching;
    public bool IsPcPatcherAvailable => _pcPatcher.IsAvailable;

    public PatchViewModel(IBootPatchService patcher, IPcPatchService pcPatcher)
    {
        _patcher = patcher;
        _pcPatcher = pcPatcher;
    }

    public void PrefillFrom(DeviceInfo? device)
    {
        _device = device;
        if (device is null)
        {
            Serial = null; Model = null; RootKindDisplay = null; HasRoot = false;
            return;
        }
        Serial = device.Serial;
        Model = device.Model;
        HasRoot = device.Root.Kind != RootKind.None;
        RootKindDisplay = device.Root.Kind == RootKind.None
            ? "No root manager detected. Use the PC-side patcher below if available."
            : $"{device.Root.Kind} {device.Root.Version}";
        PatchTargetHint = device.HasInitBoot
            ? "This device uses init_boot.img (Android 13+ GKI). Select init_boot.img, not boot.img."
            : "This device uses boot.img for root patching.";
    }

    [RelayCommand]
    public void BrowseBootImage()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select boot.img / init_boot.img",
            Filter = "Boot images (*.img)|*.img|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true) BootImagePath = dlg.FileName;
    }

    [RelayCommand(CanExecute = nameof(CanPatch))]
    public async Task PatchAsync()
    {
        if (string.IsNullOrWhiteSpace(Serial) || string.IsNullOrWhiteSpace(BootImagePath)) return;
        if (_device is null || _device.Root.Kind == RootKind.None) return;

        IsPatching = true;
        Diagnostic = null;
        OutputPath = null;
        OutputSha256 = null;
        StatusText = "Preparing…";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var progress = new Progress<PatchProgress>(p => StatusText = p.Message);

        try
        {
            var res = await _patcher.PatchBootImageAsync(Serial!, _device.Root, BootImagePath!, progress, ct).ConfigureAwait(true);
            OutputPath = res.OutputPath;
            OutputSha256 = res.Sha256;
            StatusText = $"Patched via {res.PatchedBy}. SHA256 captured. Output ready to flash.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            Diagnostic = $"Patch failed: {ex.Message}";
            StatusText = null;
        }
        finally
        {
            IsPatching = false;
        }
    }

    private bool CanPatch() => !IsPatching && HasRoot
        && !string.IsNullOrWhiteSpace(Serial)
        && !string.IsNullOrWhiteSpace(BootImagePath)
        && File.Exists(BootImagePath);

    [RelayCommand(CanExecute = nameof(CanPcPatch))]
    public async Task PcPatchAsync()
    {
        if (string.IsNullOrWhiteSpace(BootImagePath)) return;

        IsPatching = true;
        Diagnostic = null;
        OutputPath = null;
        OutputSha256 = null;
        StatusText = "Running PC-side patcher…";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var progress = new Progress<PatchProgress>(p => StatusText = p.Message);

        try
        {
            var res = await _pcPatcher.PatchBootImageAsync(BootImagePath!, progress, ct).ConfigureAwait(true);
            OutputPath = res.OutputPath;
            OutputSha256 = res.Sha256;
            StatusText = $"Patched via PC-side {res.PatchedBy}. SHA256 captured. Output ready to flash.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            Diagnostic = $"PC-side patch failed: {ex.Message}";
            StatusText = null;
        }
        finally
        {
            IsPatching = false;
        }
    }

    private bool CanPcPatch() => !IsPatching
        && !string.IsNullOrWhiteSpace(BootImagePath)
        && File.Exists(BootImagePath);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel() => _cts?.Cancel();

    private bool CanCancel() => IsPatching;

    [RelayCommand(CanExecute = nameof(HasOutput))]
    public void OpenOutputFolder()
    {
        if (string.IsNullOrWhiteSpace(OutputPath)) return;
        var folder = Path.GetDirectoryName(OutputPath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        try { Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true }); }
        catch (Exception ex) { Diagnostic = $"Could not open folder: {ex.Message}"; }
    }

    private bool HasOutput() => !string.IsNullOrWhiteSpace(OutputPath);

    partial void OnOutputPathChanged(string? value) => OpenOutputFolderCommand.NotifyCanExecuteChanged();
}

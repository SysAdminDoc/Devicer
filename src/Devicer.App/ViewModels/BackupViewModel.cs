using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.Core.Models;
using Devicer.Core.Services;

namespace Devicer.App.ViewModels;

public partial class PartitionRow : ObservableObject
{
    [ObservableProperty]
    private bool _selected;

    public required PartitionInfo Info { get; init; }
}

public partial class BackupViewModel : ObservableObject
{
    private readonly IAdbService _adb;
    private readonly IBackupService _backup;

    private DeviceInfo? _device;
    private CancellationTokenSource? _runCts;

    public ObservableCollection<PartitionRow> Partitions { get; } = new();
    public ObservableCollection<string> Warnings { get; } = new();

    [ObservableProperty]
    private string? _serial;

    [ObservableProperty]
    private string? _model;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(LoadPartitionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelBackupCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(LoadPartitionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelBackupCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string? _diagnostic;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private double? _progressFraction;

    [ObservableProperty]
    private string? _lastBackupFolder;

    public bool IsIdle => !IsLoading && !IsRunning;

    public BackupViewModel(IAdbService adb, IBackupService backup)
    {
        _adb = adb;
        _backup = backup;
    }

    public void PrefillFrom(DeviceInfo? device)
    {
        _device = device;
        if (device is null)
        {
            Serial = null; Model = null; Partitions.Clear();
            return;
        }
        Serial = device.Serial;
        Model = device.Model;
    }

    [RelayCommand(CanExecute = nameof(CanLoadPartitions))]
    public async Task LoadPartitionsAsync()
    {
        if (string.IsNullOrWhiteSpace(Serial)) return;
        IsLoading = true;
        Diagnostic = null;
        StatusText = "Listing partitions via root…";
        Partitions.Clear();
        try
        {
            var parts = await _adb.ListPartitionsAsync(Serial!).ConfigureAwait(true);
            if (parts.Count == 0)
            {
                Diagnostic = "Could not list /dev/block/by-name. Root is required (Magisk / KernelSU / APatch). On a Samsung phone, EFS sits behind root — there's no shell-readable path.";
                StatusText = null;
                return;
            }
            foreach (var p in parts)
                Partitions.Add(new PartitionRow { Info = p, Selected = p.IsCritical });
            StatusText = $"Loaded {Partitions.Count} partitions. {Partitions.Count(r => r.Info.IsCritical)} critical pre-selected.";
        }
        catch (Exception ex)
        {
            Diagnostic = $"Could not list partitions: {ex.Message}";
            StatusText = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanLoadPartitions() => !IsLoading && !IsRunning && !string.IsNullOrWhiteSpace(Serial);

    [RelayCommand(CanExecute = nameof(CanStart))]
    public async Task StartBackupAsync()
    {
        if (string.IsNullOrWhiteSpace(Serial)) return;
        var selected = Partitions.Where(r => r.Selected).Select(r => r.Info).ToList();
        if (selected.Count == 0)
        {
            Diagnostic = "Select at least one partition to back up.";
            return;
        }

        IsRunning = true;
        Diagnostic = null;
        Warnings.Clear();
        ProgressFraction = null;
        StatusText = "Starting…";
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        var progress = new Progress<BackupProgress>(p =>
        {
            StatusText = p.Message ?? $"{p.Phase} {p.PartitionName}";
            // Coarse fraction: completed partitions / total.
            ProgressFraction = p.PartitionCount > 0
                ? Math.Clamp(p.PartitionIndex / (double)p.PartitionCount, 0, 1)
                : null;
        });

        try
        {
            var result = await _backup.RunAsync(Serial!, _device, selected, progress, ct).ConfigureAwait(true);
            LastBackupFolder = result.FolderPath;
            foreach (var w in result.WarningMessages) Warnings.Add(w);
            StatusText = $"Saved {result.Manifest.Partitions.Count}/{selected.Count} partitions to {result.FolderPath}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            Diagnostic = $"Backup failed: {ex.Message}";
            StatusText = null;
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanStart() => !IsRunning && !IsLoading && !string.IsNullOrWhiteSpace(Serial);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void CancelBackup() => _runCts?.Cancel();

    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(HasFolder))]
    public void OpenLastFolder()
    {
        if (string.IsNullOrWhiteSpace(LastBackupFolder) || !Directory.Exists(LastBackupFolder)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = LastBackupFolder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Diagnostic = $"Could not open folder: {ex.Message}";
        }
    }

    private bool HasFolder() => !string.IsNullOrWhiteSpace(LastBackupFolder);

    partial void OnLastBackupFolderChanged(string? value) => OpenLastFolderCommand.NotifyCanExecuteChanged();
}

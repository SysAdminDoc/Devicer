using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.Core.Models;
using Devicer.Core.Services;

namespace Devicer.App.ViewModels;

public partial class FirmwareViewModel : ObservableObject
{
    private readonly IFirmwareCheckService _firmware;

    public ObservableCollection<FirmwareVersion> History { get; } = new();

    [ObservableProperty]
    private string? _model;

    [ObservableProperty]
    private string? _csc;

    [ObservableProperty]
    private string? _currentBuildId;

    [ObservableProperty]
    private FirmwareVersion? _latest;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private string? _diagnostic;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _updateAvailable;

    public FirmwareViewModel(IFirmwareCheckService firmware)
    {
        _firmware = firmware;
    }

    /// <summary>
    /// Pre-fill form from a probed device. Called when the Device tab's selection changes.
    /// </summary>
    public void PrefillFrom(DeviceInfo? device)
    {
        if (device is null) return;
        if (!string.IsNullOrWhiteSpace(device.Model)) Model = device.Model;
        if (!string.IsNullOrWhiteSpace(device.Csc)) Csc = device.Csc;
        // Use the Samsung PDA (AP firmware version) for comparison — NOT ro.build.id (Android build ID).
        var current = device.SamsungPda ?? device.BuildId;
        if (!string.IsNullOrWhiteSpace(current)) CurrentBuildId = current;

        // Reset latest so we don't show a stale match.
        Latest = null;
        UpdateAvailable = false;
        StatusText = null;
        Diagnostic = null;
    }

    [RelayCommand]
    public async Task CheckLatestAsync()
    {
        if (string.IsNullOrWhiteSpace(Model) || string.IsNullOrWhiteSpace(Csc))
        {
            Diagnostic = "Enter model + CSC, or select a device on the Device tab to autofill.";
            return;
        }

        IsChecking = true;
        Diagnostic = null;
        StatusText = "Querying Samsung OTA feed…";
        try
        {
            var result = await _firmware.GetLatestAsync(Model, Csc).ConfigureAwait(true);
            if (result is null)
            {
                StatusText = null;
                Diagnostic = $"Samsung returned no firmware feed for {Model} / {Csc}. The model+CSC pair may be invalid, or the device is too new for the public feed.";
                Latest = null;
                History.Clear();
                UpdateAvailable = false;
                return;
            }

            Latest = result.Latest;
            History.Clear();
            foreach (var v in result.UpgradeHistory) History.Add(v);

            UpdateAvailable = !string.IsNullOrWhiteSpace(CurrentBuildId)
                && !string.Equals(CurrentBuildId, Latest.Pda, StringComparison.OrdinalIgnoreCase)
                && FirmwareVersion.ComparePda(Latest.Pda, CurrentBuildId) > 0;

            StatusText = UpdateAvailable
                ? "Update available"
                : "You're on the latest";
        }
        catch (HttpRequestException ex)
        {
            StatusText = null;
            Diagnostic = $"Network error: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusText = null;
            Diagnostic = $"Lookup failed: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
    }
}

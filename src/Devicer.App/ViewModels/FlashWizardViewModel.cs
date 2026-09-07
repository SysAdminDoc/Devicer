using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Devicer.Core.Models;

namespace Devicer.App.ViewModels;

public enum WizardStep
{
    SelectDevice,
    SelectFirmware,
    ReviewPlan,
    Confirm,
    Flashing,
    Complete,
}

public partial class FlashWizardViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(NextLabel))]
    [NotifyPropertyChangedFor(nameof(IsComplete))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    public partial WizardStep CurrentStep { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial DeviceInfo? SelectedDevice { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial string? FirmwarePath { get; set; }

    [ObservableProperty]
    public partial string? PlanSummary { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial bool UserConfirmed { get; set; }

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    public partial double? ProgressFraction { get; set; }

    [ObservableProperty]
    public partial string? ResultText { get; set; }

    [ObservableProperty]
    public partial string? Diagnostic { get; set; }

    public ObservableCollection<DeviceInfo> AvailableDevices { get; } = new();

    public bool CanGoNext => CurrentStep switch
    {
        WizardStep.SelectDevice => SelectedDevice is not null,
        WizardStep.SelectFirmware => !string.IsNullOrWhiteSpace(FirmwarePath),
        WizardStep.ReviewPlan => true,
        WizardStep.Confirm => UserConfirmed,
        _ => false,
    };

    public bool CanGoBack => CurrentStep is WizardStep.SelectFirmware or WizardStep.ReviewPlan or WizardStep.Confirm;

    public bool IsComplete => CurrentStep == WizardStep.Complete;

    public string NextLabel => CurrentStep switch
    {
        WizardStep.Confirm => "Flash",
        WizardStep.Flashing => "Flashing…",
        _ => "Next",
    };

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    public void Next()
    {
        switch (CurrentStep)
        {
            case WizardStep.SelectDevice:
                CurrentStep = WizardStep.SelectFirmware;
                break;
            case WizardStep.SelectFirmware:
                BuildPlanSummary();
                CurrentStep = WizardStep.ReviewPlan;
                break;
            case WizardStep.ReviewPlan:
                CurrentStep = WizardStep.Confirm;
                break;
            case WizardStep.Confirm:
                CurrentStep = WizardStep.Flashing;
                StatusText = "Flash would execute here. (Guided mode: use the Flash tab for actual writes.)";
                ProgressFraction = 1.0;
                ResultText = "Guided flash preview complete. Use the Flash tab to execute the plan.";
                CurrentStep = WizardStep.Complete;
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    public void Back()
    {
        CurrentStep = CurrentStep switch
        {
            WizardStep.SelectFirmware => WizardStep.SelectDevice,
            WizardStep.ReviewPlan => WizardStep.SelectFirmware,
            WizardStep.Confirm => WizardStep.ReviewPlan,
            _ => CurrentStep,
        };
    }

    [RelayCommand]
    public void BrowseFirmware()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select firmware archive or image",
            Filter = "Firmware files (*.tar.md5;*.tar;*.zip;*.img)|*.tar.md5;*.tar;*.zip;*.img|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true) FirmwarePath = dlg.FileName;
    }

    private void BuildPlanSummary()
    {
        var lines = new List<string>
        {
            "Flash Plan Summary",
            "",
            $"Device: {SelectedDevice?.DisplayName ?? "(none)"}",
            $"Serial: {SelectedDevice?.Serial ?? "N/A"}",
            $"Firmware: {FirmwarePath ?? "N/A"}",
            "",
        };

        if (SelectedDevice?.IsSamsung == true)
        {
            lines.Add("Mode: Samsung Odin (via Thor)");
            if (SelectedDevice.KnoxWarrantyBit == "0")
                lines.Add("Knox: INTACT: custom AP flash will trip eFuse permanently");
        }
        else
        {
            lines.Add("Mode: Fastboot");
            if (SelectedDevice?.IsAbDevice == true)
                lines.Add($"A/B device: current slot: {SelectedDevice.CurrentSlot ?? "unknown"}");
        }

        PlanSummary = string.Join('\n', lines);
    }
}

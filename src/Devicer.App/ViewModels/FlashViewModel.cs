using CommunityToolkit.Mvvm.ComponentModel;
using Devicer.Core.Models;

namespace Devicer.App.ViewModels;

public partial class FlashPageViewModel : ObservableObject
{
    public OdinFlashViewModel Odin { get; }
    public FastbootFlashViewModel Fastboot { get; }

    public FlashPageViewModel(OdinFlashViewModel odin, FastbootFlashViewModel fastboot)
    {
        Odin = odin;
        Fastboot = fastboot;
    }

    public void PrefillFrom(DeviceInfo? device)
    {
        Odin.PrefillFrom(device);
        Fastboot.PrefillFrom(device);
    }
}

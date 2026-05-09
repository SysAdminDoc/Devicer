namespace Devicer.Core.Models;

public enum ConnectionState
{
    NotConnected,
    Unauthorized,
    Adb,
    Recovery,
    Sideload,
    Fastboot,
    Bootloader,
    Download,
    Unknown,
}

using System.Windows.Controls;

namespace Devicer.App.Views;

public partial class StubPage : UserControl
{
    public StubPage(string heading, string subheading, string status, string detail)
    {
        InitializeComponent();
        HeadingText.Text = heading;
        SubheadingText.Text = subheading;
        StatusText.Text = status;
        DetailText.Text = detail;
    }

    public StubPage() : this("Section", "Coming in a later version.", "Not implemented yet.",
        "See the project ROADMAP for the planned scope of this section.")
    {
    }
}

public sealed class PatchPage : StubPage
{
    public PatchPage() : base(
        "Patch",
        "PC-side Magisk boot.img patcher — no phone roundtrip.",
        "Coming in v0.6.0.",
        "Wraps affggh/Magisk_patcher to patch boot.img / init_boot.img on the host. KernelSU patch path via ksud boot-patch is also planned.")
    { }
}

public sealed class FlashPage : StubPage
{
    public FlashPage() : base(
        "Flash",
        "Samsung Odin-protocol flasher — Thor + EFS-clear and Knox safety gates.",
        "Coming in v0.7.0.",
        "Subprocess-wrapped Thor Flash Utility (GPL-3.0, kept across the process boundary). EFS-Clear is OFF by default; Knox eFuse warning gates any custom AP flash.")
    { }
}

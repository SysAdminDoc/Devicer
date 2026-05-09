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


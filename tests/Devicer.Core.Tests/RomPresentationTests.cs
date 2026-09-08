using System.Xml.Linq;

namespace Devicer.Core.Tests;

public class RomPresentationTests
{
    [Fact]
    public void Search_result_digest_is_available_not_verified_before_download()
    {
        var page = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "RomsPage.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var badge = Assert.Single(page.Descendants(wpf + "TextBlock"), element =>
            element.Attribute("Visibility")?.Value.StartsWith("{Binding Sha256,") == true);
        var label = Assert.Single(badge.Elements(wpf + "Run"));
        Assert.Equal("SHA-256 available", label.Attribute("Text")?.Value);
    }
}

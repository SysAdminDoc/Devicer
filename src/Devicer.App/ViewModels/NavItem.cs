using System.Windows.Controls;

namespace Devicer.App.ViewModels;

public sealed record NavItem(string Glyph, string Label, Func<UserControl> ViewFactory);

using System.Windows;

namespace OptimAdhan.App.Controls;

/// <summary>
/// Inherited flag set on the sidebar when it is a narrow icon rail, so the
/// navigation and quick-toggle templates can switch to their compact look.
/// </summary>
public static class Shell
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.RegisterAttached(
        "IsCompact", typeof(bool), typeof(Shell),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsCompact(DependencyObject element) => (bool)element.GetValue(IsCompactProperty);

    public static void SetIsCompact(DependencyObject element, bool value) => element.SetValue(IsCompactProperty, value);
}

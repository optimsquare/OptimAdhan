using System.Windows;
using System.Windows.Controls;

namespace OptimAdhan.App.Controls;

/// <summary>
/// Stacks cards in two columns when both can be at least <see cref="MinColumnWidth"/> wide
/// (each card picks its side with <c>CardColumns.Column</c>), otherwise in one column in
/// document order, no wider than <see cref="MaxSingleColumnWidth"/>. Cards keep their
/// natural height; nothing is stretched to fill the page.
/// </summary>
public sealed class CardColumns : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(CardColumns),
        new FrameworkPropertyMetadata(480.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxSingleColumnWidthProperty = DependencyProperty.Register(
        nameof(MaxSingleColumnWidth), typeof(double), typeof(CardColumns),
        new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(CardColumns),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnProperty = DependencyProperty.RegisterAttached(
        "Column", typeof(int), typeof(CardColumns),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double MaxSingleColumnWidth
    {
        get => (double)GetValue(MaxSingleColumnWidthProperty);
        set => SetValue(MaxSingleColumnWidthProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static int GetColumn(UIElement element) => (int)element.GetValue(ColumnProperty);

    public static void SetColumn(UIElement element, int value) => element.SetValue(ColumnProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinColumnWidth : availableSize.Width;
        var (twoColumns, columnWidth) = Plan(width);
        var heights = new double[2];
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            Place(heights, twoColumns ? Side(child) : 0, child);
        }

        return new Size(twoColumns ? width : columnWidth, Math.Max(heights[0], heights[1]));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (twoColumns, columnWidth) = Plan(finalSize.Width);
        var heights = new double[2];
        foreach (UIElement child in InternalChildren)
        {
            var side = twoColumns ? Side(child) : 0;
            var top = Place(heights, side, child);
            child.Arrange(new Rect(side * (columnWidth + Spacing), top, columnWidth, child.DesiredSize.Height));
        }

        return finalSize;
    }

    private (bool TwoColumns, double ColumnWidth) Plan(double width)
    {
        var twoColumns = width >= MinColumnWidth * 2 + Spacing;
        return twoColumns ? (true, (width - Spacing) / 2) : (false, Math.Min(width, MaxSingleColumnWidth));
    }

    private static int Side(UIElement child) => GetColumn(child) == 1 ? 1 : 0;

    /// <summary>Reserves room for <paramref name="child"/> at the foot of a column; returns its top.</summary>
    private double Place(double[] heights, int side, UIElement child)
    {
        if (child.Visibility == Visibility.Collapsed)
        {
            return heights[side];
        }

        var top = heights[side] > 0 ? heights[side] + Spacing : 0;
        heights[side] = top + child.DesiredSize.Height;
        return top;
    }
}

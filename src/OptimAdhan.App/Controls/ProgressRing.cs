using System.Windows;
using System.Windows.Media;

namespace OptimAdhan.App.Controls;

/// <summary>
/// Thin circular progress ring with a glowing head, drawn directly in OnRender.
/// It redraws only when Progress changes (about every few seconds), never on a timer.
/// </summary>
public sealed class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(
        nameof(StrokeWidth), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(ProgressRing),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x22, 0xF6, 0xF1, 0xE7)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ArcBrushProperty = DependencyProperty.Register(
        nameof(ArcBrush), typeof(Brush), typeof(ProgressRing),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x5A)), FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush HeadGlow = Freeze(new RadialGradientBrush(Color.FromArgb(0xAA, 0xFF, 0xE9, 0xA8), Color.FromArgb(0x00, 0xFF, 0xD6, 0x5A)));
    private static readonly Brush HeadFill = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xD0)));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush ArcBrush
    {
        get => (Brush)GetValue(ArcBrushProperty);
        set => SetValue(ArcBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= StrokeWidth * 2)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = size / 2 - StrokeWidth;
        dc.DrawEllipse(null, new Pen(TrackBrush, StrokeWidth), center, radius, radius);

        var progress = Math.Clamp(Progress, 0, 1);
        if (progress <= 0)
        {
            return;
        }

        var end = PointAt(center, radius, progress);
        if (progress >= 0.999)
        {
            dc.DrawEllipse(null, new Pen(ArcBrush, StrokeWidth), center, radius, radius);
        }
        else
        {
            var figure = new PathFigure { StartPoint = PointAt(center, radius, 0), IsClosed = false };
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, progress > 0.5, SweepDirection.Clockwise, true));
            var geometry = new PathGeometry([figure]);
            var pen = new Pen(ArcBrush, StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, geometry);
        }

        dc.DrawEllipse(HeadGlow, null, end, StrokeWidth * 1.8, StrokeWidth * 1.8);
        dc.DrawEllipse(HeadFill, null, end, StrokeWidth * 0.7, StrokeWidth * 0.7);
    }

    private static Point PointAt(Point center, double radius, double fraction)
    {
        var angle = fraction * 2 * Math.PI - Math.PI / 2;
        return new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

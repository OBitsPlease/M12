using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MultibandCore;

public sealed class RotaryKnob : RangeBase
{
    private Point _dragOrigin;
    private double _dragValue;

    static RotaryKnob()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RotaryKnob), new FrameworkPropertyMetadata(typeof(RotaryKnob)));
    }

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(RotaryKnob), new FrameworkPropertyMetadata(0.1));

    public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
        nameof(ValueFormat), typeof(string), typeof(RotaryKnob), new FrameworkPropertyMetadata("+0.0;-0.0;0.0"));

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragOrigin = e.GetPosition(this);
        _dragValue = Value;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        var movement = (_dragOrigin.Y - position.Y) + (position.X - _dragOrigin.X);
        var rawValue = _dragValue + movement / 160.0 * (Maximum - Minimum);
        var snappedValue = Math.Round(rawValue / Step) * Step;
        Value = Math.Clamp(snappedValue, Minimum, Maximum);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var valueText = Value.ToString(ValueFormat, CultureInfo.InvariantCulture);
        var formattedText = new FormattedText(
            valueText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            9,
            Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var knobAreaHeight = Math.Max(0, ActualHeight - formattedText.Height - 2);
        var center = new Point(ActualWidth / 2.0, knobAreaHeight / 2.0);
        var outerRadius = Math.Min(ActualWidth * 0.38, knobAreaHeight / 2.0);
        var radius = outerRadius * 0.78;
        if (radius <= 2)
        {
            return;
        }

        var tickPen = new Pen(new SolidColorBrush(Color.FromRgb(112, 118, 126)), 1);
        for (var index = 0; index <= 20; index++)
        {
            var angle = -135.0 + index * 270.0 / 20.0;
            var outer = PointOnCircle(center, outerRadius, angle);
            var tickLength = index % 5 == 0 ? outerRadius - radius : (outerRadius - radius) * 0.55;
            var inner = PointOnCircle(center, outerRadius - tickLength, angle);
            drawingContext.DrawLine(tickPen, inner, outer);
        }

        var shadow = new SolidColorBrush(Color.FromArgb(130, 0, 0, 0));
        drawingContext.DrawEllipse(shadow, null, new Point(center.X + 1.5, center.Y + 2.5), radius + 2, radius + 2);
        var face = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.32, 0.25),
            Center = new Point(0.38, 0.32),
            RadiusX = 0.72,
            RadiusY = 0.72,
            GradientStops =
            {
                new GradientStop(Color.FromRgb(116, 120, 126), 0),
                new GradientStop(Color.FromRgb(57, 60, 65), 0.56),
                new GradientStop(Color.FromRgb(24, 26, 29), 1)
            }
        };
        drawingContext.DrawEllipse(face, new Pen(new SolidColorBrush(Color.FromRgb(15, 16, 18)), 1.5), center, radius, radius);

        var normalized = Maximum > Minimum ? (Value - Minimum) / (Maximum - Minimum) : 0.5;
        var indicatorAngle = -135.0 + normalized * 270.0;
        var indicatorStart = PointOnCircle(center, radius * 0.56, indicatorAngle);
        var indicatorEnd = PointOnCircle(center, radius * 0.82, indicatorAngle);
        drawingContext.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(111, 230, 82)), 2.2), indicatorStart, indicatorEnd);

        drawingContext.DrawText(formattedText, new Point(center.X - formattedText.Width / 2.0, ActualHeight - formattedText.Height));
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        InvalidateVisual();
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180.0;
        return new Point(center.X + Math.Sin(radians) * radius, center.Y - Math.Cos(radians) * radius);
    }
}
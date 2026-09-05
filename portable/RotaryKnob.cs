using System.Globalization;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace BitsPleaseYTM6.Portable;

public sealed class RotaryKnob : RangeBase
{
    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<RotaryKnob, double>(nameof(Step), 0.1);

    public static readonly StyledProperty<string> ValueFormatProperty =
        AvaloniaProperty.Register<RotaryKnob, string>(nameof(ValueFormat), "+0.0;-0.0;0.0");

    private Point _dragOrigin;
    private double _dragValue;
    private bool _dragging;

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public string ValueFormat
    {
        get => GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 96 : Math.Min(96, availableSize.Width),
            double.IsInfinity(availableSize.Height) ? 88 : Math.Min(88, availableSize.Height));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragOrigin = e.GetPosition(this);
        _dragValue = Value;
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
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

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty ||
            change.Property == MinimumProperty ||
            change.Property == MaximumProperty ||
            change.Property == ValueFormatProperty ||
            change.Property == ForegroundProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var text = new FormattedText(
            Value.ToString(ValueFormat, CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight),
            9,
            Foreground);
        var knobHeight = Math.Max(0, Bounds.Height - text.Height - 2);
        var center = new Point(Bounds.Width / 2.0, knobHeight / 2.0);
        var outerRadius = Math.Min(Bounds.Width * 0.4, knobHeight / 2.0);
        var radius = outerRadius * 0.76;
        if (radius <= 2)
        {
            return;
        }

        var tickPen = new Pen(Brush.Parse("#70767E"), 1);
        for (var index = 0; index <= 20; index++)
        {
            var angle = -135.0 + index * 270.0 / 20.0;
            var outer = PointOnCircle(center, outerRadius, angle);
            var tickLength = index % 5 == 0 ? outerRadius - radius : (outerRadius - radius) * 0.55;
            context.DrawLine(tickPen, PointOnCircle(center, outerRadius - tickLength, angle), outer);
        }

        context.DrawEllipse(Brush.Parse("#82000000"), null, new Point(center.X + 1.5, center.Y + 2.5), radius + 2, radius + 2);
        var face = new RadialGradientBrush
        {
            Center = new RelativePoint(0.38, 0.32, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.32, 0.25, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.72, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.72, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#74787E"), 0),
                new GradientStop(Color.Parse("#393C41"), 0.56),
                new GradientStop(Color.Parse("#181A1D"), 1)
            }
        };
        context.DrawEllipse(face, new Pen(Brush.Parse("#0F1012"), 1.5), center, radius, radius);

        var normalized = Maximum > Minimum ? (Value - Minimum) / (Maximum - Minimum) : 0.5;
        var indicatorAngle = -135.0 + normalized * 270.0;
        context.DrawLine(
            new Pen(Brush.Parse("#6FE652"), 2.2),
            PointOnCircle(center, radius * 0.54, indicatorAngle),
            PointOnCircle(center, radius * 0.84, indicatorAngle));
        context.DrawText(text, new Point(center.X - text.Width / 2.0, Bounds.Height - text.Height));
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180.0;
        return new Point(center.X + Math.Sin(radians) * radius, center.Y - Math.Cos(radians) * radius);
    }
}

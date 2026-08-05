using System.Windows;
using System.Windows.Media;

namespace DiscordMultiband;

public sealed class CompressorGraph : FrameworkElement
{
    public IReadOnlyList<BandSettings> Bands { get; set; } = [];
    public IReadOnlyList<CrossoverSetting> Crossovers { get; set; } = [];

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 19, 23)), null, new Rect(0, 0, width, height));
        DrawGrid(drawingContext, width, height);
        DrawBands(drawingContext, width, height);
        DrawResponse(drawingContext, width, height);
    }

    private void DrawGrid(DrawingContext drawingContext, double width, double height)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(48, 52, 59)), 1);
        double[] frequencies = [20, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 20000];
        foreach (var frequency in frequencies)
        {
            var x = FrequencyToX(frequency, width);
            drawingContext.DrawLine(gridPen, new Point(x, 0), new Point(x, height));
            var label = frequency >= 1000 ? $"{frequency / 1000:0.#}k" : $"{frequency:0}";
            DrawText(drawingContext, label, new Point(x + 4, height - 20), 10, Color.FromRgb(130, 136, 146));
        }

        for (var db = -18; db <= 18; db += 6)
        {
            var y = DbToY(db, height);
            drawingContext.DrawLine(gridPen, new Point(0, y), new Point(width, y));
            DrawText(drawingContext, db.ToString(), new Point(5, y - 14), 10, Color.FromRgb(130, 136, 146));
        }
    }

    private void DrawBands(DrawingContext drawingContext, double width, double height)
    {
        for (var index = 0; index < Bands.Count; index++)
        {
            var left = index == 0 ? 0 : FrequencyToX(Crossovers[index - 1].Frequency, width);
            var right = index == Bands.Count - 1 ? width : FrequencyToX(Crossovers[index].Frequency, width);
            var color = (Color)ColorConverter.ConvertFromString(Bands[index].Color)!;
            drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(18, color.R, color.G, color.B)), null, new Rect(left, 0, right - left, height));
        }

        foreach (var crossover in Crossovers)
        {
            var x = FrequencyToX(crossover.Frequency, width);
            drawingContext.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(89, 96, 108)), 1.5), new Point(x, 0), new Point(x, height));
        }
    }

    private void DrawResponse(DrawingContext drawingContext, double width, double height)
    {
        if (Bands.Count == 0)
        {
            return;
        }

        var points = new List<Point>();
        for (var pixel = 0; pixel <= (int)width; pixel += 3)
        {
            var frequency = XToFrequency(pixel, width);
            var weightedGain = 0.0;
            var totalWeight = 0.0;
            for (var index = 0; index < Bands.Count; index++)
            {
                var distance = Math.Log(frequency / GetBandCenter(index), 2);
                var weight = Math.Exp(-distance * distance / 1.3);
                weightedGain += (Bands[index].MakeupGain - Bands[index].GainReduction) * weight;
                totalWeight += weight;
            }
            points.Add(new Point(pixel, DbToY(weightedGain / Math.Max(0.001, totalWeight), height)));
        }

        var fillGeometry = new StreamGeometry();
        using (var context = fillGeometry.Open())
        {
            context.BeginFigure(new Point(0, height / 2), true, true);
            context.PolyLineTo(points, true, false);
            context.LineTo(new Point(width, height / 2), true, false);
        }
        fillGeometry.Freeze();
        drawingContext.DrawGeometry(new SolidColorBrush(Color.FromArgb(55, 220, 52, 185)), null, fillGeometry);

        var responseGeometry = new StreamGeometry();
        using (var context = responseGeometry.Open())
        {
            context.BeginFigure(points[0], false, false);
            context.PolyLineTo(points.Skip(1).ToList(), true, false);
        }
        responseGeometry.Freeze();
        drawingContext.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(242, 95, 202)), 2.5), responseGeometry);

        for (var index = 0; index < Bands.Count; index++)
        {
            var x = FrequencyToX(GetBandCenter(index), width);
            var y = DbToY(Bands[index].MakeupGain - Bands[index].GainReduction, height);
            var color = (Color)ColorConverter.ConvertFromString(Bands[index].Color)!;
            drawingContext.DrawEllipse(new SolidColorBrush(color), new Pen(Brushes.White, 1), new Point(x, y), 5, 5);
        }
    }

    private static double FrequencyToX(double frequency, double width) => Math.Log10(frequency / 20.0) / 3.0 * width;
    private static double XToFrequency(double x, double width) => 20.0 * Math.Pow(1000.0, x / width);
    private static double DbToY(double db, double height) => height / 2.0 - db / 36.0 * height;

    private double GetBandCenter(int index)
    {
        var lower = index == 0 ? 20.0 : Crossovers[index - 1].Frequency;
        var upper = index == Bands.Count - 1 ? 20000.0 : Crossovers[index].Frequency;
        return Math.Sqrt(lower * upper);
    }

    private static void DrawText(DrawingContext context, string text, Point point, double size, Color color)
    {
        var formattedText = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Bahnschrift"), size, new SolidColorBrush(color), 1.0);
        context.DrawText(formattedText, point);
    }
}
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace DMGLauncher;

// Silver text with a 1px shifting-rainbow outline and a 1px black outline outside that.
// TextBlock can't stroke text, so this draws the glyph outlines itself.
public class OutlinedText : Control
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<OutlinedText, string>(nameof(Text), "");
    public static readonly StyledProperty<FontFamily> FontProperty = AvaloniaProperty.Register<OutlinedText, FontFamily>(nameof(Font), FontFamily.Default);
    public static readonly StyledProperty<double> FontSizeProperty = AvaloniaProperty.Register<OutlinedText, double>(nameof(FontSize), 24);
    public static readonly StyledProperty<double> LetterSpacingProperty = AvaloniaProperty.Register<OutlinedText, double>(nameof(LetterSpacing));

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public FontFamily Font { get => GetValue(FontProperty); set => SetValue(FontProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public double LetterSpacing { get => GetValue(LetterSpacingProperty); set => SetValue(LetterSpacingProperty, value); }

    const double Pad = 2; // room for the outer (black) outline
    static readonly Pen Black = new(Brushes.Black, 4, lineJoin: PenLineJoin.Round);
    static readonly GradientStops Rainbow =
    [
        new(Color.Parse("#FF4D6D"), 0), new(Color.Parse("#FFB347"), 0.17), new(Color.Parse("#FFF275"), 0.33),
        new(Color.Parse("#5CF2A0"), 0.5), new(Color.Parse("#4DC3FF"), 0.67), new(Color.Parse("#B57BFF"), 0.83), new(Color.Parse("#FF4D6D"), 1),
    ];
    static readonly IBrush Silver = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = [new(Color.Parse("#FFFFFF"), 0), new(Color.Parse("#D4DBEA"), 0.5), new(Color.Parse("#8591AD"), 1)],
    };

    Geometry? _glyphs;
    Size _size;
    double _phase;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    static OutlinedText() => AffectsMeasure<OutlinedText>(TextProperty, FontProperty, FontSizeProperty, LetterSpacingProperty);

    public OutlinedText() => _timer.Tick += (_, _) =>
    {
        _phase = (_phase + _size.Width / 360) % Math.Max(_size.Width, 1); // one full cycle every ~12 s
        InvalidateVisual();
    };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _timer.Start(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); _timer.Stop(); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var group = new GeometryGroup();
        var typeface = new Typeface(Font);
        double x = 0;
        foreach (var ch in Text)
        {
            var ft = new FormattedText(ch.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, FontSize, Brushes.White);
            if (ft.BuildGeometry(new Point(x, 0)) is { } g) group.Children.Add(g);
            x += ft.WidthIncludingTrailingWhitespace + LetterSpacing;
        }
        // Size to the ink, not the line box: this font leaves ~40% of its line height empty around capitals.
        var ink = group.Bounds;
        group.Transform = new TranslateTransform(Pad - ink.X, Pad - ink.Y);
        _glyphs = group;
        _size = new Size(ink.Width + 2 * Pad, ink.Height + 2 * Pad);
        return _size;
    }

    public override void Render(DrawingContext context)
    {
        if (_glyphs is null) return;
        var rainbow = new LinearGradientBrush
        {
            SpreadMethod = GradientSpreadMethod.Repeat,
            StartPoint = new RelativePoint(_phase, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(_phase + _size.Width, 0, RelativeUnit.Absolute),
            GradientStops = Rainbow,
        };
        context.DrawGeometry(null, Black, _glyphs);                                          // 2px out: black
        context.DrawGeometry(null, new Pen(rainbow, 2, lineJoin: PenLineJoin.Round), _glyphs); // 1px out: rainbow
        context.DrawGeometry(Silver, null, _glyphs);                                         // covers the inner half of both
    }
}

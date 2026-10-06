using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AideDeCamp;

/// <summary>
/// Lightweight NATO-style APP-6-inspired counter used as the persistent visual anchor for OOB nodes.
/// It intentionally focuses on the symbols needed by GTCW: infantry, cavalry, artillery and HQ echelons.
/// </summary>
public sealed class NatoCounterControl : FrameworkElement
{
    public static readonly DependencyProperty UnitTypeProperty = DependencyProperty.Register(
        nameof(UnitType), typeof(int), typeof(NatoCounterControl), new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UnitTierProperty = DependencyProperty.Register(
        nameof(UnitTier), typeof(int), typeof(NatoCounterControl), new FrameworkPropertyMetadata(13, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsHeadquartersProperty = DependencyProperty.Register(
        nameof(IsHeadquarters), typeof(bool), typeof(NatoCounterControl), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrengthAlertColorProperty = DependencyProperty.Register(
        nameof(StrengthAlertColor), typeof(string), typeof(NatoCounterControl), new FrameworkPropertyMetadata("#7EAAB2", FrameworkPropertyMetadataOptions.AffectsRender));

    public int UnitType { get => (int)GetValue(UnitTypeProperty); set => SetValue(UnitTypeProperty, value); }
    public int UnitTier { get => (int)GetValue(UnitTierProperty); set => SetValue(UnitTierProperty, value); }
    public bool IsHeadquarters { get => (bool)GetValue(IsHeadquartersProperty); set => SetValue(IsHeadquartersProperty, value); }
    public string StrengthAlertColor { get => (string)GetValue(StrengthAlertColorProperty); set => SetValue(StrengthAlertColorProperty, value); }

    public double EchelonScale { get; set; } = 1.2;
    public bool ShowHqStaff { get; set; } = true;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        // Make the full counter, including space around its echelon marks, clickable.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var w = Math.Max(40, ActualWidth);
        // Headquarters counters reserve a short staff beneath the frame so HQ status is
        // encoded by the NATO counter itself rather than a separate text tag.
        var top = 14 * EchelonScale;
        var h = Math.Max(28, ActualHeight - top - (IsHeadquarters && ShowHqStaff ? 12 : 2));
        var rect = new Rect(0.5, top + .5, w - 1, h - 1);
        var fill = new SolidColorBrush(Color.FromRgb(226, 222, 190));
        var line = new Pen(new SolidColorBrush(Color.FromRgb(35, 43, 49)), 1.7);
        dc.DrawRectangle(fill, line, rect);

        // Echelon amplifier above the frame.
        var echelon = UnitTier switch
        {
            18 => "XXXXX", 17 => "XXXX", 16 => "XXXX", 15 => "XXX", 14 => "XX", 13 => "X",
            12 => "III", 11 => "II", 10 => "I", _ => ""
        };
        DrawText(dc, echelon, w / 2, 0, 10 * EchelonScale, Brushes.Gainsboro, TextAlignment.Center);

        var cx = w / 2; var cy = top + h / 2;
        switch (UnitType)
        {
            case 0: // infantry X
                dc.DrawLine(line, new Point(5, top + 5), new Point(w - 5, top + h - 5));
                dc.DrawLine(line, new Point(w - 5, top + 5), new Point(5, top + h - 5));
                break;
            case 1: // cavalry diagonal
                dc.DrawLine(new Pen(line.Brush, 2.2), new Point(5, top + h - 5), new Point(w - 5, top + 5));
                break;
            case 2: // artillery dot
                dc.DrawEllipse(line.Brush, null, new Point(cx, cy), Math.Min(w, h) * .13, Math.Min(w, h) * .13);
                break;
            default: // command/HQ: simple command bar
                dc.DrawLine(new Pen(line.Brush, 2.0), new Point(7, cy), new Point(w - 7, cy));
                break;
        }

        // Strength alert cue as a restrained lower edge.
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(StrengthAlertColor)!;
            dc.DrawLine(new Pen(new SolidColorBrush(c), 3), new Point(2, top + h - 2), new Point(w - 2, top + h - 2));
        }
        catch { }

        if (IsHeadquarters && ShowHqStaff)
        {
            var frameBottom = top + h;
            dc.DrawLine(new Pen(line.Brush, 1.7), new Point(8, frameBottom), new Point(8, Math.Max(frameBottom, ActualHeight - 1)));
        }

    }

    private void DrawText(DrawingContext dc, string text, double x, double y, double size, Brush brush, TextAlignment align)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { TextAlignment = align };
        dc.DrawText(ft, new Point(x, y));
    }
}

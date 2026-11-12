using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EarthWatch3D.App.Rendering;

/// <summary>
/// 2D overlay drawn on top of the 3D viewport: event labels with simple collision avoidance.
/// TextBlocks are pooled so per-frame updates do not allocate.
/// Copied from SolarSystem3D's LabelOverlay, without its selection ring (and so without its palette colour).
/// </summary>
public sealed class LabelOverlay
{
    private readonly Canvas _canvas;
    private readonly List<TextBlock> _pool = new();
    private readonly List<Rect> _occupied = new();
    private readonly Dictionary<Color, SolidColorBrush> _brushes = new();
    private int _used;

    public LabelOverlay(Canvas canvas)
    {
        _canvas = canvas;
        _canvas.IsHitTestVisible = false;
    }

    public void Begin()
    {
        _used = 0;
        _occupied.Clear();
    }

    /// <summary>Places a label to the right of a screen point.
    /// When <paramref name="force"/> is false the label is skipped if it would overlap an earlier one.</summary>
    public bool TryAdd(string text, Point at, Color color, bool force = false, double fontSize = 12)
    {
        double w = text.Length * fontSize * 0.55 + 4, h = fontSize + 4;
        var rect = new Rect(at.X + 6, at.Y - h / 2, w, h);
        if (rect.Right < 0 || rect.Left > _canvas.ActualWidth || rect.Bottom < 0 || rect.Top > _canvas.ActualHeight) return false;
        if (!force)
            foreach (var r in _occupied)
                if (r.IntersectsWith(rect)) return false;
        _occupied.Add(rect);

        TextBlock tb;
        if (_used < _pool.Count) tb = _pool[_used];
        else
        {
            tb = new TextBlock { FontFamily = new FontFamily("Segoe UI") };
            _pool.Add(tb);
            _canvas.Children.Add(tb);
        }
        _used++;
        tb.Text = text;
        tb.FontSize = fontSize;
        tb.Foreground = BrushFor(color);
        tb.Visibility = Visibility.Visible;
        Canvas.SetLeft(tb, rect.X);
        Canvas.SetTop(tb, rect.Y);
        return true;
    }

    public void End()
    {
        for (int k = _used; k < _pool.Count; k++)
            if (_pool[k].Visibility != Visibility.Collapsed) _pool[k].Visibility = Visibility.Collapsed;
    }

    private SolidColorBrush BrushFor(Color c)
    {
        if (!_brushes.TryGetValue(c, out var b))
        {
            b = new SolidColorBrush(c);
            b.Freeze();
            _brushes[c] = b;
        }
        return b;
    }
}

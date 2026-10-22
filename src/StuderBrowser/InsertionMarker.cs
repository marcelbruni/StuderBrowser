using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace StuderBrowser;

public sealed class InsertionMarker : Adorner
{
    private static readonly Pen LinePen = CreatePen();
    private Point _top;
    private double _height;

    private InsertionMarker(UIElement adornedElement)
        : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    public static InsertionMarker Show(InsertionMarker? marker, UIElement element, Point top, double height)
    {
        if (marker is null)
        {
            marker = new InsertionMarker(element);
            AdornerLayer.GetAdornerLayer(element)!.Add(marker);
        }

        marker._top = top;
        marker._height = height;
        marker.InvalidateVisual();
        return marker;
    }

    public static void Hide(ref InsertionMarker? marker)
    {
        if (marker is null)
        {
            return;
        }

        AdornerLayer.GetAdornerLayer(marker.AdornedElement)?.Remove(marker);
        marker = null;
    }

    protected override void OnRender(DrawingContext drawingContext) =>
        drawingContext.DrawLine(LinePen, _top, new Point(_top.X, _top.Y + _height));

    private static Pen CreatePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x1A, 0x73, 0xE8)), 2);
        pen.Freeze();
        return pen;
    }
}

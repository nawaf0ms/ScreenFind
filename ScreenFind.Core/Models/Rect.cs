namespace ScreenFind.Core.Models;

/// <summary>
/// Axis-aligned rectangle in physical screen pixels.
/// Deliberately defined here instead of using <c>System.Windows.Rect</c> so that
/// ScreenFind.Core stays free of WPF (see spec §10.3).
/// </summary>
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public static readonly Rect Empty = new(0, 0, 0, 0);

    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double Area => Width <= 0 || Height <= 0 ? 0 : Width * Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;

    public static Rect FromEdges(double left, double top, double right, double bottom)
        => new(left, top, right - left, bottom - top);

    public Rect Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    public Rect Scale(double factor) => new(X * factor, Y * factor, Width * factor, Height * factor);

    public Rect Inflate(double dx, double dy) => new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);

    public Rect Union(Rect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return FromEdges(
            Math.Min(Left, other.Left),
            Math.Min(Top, other.Top),
            Math.Max(Right, other.Right),
            Math.Max(Bottom, other.Bottom));
    }

    public Rect Intersect(Rect other)
    {
        double left = Math.Max(Left, other.Left);
        double top = Math.Max(Top, other.Top);
        double right = Math.Min(Right, other.Right);
        double bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? Empty : FromEdges(left, top, right, bottom);
    }

    /// <summary>Intersection over union — used to de-duplicate boxes coming from two OCR engines.</summary>
    public double IntersectionOverUnion(Rect other)
    {
        double intersection = Intersect(other).Area;
        if (intersection <= 0) return 0;
        double union = Area + other.Area - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    /// <summary>Fraction of the smaller rectangle covered by the intersection.</summary>
    public double OverlapRatio(Rect other)
    {
        double intersection = Intersect(other).Area;
        if (intersection <= 0) return 0;
        double smaller = Math.Min(Area, other.Area);
        return smaller <= 0 ? 0 : intersection / smaller;
    }

    /// <summary>Vertical overlap as a fraction of the shorter box — used for line grouping.</summary>
    public double VerticalOverlapRatio(Rect other)
    {
        double overlap = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        if (overlap <= 0) return 0;
        double shorter = Math.Min(Height, other.Height);
        return shorter <= 0 ? 0 : overlap / shorter;
    }

    public override string ToString()
        => $"({X:0.##},{Y:0.##} {Width:0.##}x{Height:0.##})";
}

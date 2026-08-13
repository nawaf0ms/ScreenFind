namespace ScreenFind.Core.Models;

/// <param name="Bounds">One rectangle per visual line the match spans.</param>
/// <param name="Score">1.0 = exact match on normalized text.</param>
public record Match(
    int StartWordIndex,
    int EndWordIndex,
    IReadOnlyList<Rect> Bounds,
    float Score)
{
    public bool IsExact => Score >= 1f;

    public Rect BoundingBox
    {
        get
        {
            var result = Rect.Empty;
            foreach (var b in Bounds) result = result.Union(b);
            return result;
        }
    }
}

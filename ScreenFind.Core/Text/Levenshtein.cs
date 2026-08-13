namespace ScreenFind.Core.Text;

public static class Levenshtein
{
    /// <summary>
    /// Edit distance with an upper bound: as soon as every cell in a row exceeds
    /// <paramref name="maxDistance"/> the computation stops and returns <c>maxDistance + 1</c>.
    /// </summary>
    public static int Distance(ReadOnlySpan<char> a, ReadOnlySpan<char> b, int maxDistance = int.MaxValue)
    {
        if (a.Length == 0) return Math.Min(b.Length, maxDistance == int.MaxValue ? b.Length : maxDistance + 1);
        if (b.Length == 0) return Math.Min(a.Length, maxDistance == int.MaxValue ? a.Length : maxDistance + 1);

        if (maxDistance != int.MaxValue && Math.Abs(a.Length - b.Length) > maxDistance)
            return maxDistance + 1;

        // Keep the shorter span on the row axis to minimise allocation.
        if (b.Length < a.Length)
        {
            var tmp = a;
            a = b;
            b = tmp;
        }

        Span<int> previous = a.Length < 256 ? stackalloc int[a.Length + 1] : new int[a.Length + 1];
        Span<int> current = a.Length < 256 ? stackalloc int[a.Length + 1] : new int[a.Length + 1];

        for (int i = 0; i <= a.Length; i++) previous[i] = i;

        for (int j = 1; j <= b.Length; j++)
        {
            current[0] = j;
            int rowMin = current[0];
            char bj = b[j - 1];

            for (int i = 1; i <= a.Length; i++)
            {
                int cost = a[i - 1] == bj ? 0 : 1;
                int value = Math.Min(
                    Math.Min(current[i - 1] + 1, previous[i] + 1),
                    previous[i - 1] + cost);
                current[i] = value;
                if (value < rowMin) rowMin = value;
            }

            if (rowMin > maxDistance) return maxDistance + 1;

            var swap = previous;
            previous = current;
            current = swap;
        }

        return previous[a.Length];
    }

    /// <summary>1.0 = identical, 0.0 = nothing in common. Normalized by the longer length.</summary>
    public static double Similarity(ReadOnlySpan<char> a, ReadOnlySpan<char> b, double minSimilarity = 0)
    {
        int longest = Math.Max(a.Length, b.Length);
        if (longest == 0) return 1.0;

        int maxDistance = minSimilarity <= 0
            ? int.MaxValue
            : (int)Math.Floor(longest * (1.0 - minSimilarity));

        int distance = Distance(a, b, maxDistance);
        if (maxDistance != int.MaxValue && distance > maxDistance) return 0;

        return 1.0 - (double)distance / longest;
    }
}

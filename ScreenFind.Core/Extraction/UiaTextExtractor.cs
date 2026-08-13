using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using ScreenFind.Core.Interop;
using ScreenFind.Core.Models;

namespace ScreenFind.Core.Extraction;

public sealed record UiaOptions
{
    public static readonly UiaOptions Default = new();

    /// <summary>UIA calls into a hung application can block forever — always bound them (spec §6).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Safety stop for pathological documents.</summary>
    public int MaxWords { get; init; } = 20_000;
}

/// <summary>
/// Tier 1 (spec §5.2): exact text with exact coordinates, no OCR, no capture. Browsers, Word and
/// WinUI apps all land here. Everything is wrapped in try/catch because UI Automation throws
/// freely and unpredictably on older applications.
/// </summary>
public sealed class UiaTextExtractor : ITextExtractor
{
    private readonly UiaOptions _options;

    public UiaTextExtractor(UiaOptions? options = null) => _options = options ?? UiaOptions.Default;

    public string Name => "UIA";

    public bool RequiresCapture => false;

    public Task<ExtractedDocument> ExtractAsync(ExtractionContext context,
        CancellationToken cancellationToken = default)
    {
        IntPtr window = context.Window;
        if (window == IntPtr.Zero || !Win32.IsWindow(window))
            return Task.FromResult(ExtractedDocument.Empty);

        // Never call UI Automation from the UI thread: cross-process calls can deadlock.
        return Task.Run(() => Extract(window, cancellationToken), cancellationToken);
    }

    private ExtractedDocument Extract(IntPtr window, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        var words = new List<WordBox>();

        try
        {
            var root = AutomationElement.FromHandle(window);
            if (root is null) return ExtractedDocument.Empty;

            foreach (var element in FindTextElements(root))
            {
                if (cancellationToken.IsCancellationRequested || deadline.Elapsed > _options.Timeout) break;

                try
                {
                    if (element.GetCurrentPattern(TextPattern.Pattern) is not TextPattern pattern) continue;
                    CollectFromPattern(pattern, words, deadline, cancellationToken);
                }
                catch (Exception)
                {
                    // Element vanished or refused the pattern — move on.
                }

                if (words.Count >= _options.MaxWords) break;
            }
        }
        catch (Exception)
        {
            return ExtractedDocument.Empty;
        }

        return words.Count == 0 ? ExtractedDocument.Empty : ExtractedDocument.FromLines(GroupIntoLines(words));
    }

    private static IEnumerable<AutomationElement> FindTextElements(AutomationElement root)
    {
        var found = new List<AutomationElement>();

        try
        {
            if ((bool)root.GetCurrentPropertyValue(AutomationElement.IsTextPatternAvailableProperty))
                found.Add(root);
        }
        catch (Exception) { /* ignore */ }

        try
        {
            var condition = new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true);
            var descendants = root.FindAll(TreeScope.Descendants, condition);
            foreach (AutomationElement element in descendants) found.Add(element);
        }
        catch (Exception) { /* ignore */ }

        return found;
    }

    private void CollectFromPattern(TextPattern pattern, List<WordBox> words,
        Stopwatch deadline, CancellationToken cancellationToken)
    {
        TextPatternRange[] ranges;
        try
        {
            // Only what is on screen — the spec explicitly excludes indexing the whole document.
            ranges = pattern.GetVisibleRanges();
        }
        catch (Exception)
        {
            ranges = new[] { pattern.DocumentRange };
        }

        foreach (var visible in ranges)
        {
            if (cancellationToken.IsCancellationRequested || deadline.Elapsed > _options.Timeout) return;
            CollectWords(visible, words, deadline, cancellationToken);
            if (words.Count >= _options.MaxWords) return;
        }
    }

    private void CollectWords(TextPatternRange visible, List<WordBox> words,
        Stopwatch deadline, CancellationToken cancellationToken)
    {
        TextPatternRange walker;
        try
        {
            walker = visible.Clone();
            walker.ExpandToEnclosingUnit(TextUnit.Word);
        }
        catch (Exception)
        {
            return;
        }

        while (true)
        {
            if (cancellationToken.IsCancellationRequested || deadline.Elapsed > _options.Timeout) return;
            if (words.Count >= _options.MaxWords) return;

            try
            {
                if (walker.CompareEndpoints(TextPatternRangeEndpoint.Start, visible, TextPatternRangeEndpoint.End) >= 0)
                    return;

                string text = walker.GetText(-1);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var bounds = FirstBounds(walker);
                    if (!bounds.IsEmpty)
                    {
                        string trimmed = text.Trim();
                        words.Add(new WordBox(trimmed, bounds, LanguageTags.Detect(trimmed), 1f, ExtractionSource.Uia));
                    }
                }

                if (walker.Move(TextUnit.Word, 1) != 1) return;
            }
            catch (Exception)
            {
                return;
            }
        }
    }

    /// <summary>
    /// GetBoundingRectangles returns one rectangle per visual line the range spans, already in
    /// physical screen coordinates (this process is PerMonitorV2 aware).
    /// </summary>
    private static Rect FirstBounds(TextPatternRange range)
    {
        System.Windows.Rect[] rectangles;
        try
        {
            rectangles = range.GetBoundingRectangles();
        }
        catch (Exception)
        {
            return Rect.Empty;
        }

        foreach (var rectangle in rectangles)
        {
            var rect = new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            if (!rect.IsEmpty) return rect;
        }

        return Rect.Empty;
    }

    /// <summary>Groups words into visual lines so multi-line matches produce one rectangle per line.</summary>
    private static List<IReadOnlyList<WordBox>> GroupIntoLines(List<WordBox> words)
    {
        var lines = new List<IReadOnlyList<WordBox>>();
        var current = new List<WordBox>();
        Rect lineBounds = Rect.Empty;

        foreach (var word in words)
        {
            if (current.Count == 0 || lineBounds.VerticalOverlapRatio(word.ScreenBounds) >= 0.5)
            {
                current.Add(word);
                lineBounds = lineBounds.IsEmpty ? word.ScreenBounds : lineBounds.Union(word.ScreenBounds);
                continue;
            }

            lines.Add(current);
            current = new List<WordBox> { word };
            lineBounds = word.ScreenBounds;
        }

        if (current.Count > 0) lines.Add(current);
        return lines;
    }
}

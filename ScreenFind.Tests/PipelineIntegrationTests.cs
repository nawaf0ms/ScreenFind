using System.IO;
using ScreenFind.Core.Capture;
using ScreenFind.Core.Extraction;
using ScreenFind.Core.Models;
using ScreenFind.Core.Text;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using Xunit;

namespace ScreenFind.Tests;

/// <summary>
/// End-to-end over a real image: capture → preprocess → OCR (ar + en) → merge → coordinates →
/// match. Nothing on the user's screen is touched; the "capture" is a PNG from the phase 0
/// sample set, placed at a fake window origin so the coordinate chain is exercised for real.
/// </summary>
public class PipelineIntegrationTests
{
    /// <summary>A window sitting at (300, 200) on the desktop.</summary>
    private static readonly Rect WindowOrigin = new(300, 200, 0, 0);

    private sealed class FileCaptureService : ICaptureService
    {
        private readonly string _path;

        public FileCaptureService(string path) => _path = path;

        public async Task<CaptureResult?> CaptureWindowAsync(IntPtr hwnd, CancellationToken cancellationToken = default)
        {
            var bitmap = await LoadAsync(_path);
            var bounds = new Rect(WindowOrigin.X, WindowOrigin.Y, bitmap.PixelWidth, bitmap.PixelHeight);
            return new CaptureResult(bitmap, bounds, hwnd, ContentHash.Compute(bitmap));
        }

        public Task<CaptureResult?> CaptureMonitorForWindowAsync(IntPtr hwnd, CancellationToken cancellationToken = default)
            => CaptureWindowAsync(hwnd, cancellationToken);

        public Task<CaptureResult?> CaptureAsync(IntPtr hwnd, CancellationToken cancellationToken = default)
            => CaptureWindowAsync(hwnd, cancellationToken);

        public void Dispose() { }
    }

    private static async Task<SoftwareBitmap> LoadAsync(string path)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path);
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(CryptographicBuffer.CreateFromByteArray(bytes));
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    private static string? SamplePath(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ScreenFind.sln")))
            directory = directory.Parent;

        if (directory is null) return null;
        string path = Path.Combine(directory.FullName, "samples", "synthetic", name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Returns null when the prerequisites are absent (samples not generated, or an OCR language
    /// pack missing). xunit 2 has no dynamic skip, so those tests end without assertions instead
    /// of failing on a machine that is simply not set up.
    /// </summary>
    private static async Task<(ExtractedDocument Document, Rect Bounds)?> ExtractAsync(string sample)
    {
        string? path = SamplePath(sample);
        if (path is null) return null; // run: dotnet run --project ScreenFind.Feasibility -- synth

        var capture = new FileCaptureService(path);
        using var engine = new WindowsOcrEngine();
        if (!engine.IsLanguageAvailable("ar") || !engine.IsLanguageAvailable("en")) return null;

        using var pipeline = new ExtractionPipeline(capture, new ITextExtractor[] { new OcrTextExtractor(engine) });
        var outcome = await pipeline.ExtractAsync(new IntPtr(1));

        using var frame = await capture.CaptureWindowAsync(new IntPtr(1));
        return (outcome.Document, frame!.SourceBounds);
    }

    [Fact]
    public async Task ReadsArabicPageAndPlacesEveryWordInsideTheWindow()
    {
        var extracted = await ExtractAsync("ar_16_clean.png");
        if (extracted is null) return;
        var (document, bounds) = extracted.Value;

        Assert.False(document.IsEmpty);
        Assert.True(document.Words.Count > 40, $"only {document.Words.Count} words recognised");

        // The whole point of CoordinateMapper: undo the ×2 upscale and add the window origin.
        // If either step were missing the boxes would fall outside the window rectangle.
        foreach (var word in document.Words)
        {
            Assert.True(word.ScreenBounds.X >= bounds.X - 2, $"'{word.Text}' left of the window");
            Assert.True(word.ScreenBounds.Y >= bounds.Y - 2, $"'{word.Text}' above the window");
            Assert.True(word.ScreenBounds.Right <= bounds.Right + 2, $"'{word.Text}' right of the window");
            Assert.True(word.ScreenBounds.Bottom <= bounds.Bottom + 2, $"'{word.Text}' below the window");
        }
    }

    [Fact]
    public async Task FindsArabicQueryAndHighlightsTheRightLine()
    {
        var extracted = await ExtractAsync("ar_16_clean.png");
        if (extracted is null) return;
        var (document, bounds) = extracted.Value;
        var searchable = SearchableDocument.Create(document);
        var engine = new MatchEngine();

        // «الطالبة» is on the first line, «الدكتوراه» on the last one.
        var first = engine.Find(searchable, "الطالبة");
        var last = engine.Find(searchable, "الدكتوراه");

        Assert.NotEmpty(first);
        Assert.NotEmpty(last);

        var firstBox = first[0].BoundingBox;
        var lastBox = last[0].BoundingBox;

        Assert.True(firstBox.Y < lastBox.Y, "the first line must be above the last line");
        Assert.True(firstBox.Y >= bounds.Y && firstBox.Bottom <= bounds.Bottom, "highlight outside the window");

        // A word box on a 16px page: tens of pixels wide, not hundreds, and never zero.
        Assert.InRange(firstBox.Width, 10, 400);
        Assert.InRange(firstBox.Height, 8, 80);
    }

    [Fact]
    public async Task ReadsEnglishPageAndMatchesAPhrase()
    {
        var extracted = await ExtractAsync("en_16_clean.png");
        if (extracted is null) return;
        var searchable = SearchableDocument.Create(extracted.Value.Document);

        var matches = new MatchEngine().Find(searchable, "university library");

        var match = Assert.Single(matches);
        Assert.Equal(1f, match.Score);
        Assert.Single(match.Bounds); // both words sit on the same line
    }

    [Fact]
    public async Task SecondExtractionOfTheSamePixelsComesFromCache()
    {
        string? path = SamplePath("en_16_clean.png");
        if (path is null) return;

        var capture = new FileCaptureService(path);
        using var engine = new WindowsOcrEngine();
        if (!engine.IsLanguageAvailable("en")) return;

        using var pipeline = new ExtractionPipeline(capture, new ITextExtractor[] { new OcrTextExtractor(engine) });

        var first = await pipeline.ExtractAsync(new IntPtr(1));
        var second = await pipeline.ExtractAsync(new IntPtr(1));

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(first.Document.Words.Count, second.Document.Words.Count);
    }
}

using System.Diagnostics;
using System.Text;
using ScreenFind.Core.Capture;
using ScreenFind.Core.Extraction;
using ScreenFind.Core.Input;
using ScreenFind.Core.Interop;
using ScreenFind.Core.Models;
using ScreenFind.Core.Text;

namespace ScreenFind.Cli;

/// <summary>
/// Phase 1 (spec §7): hotkey → capture → extract → print words and their screen coordinates.
/// No GUI on purpose — the core has to be proven before any window exists.
/// </summary>
public static class Program
{
    private static readonly MatchEngine Matcher = new();

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var options = CliOptions.Parse(args);
        if (options.ShowHelp) return Help();

        using var capture = new GraphicsCaptureService();
        var ocr = new WindowsOcrEngine();
        var ocrExtractor = new OcrTextExtractor(ocr);

        var tiers = new List<ITextExtractor>();
        if (!options.OcrOnly) tiers.Add(new UiaTextExtractor());
        if (!options.UiaOnly) tiers.Add(ocrExtractor);

        using var pipeline = new ExtractionPipeline(capture, tiers);

        Console.WriteLine("ScreenFind CLI — © 2026 nawaf0ms (https://github.com/nawaf0ms)");
        Console.WriteLine($"Windows.Graphics.Capture supported: {GraphicsCaptureService.IsSupported}");
        Console.WriteLine($"OCR languages: {string.Join(", ", ocr.AvailableLanguages)}");
        foreach (string missing in ocrExtractor.MissingLanguages)
            Console.WriteLine("WARNING: " + OcrLanguageHelp.MissingLanguageMessage(missing));
        Console.WriteLine();

        if (options.DelaySeconds > 0)
        {
            Console.WriteLine($"Switch to the window you want to read — capturing in {options.DelaySeconds}s...");
            Thread.Sleep(TimeSpan.FromSeconds(options.DelaySeconds));
            RunOnce(pipeline, options);
            return 0;
        }

        using var hotkey = new HotkeyListener();
        hotkey.Failed += message => Console.Error.WriteLine(message);
        hotkey.Pressed += () =>
        {
            try { RunOnce(pipeline, options); }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
        };
        hotkey.Start();

        Console.WriteLine($"Press {hotkey.Definition.Display} over any window. Ctrl+C here to quit.");

        var exit = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            exit.Set();
        };
        exit.Wait();
        return 0;
    }

    private static int Help()
    {
        Console.WriteLine("""
            ScreenFind phase 1 console

              (no args)            register Ctrl+Shift+F and dump text on every press
              --once [seconds]     capture the foreground window after a delay (default 4s)
              --search <query>     also run the match engine and print the highlight rectangles
              --words <n>          how many words to print (default 25)
              --uia-only           skip OCR
              --ocr-only           skip UI Automation
            """);
        return 0;
    }

    private static void RunOnce(ExtractionPipeline pipeline, CliOptions options)
    {
        IntPtr window = Win32.GetForegroundWindow();
        string title = Win32.GetWindowTitle(window);
        string className = Win32.GetWindowClass(window);
        var windowBounds = Win32.GetWindowBounds(window);
        var frameBounds = Win32.GetExtendedFrameBounds(window);
        IntPtr monitor = Win32.MonitorFromWindow(window, Win32.MONITOR_DEFAULTTONEAREST);
        double dpiScale = Win32.GetMonitorDpiScale(monitor);

        Console.WriteLine(new string('-', 78));
        Console.WriteLine($"window   : 0x{window.ToInt64():X}  [{className}]  {title}");
        Console.WriteLine($"rect     : {windowBounds}  frame: {frameBounds}");
        Console.WriteLine($"monitor  : {Win32.GetMonitorBounds(monitor)}  dpi scale: {dpiScale:0.##} " +
                          $"({dpiScale * 100:0}%)");

        var stopwatch = Stopwatch.StartNew();
        var outcome = pipeline.ExtractAsync(window).GetAwaiter().GetResult();
        stopwatch.Stop();

        Console.WriteLine($"extractor: {outcome.ExtractorName}{(outcome.FromCache ? " (cached)" : "")}  " +
                          $"words: {outcome.Document.Words.Count}  elapsed: {stopwatch.ElapsedMilliseconds}ms");

        if (outcome.Document.IsEmpty)
        {
            Console.WriteLine("no text extracted");
            return;
        }

        int limit = Math.Min(options.WordLimit, outcome.Document.Words.Count);
        Console.WriteLine($"--- first {limit} words ---");
        for (int i = 0; i < limit; i++)
        {
            var word = outcome.Document.Words[i];
            Console.WriteLine($"  [{i,3}] {word.ScreenBounds,-32} {word.Language,-7} {word.Text}");
        }

        string preview = outcome.Document.RawText.Length > 300
            ? outcome.Document.RawText[..300] + "…"
            : outcome.Document.RawText;
        Console.WriteLine("--- text ---");
        Console.WriteLine(preview);

        if (!string.IsNullOrWhiteSpace(options.Query))
        {
            var searchable = SearchableDocument.Create(outcome.Document);
            var matches = Matcher.Find(searchable, options.Query);
            Console.WriteLine($"--- {matches.Count} match(es) for \"{options.Query}\" ---");
            foreach (var match in matches)
            {
                string text = string.Join(' ', Enumerable
                    .Range(match.StartWordIndex, match.EndWordIndex - match.StartWordIndex + 1)
                    .Select(i => outcome.Document.Words[i].Text));
                Console.WriteLine($"  score {match.Score:0.00}  {string.Join(" | ", match.Bounds)}  {text}");
            }
        }
    }

    private sealed class CliOptions
    {
        public bool ShowHelp { get; private init; }
        public int DelaySeconds { get; private init; }
        public string? Query { get; private init; }
        public int WordLimit { get; private init; } = 25;
        public bool UiaOnly { get; private init; }
        public bool OcrOnly { get; private init; }

        public static CliOptions Parse(string[] args)
        {
            bool help = false, uiaOnly = false, ocrOnly = false;
            int delay = 0, words = 25;
            string? query = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--help" or "-h" or "/?":
                        help = true;
                        break;
                    case "--once":
                        delay = 4;
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int parsed)) delay = parsed;
                        break;
                    case "--search":
                        if (i + 1 < args.Length) query = args[++i];
                        break;
                    case "--words":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int count)) words = count;
                        break;
                    case "--uia-only":
                        uiaOnly = true;
                        break;
                    case "--ocr-only":
                        ocrOnly = true;
                        break;
                }
            }

            return new CliOptions
            {
                ShowHelp = help,
                DelaySeconds = delay,
                Query = query,
                WordLimit = words,
                UiaOnly = uiaOnly,
                OcrOnly = ocrOnly
            };
        }
    }
}

using System.IO;
using System.Text;
using ScreenFind.Core.Extraction;
using ScreenFind.Core.Text;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace ScreenFind.Feasibility;

/// <summary>
/// Phase 0 (spec §7): measure Windows OCR accuracy on Arabic and English before any product
/// code depends on it. Decision gate: Arabic word accuracy below 70% means the engine must be
/// replaced (PaddleOCR/Tesseract) behind the same <see cref="IOcrEngine"/> interface.
/// </summary>
public static class Program
{
    private static readonly (string Name, PreprocessOptions Options)[] Modes =
    {
        ("raw", PreprocessOptions.None),
        ("gray", new PreprocessOptions(Scale: 1.0, Grayscale: true)),
        ("x2-bilinear", new PreprocessOptions(Scale: 2.0, Grayscale: true, Mode: ResampleMode.Bilinear)),
        ("x2-bicubic", new PreprocessOptions(Scale: 2.0, Grayscale: true, Mode: ResampleMode.Bicubic)),
        ("x3-bicubic", new PreprocessOptions(Scale: 3.0, Grayscale: true, Mode: ResampleMode.Bicubic))
    };

    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

        try
        {
            return command switch
            {
                "langs" => ListLanguages(),
                "synth" => Synthesize(args.Length > 1 ? args[1] : DefaultSampleDirectory),
                "run" => RunAsync(args.Length > 1 ? args[1] : DefaultSampleDirectory).GetAwaiter().GetResult(),
                "gate" => GateAsync().GetAwaiter().GetResult(),
                "dump" => DumpAsync(args).GetAwaiter().GetResult(),
                _ => Help()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static string DefaultSampleDirectory =>
        Path.Combine(RepositoryRoot, "samples", "synthetic");

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ScreenFind.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? Environment.CurrentDirectory;
        }
    }

    private static int Help()
    {
        Console.WriteLine("""
            ScreenFind phase 0 — OCR feasibility
            © 2026 nawaf0ms — https://github.com/nawaf0ms

              langs            list installed OCR recognizer languages
              synth [dir]      render synthetic samples with ground truth
              run [dir]        OCR every sample and report accuracy per preprocessing mode
              gate             synth + run, then apply the 70% Arabic decision gate

            Drop real screenshots into the sample directory as <name>.png with a matching
            <name>.txt ground truth; prefix the name with ar_ or en_ to pick the engine.
            """);
        return 0;
    }

    private static int ListLanguages()
    {
        using var engine = new WindowsOcrEngine();
        Console.WriteLine($"Max image dimension: {WindowsOcrEngine.MaxImageDimension}");
        Console.WriteLine("Available recognizer languages:");
        foreach (string tag in engine.AvailableLanguages) Console.WriteLine("  " + tag);

        foreach (string required in new[] { "ar", "en" })
        {
            string? resolved = engine.ResolveTag(required);
            Console.WriteLine(resolved is null
                ? $"  [MISSING] {required} — {OcrLanguageHelp.MissingLanguageMessage(required)}"
                : $"  [ok] {required} -> {resolved}");
        }

        return engine.IsLanguageAvailable("ar") && engine.IsLanguageAvailable("en") ? 0 : 1;
    }

    private static int Synthesize(string directory)
    {
        var files = SampleGenerator.Generate(directory);
        Console.WriteLine($"Wrote {files.Count} samples to {directory}");
        return 0;
    }

    private static async Task<int> GateAsync()
    {
        Synthesize(DefaultSampleDirectory);
        return await RunAsync(DefaultSampleDirectory).ConfigureAwait(false);
    }

    private static async Task<int> RunAsync(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"No such directory: {directory}. Run 'synth' first.");
            return 2;
        }

        using var engine = new WindowsOcrEngine();
        var samples = Directory.GetFiles(directory, "*.png").OrderBy(f => f).ToArray();
        if (samples.Length == 0)
        {
            Console.Error.WriteLine($"No .png samples in {directory}");
            return 2;
        }

        var results = new List<SampleResult>();

        foreach (string imagePath in samples)
        {
            string groundTruthPath = Path.ChangeExtension(imagePath, ".txt");
            if (!File.Exists(groundTruthPath))
            {
                Console.WriteLine($"skip {Path.GetFileName(imagePath)} — no ground truth");
                continue;
            }

            string name = Path.GetFileNameWithoutExtension(imagePath);
            string language = name.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
            string condition = Condition(name);
            string reference = await File.ReadAllTextAsync(groundTruthPath).ConfigureAwait(false);

            using var original = await LoadBitmapAsync(imagePath).ConfigureAwait(false);

            foreach (var (modeName, options) in Modes)
            {
                var started = DateTime.UtcNow;
                using var prepared = ImagePreprocessor.Prepare(original, options);
                var output = await engine.RecognizeAsync(prepared.Bitmap, language).ConfigureAwait(false);
                var elapsed = DateTime.UtcNow - started;

                // Same reading-order reconstruction the product applies (visual -> logical).
                string hypothesis = string.Join("\n", output.Lines.Select(ReadingOrder.ToLogicalText));
                var metrics = Accuracy.Compare(reference, hypothesis);

                results.Add(new SampleResult(name, language, condition, modeName, metrics.Accuracy, metrics.Recall,
                    metrics.ReferenceWords, output.WordCount, elapsed.TotalMilliseconds, prepared.Scale));

                Console.WriteLine(
                    $"{name,-16} {language}  {modeName,-12} acc={metrics.Accuracy,6:P1} recall={metrics.Recall,6:P1} " +
                    $"words={output.WordCount,4}/{metrics.ReferenceWords,-4} {elapsed.TotalMilliseconds,6:F0}ms");
            }
        }

        return Report(results, directory);
    }

    private static int Report(List<SampleResult> results, string directory)
    {
        if (results.Count == 0)
        {
            Console.Error.WriteLine("Nothing measured.");
            return 2;
        }

        var builder = new StringBuilder();
        builder.AppendLine("# Phase 0 — OCR feasibility report");
        builder.AppendLine();
        builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        builder.AppendLine($"Samples: `{directory}`");
        builder.AppendLine();
        builder.AppendLine("Accuracy = 1 − word error rate (insertions/deletions/substitutions over the");
        builder.AppendLine("normalized reference). Recall = share of reference words found somewhere in the");
        builder.AppendLine("output, allowing the same fuzzy tolerance the matcher uses (≥ 0.85 similarity).");
        builder.AppendLine();
        builder.AppendLine("## Summary by language and preprocessing mode");
        builder.AppendLine();
        builder.AppendLine("| language | mode | accuracy | recall | avg ms |");
        builder.AppendLine("|---|---|---|---|---|");

        Console.WriteLine();
        Console.WriteLine("=== by preprocessing mode ===");

        double arabicBest = 0;
        string arabicBestMode = "-";

        foreach (var group in results.GroupBy(r => (r.Language, r.Mode)).OrderBy(g => g.Key.Language).ThenBy(g => g.Key.Mode))
        {
            double accuracy = group.Average(r => r.Accuracy);
            double recall = group.Average(r => r.Recall);
            double milliseconds = group.Average(r => r.Milliseconds);

            builder.AppendLine($"| {group.Key.Language} | {group.Key.Mode} | {accuracy:P1} | {recall:P1} | {milliseconds:F0} |");
            Console.WriteLine($"{group.Key.Language}  {group.Key.Mode,-12} acc={accuracy,6:P1} recall={recall,6:P1} {milliseconds,6:F0}ms");

            if (group.Key.Language == "ar" && accuracy > arabicBest)
            {
                arabicBest = accuracy;
                arabicBestMode = group.Key.Mode;
            }
        }

        // The average across conditions hides the finding that matters: this engine is excellent
        // on rendered text and weak on degraded scans. Report the gate per condition.
        builder.AppendLine();
        builder.AppendLine($"## Accuracy by condition (mode `{arabicBestMode}`)");
        builder.AppendLine();
        builder.AppendLine("| condition | language | accuracy | recall |");
        builder.AppendLine("|---|---|---|---|");

        Console.WriteLine();
        Console.WriteLine($"=== by condition (mode {arabicBestMode}) ===");

        var conditionScores = new Dictionary<(string Condition, string Language), double>();
        foreach (var group in results.Where(r => r.Mode == arabicBestMode)
                                     .GroupBy(r => (r.Condition, r.Language))
                                     .OrderBy(g => g.Key.Condition).ThenBy(g => g.Key.Language))
        {
            double accuracy = group.Average(r => r.Accuracy);
            double recall = group.Average(r => r.Recall);
            conditionScores[group.Key] = accuracy;

            builder.AppendLine($"| {group.Key.Condition} | {group.Key.Language} | {accuracy:P1} | {recall:P1} |");
            Console.WriteLine($"{group.Key.Condition,-6} {group.Key.Language}  acc={accuracy,6:P1} recall={recall,6:P1}");
        }

        const double gate = 0.70;
        double arabicClean = conditionScores.GetValueOrDefault(("clean", "ar"));
        double arabicScan = conditionScores.GetValueOrDefault(("scan", "ar"));
        double arabicHarsh = conditionScores.GetValueOrDefault(("harsh", "ar"));

        bool passed = arabicClean >= gate && arabicScan >= gate;
        string verdict = passed
            ? $"PASS (conditional) — Arabic accuracy is {arabicClean:P1} on rendered screen text and " +
              $"{arabicScan:P1} on scan-like input, both above the 70% gate with mode '{arabicBestMode}'. " +
              $"Keep Windows.Media.Ocr for v1. Degraded input ('harsh': {arabicHarsh:P1}) is below the gate, " +
              "so a second engine stays on the table for photocopies and photographed pages."
            : $"FAIL — Arabic accuracy {arabicClean:P1} (rendered) / {arabicScan:P1} (scan-like) against a 70% gate. " +
              "Replace the engine behind IOcrEngine (PaddleOCR via ONNX Runtime, or Tesseract 5 + ara.traineddata).";

        builder.AppendLine();
        builder.AppendLine("## Decision gate");
        builder.AppendLine();
        builder.AppendLine(verdict);
        builder.AppendLine();
        builder.AppendLine("> These are synthetic samples. Drop real screenshots (scanned Arabic PDF, scanned");
        builder.AppendLine("> English PDF, photographed book, RDP session) into the sample directory as");
        builder.AppendLine("> `ar_*_scan.png` + `.txt` ground truth and re-run to confirm on real data.");
        builder.AppendLine();
        builder.AppendLine("## Per sample");
        builder.AppendLine();
        builder.AppendLine("| sample | language | mode | accuracy | recall | ocr words | ref words | ms |");
        builder.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var result in results.OrderBy(r => r.Name).ThenBy(r => r.Mode))
        {
            builder.AppendLine($"| {result.Name} | {result.Language} | {result.Mode} | {result.Accuracy:P1} | " +
                               $"{result.Recall:P1} | {result.OcrWords} | {result.ReferenceWords} | {result.Milliseconds:F0} |");
        }

        string reportPath = Path.Combine(RepositoryRoot, "docs", "phase0-ocr-feasibility.md");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, builder.ToString(), Encoding.UTF8);

        Console.WriteLine();
        Console.WriteLine(verdict);
        Console.WriteLine($"Report written to {reportPath}");

        return passed ? 0 : 1;
    }

    /// <summary>Prints the raw engine output for one image — the fastest way to see word order and boxes.</summary>
    private static async Task<int> DumpAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: dump <image> [language] [mode]");
            return 2;
        }

        string imagePath = args[1];
        string language = args.Length > 2 ? args[2] : "ar";
        string modeName = args.Length > 3 ? args[3] : "x2-bicubic";
        var options = Modes.FirstOrDefault(m => m.Name == modeName).Options ?? PreprocessOptions.Default;

        using var engine = new WindowsOcrEngine();
        using var bitmap = await LoadBitmapAsync(imagePath).ConfigureAwait(false);
        using var prepared = ImagePreprocessor.Prepare(bitmap, options);

        var output = await engine.RecognizeAsync(prepared.Bitmap, language).ConfigureAwait(false);

        Console.WriteLine($"{output.Lines.Count} lines, {output.WordCount} words (scale {prepared.Scale})");
        foreach (var line in output.Lines)
        {
            Console.WriteLine();
            Console.WriteLine("LINE: " + line.Text);
            foreach (var word in line.Words)
            {
                Console.WriteLine($"   x={word.Bounds.X,7:F0} y={word.Bounds.Y,7:F0} w={word.Bounds.Width,6:F0}  {word.Text}");
            }
        }

        return 0;
    }

    private static async Task<SoftwareBitmap> LoadBitmapAsync(string path)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(CryptographicBuffer.CreateFromByteArray(bytes));
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    private sealed record SampleResult(
        string Name, string Language, string Condition, string Mode,
        double Accuracy, double Recall,
        int ReferenceWords, int OcrWords,
        double Milliseconds, double Scale);

    /// <summary>Sample names are &lt;language&gt;_&lt;size&gt;_&lt;condition&gt;, e.g. ar_16_scan.</summary>
    private static string Condition(string sampleName)
    {
        int separator = sampleName.LastIndexOf('_');
        return separator < 0 ? "unknown" : sampleName[(separator + 1)..].ToLowerInvariant();
    }
}

/// <summary>Word-level accuracy on normalized text — the same normalizer the product uses.</summary>
public static class Accuracy
{
    public sealed record Result(double Accuracy, double Recall, int ReferenceWords);

    public static Result Compare(string reference, string hypothesis)
    {
        var referenceWords = Tokenize(reference);
        var hypothesisWords = Tokenize(hypothesis);

        if (referenceWords.Length == 0) return new Result(0, 0, 0);

        int distance = TokenDistance(referenceWords, hypothesisWords);
        double accuracy = Math.Max(0, 1.0 - distance / (double)referenceWords.Length);

        int found = referenceWords.Count(word => hypothesisWords.Any(candidate =>
            Levenshtein.Similarity(word, candidate, 0.85) >= 0.85));

        return new Result(accuracy, found / (double)referenceWords.Length, referenceWords.Length);
    }

    private static string[] Tokenize(string text)
        => TextNormalizer.NormalizeToString(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static int TokenDistance(string[] reference, string[] hypothesis)
    {
        var previous = new int[hypothesis.Length + 1];
        var current = new int[hypothesis.Length + 1];

        for (int j = 0; j <= hypothesis.Length; j++) previous[j] = j;

        for (int i = 1; i <= reference.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= hypothesis.Length; j++)
            {
                int cost = reference[i - 1] == hypothesis[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[hypothesis.Length];
    }
}

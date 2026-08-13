using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenFind.Feasibility;

/// <summary>
/// Renders synthetic "page on screen" samples with known ground truth, plus degraded variants
/// that stand in for scanned documents (blur, noise, low contrast). Real screenshots are still
/// the authority — these exist so the phase 0 gate can be measured on day one.
/// </summary>
public static class SampleGenerator
{
    private const double PageWidth = 900;
    private const double PageHeight = 620;

    private static readonly string[] ArabicParagraphs =
    {
        "ذهبت الطالبة إلى المكتبة الجامعية في الصباح الباكر لتستعير كتاب النحو والصرف",
        "تعد اللغة العربية من أقدم اللغات الحية وأكثرها انتشارا بين الشعوب في العالم",
        "قال المدرس للطلاب إن الامتحان النهائي سيكون يوم الأحد الموافق ٢٥ من الشهر القادم",
        "المكتبة الوطنية تحتوي على مليون كتاب ومخطوطة نادرة يعود تاريخها إلى القرن الثالث عشر",
        "يجب على الباحث أن يوثق المصادر والمراجع التي استعان بها في إعداد رسالة الدكتوراه"
    };

    private static readonly string[] EnglishParagraphs =
    {
        "The student walked to the university library early in the morning to borrow a grammar book",
        "Optical character recognition converts images of typed or printed text into machine encoded text",
        "The final examination will be held on Sunday the 25th of next month in lecture hall number 4",
        "The national library holds over one million books and rare manuscripts from the thirteenth century",
        "Researchers must document every source and reference used while preparing the doctoral thesis"
    };

    /// <summary>
    /// Three conditions, because they answer different questions:
    /// <list type="bullet">
    /// <item><c>clean</c> — text rendered by the OS: a protected viewer, an RDP session, a legacy app.</item>
    /// <item><c>scan</c> — a decent scanned PDF shown on screen: mild blur, mild noise, slightly grey paper.</item>
    /// <item><c>harsh</c> — a bad photocopy or a phone photo: heavy blur, strong noise, washed out.</item>
    /// </list>
    /// </summary>
    public enum Degradation
    {
        Clean,
        Scan,
        Harsh
    }

    public static IReadOnlyList<string> Generate(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        // Start from a clean slate so renamed samples never linger and skew the averages.
        foreach (string stale in Directory.GetFiles(outputDirectory, "*.png")) File.Delete(stale);
        foreach (string stale in Directory.GetFiles(outputDirectory, "*.txt")) File.Delete(stale);

        var created = new List<string>();

        foreach (double fontSize in new[] { 13.0, 16.0, 21.0 })
        {
            foreach (var degradation in new[] { Degradation.Clean, Degradation.Scan, Degradation.Harsh })
            {
                string suffix = $"{fontSize:00}_{degradation.ToString().ToLowerInvariant()}";
                created.Add(Render(outputDirectory, $"ar_{suffix}", ArabicParagraphs, fontSize,
                    FlowDirection.RightToLeft, "Segoe UI", degradation));
                created.Add(Render(outputDirectory, $"en_{suffix}", EnglishParagraphs, fontSize,
                    FlowDirection.LeftToRight, "Segoe UI", degradation));
            }
        }

        // A serif "book page" look, closer to a photographed book than to a UI.
        created.Add(Render(outputDirectory, "ar_book_scan", ArabicParagraphs, 20, FlowDirection.RightToLeft,
            "Traditional Arabic", Degradation.Scan));
        created.Add(Render(outputDirectory, "en_book_scan", EnglishParagraphs, 20, FlowDirection.LeftToRight,
            "Times New Roman", Degradation.Scan));

        return created;
    }

    private static string Render(string directory, string name, IReadOnlyList<string> paragraphs,
        double fontSize, FlowDirection flow, string fontFamily, Degradation degradation)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, PageWidth, PageHeight));

            double y = 40;
            foreach (string paragraph in paragraphs)
            {
                var text = new FormattedText(
                    paragraph,
                    CultureInfo.GetCultureInfo(flow == FlowDirection.RightToLeft ? "ar-SA" : "en-US"),
                    flow,
                    new Typeface(fontFamily),
                    fontSize,
                    Brushes.Black,
                    1.0)
                {
                    MaxTextWidth = PageWidth - 80,
                    TextAlignment = flow == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left
                };

                // DrawText always anchors the layout box at its top-left corner, whatever the
                // flow direction is; RTL only changes how text is laid out inside that box.
                context.DrawText(text, new Point(40, y));
                y += text.Height + fontSize * 1.2;
            }
        }

        var bitmap = new RenderTargetBitmap((int)PageWidth, (int)PageHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);

        if (degradation != Degradation.Clean) Degrade(pixels, bitmap.PixelWidth, bitmap.PixelHeight, degradation);

        var output = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96,
            PixelFormats.Bgra32, null, pixels, bitmap.PixelWidth * 4);

        string imagePath = Path.Combine(directory, name + ".png");
        using (var stream = File.Create(imagePath))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(output));
            encoder.Save(stream);
        }

        File.WriteAllText(Path.Combine(directory, name + ".txt"),
            string.Join(Environment.NewLine, paragraphs), System.Text.Encoding.UTF8);

        return imagePath;
    }

    /// <summary>Blur + noise + reduced contrast: what a scan or a photographed page looks like.</summary>
    private static void Degrade(byte[] pixels, int width, int height, Degradation degradation)
    {
        // Scan: a 3×3 gaussian (σ≈0.8) — softens edges without dissolving letter dots.
        // Harsh: a flat 3×3 box blur, which is what a bad photocopy does to 13px text.
        int[] kernel = degradation == Degradation.Scan
            ? new[] { 1, 2, 1, 2, 4, 2, 1, 2, 1 }
            : new[] { 1, 1, 1, 1, 1, 1, 1, 1, 1 };
        int kernelWeight = kernel.Sum();

        int noiseAmplitude = degradation == Degradation.Scan ? 7 : 14;
        int black = degradation == Degradation.Scan ? 16 : 28;   // ink is never fully black
        int range = degradation == Degradation.Scan ? 225 : 200;  // paper is never fully white

        var blurred = (byte[])pixels.Clone();
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    int sum = 0, k = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            sum += pixels[((y + dy) * width + (x + dx)) * 4 + channel] * kernel[k++];
                        }
                    }
                    blurred[(y * width + x) * 4 + channel] = (byte)(sum / kernelWeight);
                }
            }
        }

        var random = new Random(1907);
        for (int i = 0; i < blurred.Length; i += 4)
        {
            int noise = random.Next(-noiseAmplitude, noiseAmplitude + 1);
            for (int channel = 0; channel < 3; channel++)
            {
                int value = black + blurred[i + channel] * range / 255 + noise;
                pixels[i + channel] = (byte)Math.Clamp(value, 0, 255);
            }
            pixels[i + 3] = 255;
        }
    }
}

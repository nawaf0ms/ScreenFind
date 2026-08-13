using ScreenFind.Core.Capture;
using Windows.Graphics.Imaging;

namespace ScreenFind.Core.Extraction;

public enum ResampleMode
{
    NearestNeighbor,
    Bilinear,
    Bicubic
}

/// <param name="Scale">Upscale factor. Screen text at 96 DPI is far smaller than the 300 DPI
/// scans OCR engines are trained on, so ×2 is the default (spec §5.3).</param>
/// <param name="Grayscale">Grayscale helps and, unlike binarization, does not hurt Windows OCR.</param>
/// <param name="MaxPixels">Safety valve so a 4K capture does not turn into a 130 MB bitmap.</param>
public sealed record PreprocessOptions(
    double Scale = 2.0,
    bool Grayscale = true,
    ResampleMode Mode = ResampleMode.Bicubic,
    int MaxPixels = 24_000_000,
    int MaxDimension = 10_000)
{
    public static readonly PreprocessOptions Default = new();
    public static readonly PreprocessOptions None = new(Scale: 1.0, Grayscale: false, Mode: ResampleMode.NearestNeighbor);
}

/// <param name="Scale">The factor actually applied — may be lower than requested because of the size caps.</param>
public sealed record PreprocessedImage(SoftwareBitmap Bitmap, double Scale) : IDisposable
{
    public void Dispose() => Bitmap.Dispose();
}

/// <summary>
/// Preprocessing affects OCR accuracy more than anything else (spec §5.3). Grayscale is applied
/// before upscaling: it is the same result as scaling the colour planes, at a quarter of the work.
/// No binarization — it makes Windows.Media.Ocr worse.
/// </summary>
public static class ImagePreprocessor
{
    public static PreprocessedImage Prepare(SoftwareBitmap source, PreprocessOptions? options = null)
    {
        options ??= PreprocessOptions.Default;

        int width = source.PixelWidth;
        int height = source.PixelHeight;
        double scale = ClampScale(options, width, height);

        byte[] bgra = BitmapPixels.ToBgraBytes(source);

        int targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        int targetHeight = Math.Max(1, (int)Math.Round(height * scale));

        byte[] result;
        if (options.Grayscale)
        {
            byte[] gray = ToGrayscale(bgra, width, height);
            byte[] scaled = scale == 1.0
                ? gray
                : Resample(gray, width, height, targetWidth, targetHeight, options.Mode);
            result = GrayToBgra(scaled, targetWidth, targetHeight);
        }
        else if (scale == 1.0)
        {
            result = bgra;
        }
        else
        {
            result = ResampleBgra(bgra, width, height, targetWidth, targetHeight, options.Mode);
        }

        var bitmap = BitmapPixels.FromBgraBytes(result, targetWidth, targetHeight);
        return new PreprocessedImage(bitmap, scale);
    }

    private static double ClampScale(PreprocessOptions options, int width, int height)
    {
        double scale = options.Scale <= 0 ? 1.0 : options.Scale;

        double byDimension = Math.Min(
            options.MaxDimension / (double)Math.Max(width, 1),
            options.MaxDimension / (double)Math.Max(height, 1));

        double byArea = Math.Sqrt(options.MaxPixels / (double)Math.Max(width * (long)height, 1));

        return Math.Max(1.0, Math.Min(scale, Math.Min(byDimension, byArea)));
    }

    public static byte[] ToGrayscale(byte[] bgra, int width, int height)
    {
        var gray = new byte[width * height];
        for (int i = 0, p = 0; i < gray.Length; i++, p += 4)
        {
            // Rec. 601 luma, integer arithmetic.
            int b = bgra[p];
            int g = bgra[p + 1];
            int r = bgra[p + 2];
            gray[i] = (byte)((r * 77 + g * 150 + b * 29) >> 8);
        }
        return gray;
    }

    public static byte[] GrayToBgra(byte[] gray, int width, int height)
    {
        var bgra = new byte[width * height * 4];
        for (int i = 0, p = 0; i < gray.Length; i++, p += 4)
        {
            byte value = gray[i];
            bgra[p] = value;
            bgra[p + 1] = value;
            bgra[p + 2] = value;
            bgra[p + 3] = 255;
        }
        return bgra;
    }

    /// <summary>Separable resampling of a single channel: horizontal pass, then vertical pass.</summary>
    public static byte[] Resample(byte[] source, int sourceWidth, int sourceHeight,
        int targetWidth, int targetHeight, ResampleMode mode)
    {
        var horizontal = new byte[targetWidth * sourceHeight];
        ResamplePass(source, sourceWidth, sourceHeight, horizontal, targetWidth, sourceHeight, mode, horizontalPass: true);

        var vertical = new byte[targetWidth * targetHeight];
        ResamplePass(horizontal, targetWidth, sourceHeight, vertical, targetWidth, targetHeight, mode, horizontalPass: false);

        return vertical;
    }

    private static void ResamplePass(byte[] source, int sourceWidth, int sourceHeight,
        byte[] target, int targetWidth, int targetHeight, ResampleMode mode, bool horizontalPass)
    {
        int outerCount = horizontalPass ? sourceHeight : targetWidth;
        int innerCount = horizontalPass ? targetWidth : targetHeight;
        int sourceCount = horizontalPass ? sourceWidth : sourceHeight;
        double ratio = sourceCount / (double)innerCount;

        for (int outer = 0; outer < outerCount; outer++)
        {
            for (int inner = 0; inner < innerCount; inner++)
            {
                double sourcePosition = (inner + 0.5) * ratio - 0.5;
                int index = horizontalPass ? outer * targetWidth + inner : inner * targetWidth + outer;
                target[index] = mode switch
                {
                    ResampleMode.NearestNeighbor => SampleNearest(source, sourcePosition, outer, sourceCount, sourceWidth, horizontalPass),
                    ResampleMode.Bilinear => SampleBilinear(source, sourcePosition, outer, sourceCount, sourceWidth, horizontalPass),
                    _ => SampleCubic(source, sourcePosition, outer, sourceCount, sourceWidth, horizontalPass)
                };
            }
        }
    }

    private static byte Read(byte[] source, int position, int outer, int count, int stride, bool horizontalPass)
    {
        if (position < 0) position = 0;
        else if (position >= count) position = count - 1;

        return horizontalPass
            ? source[outer * stride + position]
            : source[position * stride + outer];
    }

    private static byte SampleNearest(byte[] source, double position, int outer, int count, int stride, bool horizontalPass)
        => Read(source, (int)Math.Round(position), outer, count, stride, horizontalPass);

    private static byte SampleBilinear(byte[] source, double position, int outer, int count, int stride, bool horizontalPass)
    {
        int left = (int)Math.Floor(position);
        double t = position - left;
        double a = Read(source, left, outer, count, stride, horizontalPass);
        double b = Read(source, left + 1, outer, count, stride, horizontalPass);
        return Clamp(a + (b - a) * t);
    }

    /// <summary>Catmull-Rom cubic — the "bicubic" the spec asks for, in its separable form.</summary>
    private static byte SampleCubic(byte[] source, double position, int outer, int count, int stride, bool horizontalPass)
    {
        int center = (int)Math.Floor(position);
        double t = position - center;

        double p0 = Read(source, center - 1, outer, count, stride, horizontalPass);
        double p1 = Read(source, center, outer, count, stride, horizontalPass);
        double p2 = Read(source, center + 1, outer, count, stride, horizontalPass);
        double p3 = Read(source, center + 2, outer, count, stride, horizontalPass);

        double value = 0.5 * (
            2 * p1 +
            (-p0 + p2) * t +
            (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t +
            (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);

        return Clamp(value);
    }

    private static byte Clamp(double value)
        => value <= 0 ? (byte)0 : value >= 255 ? (byte)255 : (byte)(value + 0.5);

    /// <summary>Colour path — only used when grayscale is disabled (comparison runs).</summary>
    public static byte[] ResampleBgra(byte[] source, int sourceWidth, int sourceHeight,
        int targetWidth, int targetHeight, ResampleMode mode)
    {
        var planes = new byte[4][];
        for (int channel = 0; channel < 4; channel++)
        {
            var plane = new byte[sourceWidth * sourceHeight];
            for (int i = 0; i < plane.Length; i++) plane[i] = source[i * 4 + channel];
            planes[channel] = Resample(plane, sourceWidth, sourceHeight, targetWidth, targetHeight, mode);
        }

        var result = new byte[targetWidth * targetHeight * 4];
        for (int i = 0; i < targetWidth * targetHeight; i++)
        {
            result[i * 4] = planes[0][i];
            result[i * 4 + 1] = planes[1][i];
            result[i * 4 + 2] = planes[2][i];
            result[i * 4 + 3] = planes[3][i];
        }
        return result;
    }
}

using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;

namespace ScreenFind.Core.Capture;

/// <summary>
/// Safe pixel access for <see cref="SoftwareBitmap"/>. Uses the WinRT buffer APIs instead of
/// IMemoryBufferByteAccess so no unsafe COM casts are needed; the extra copy costs a couple of
/// milliseconds on a 1080p frame, which the OCR budget can absorb.
/// </summary>
public static class BitmapPixels
{
    public const int BytesPerPixel = 4;

    /// <summary>Returns tightly packed BGRA8 bytes, converting the bitmap first if needed.</summary>
    public static byte[] ToBgraBytes(SoftwareBitmap bitmap)
    {
        SoftwareBitmap source = EnsureBgra8(bitmap, out bool converted);
        try
        {
            uint size = (uint)(source.PixelWidth * source.PixelHeight * BytesPerPixel);
            var buffer = new Windows.Storage.Streams.Buffer(size);
            source.CopyToBuffer(buffer);
            CryptographicBuffer.CopyToByteArray(buffer, out byte[] bytes);
            return bytes;
        }
        finally
        {
            if (converted) source.Dispose();
        }
    }

    public static SoftwareBitmap FromBgraBytes(byte[] bytes, int width, int height)
    {
        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(bytes));
        return bitmap;
    }

    /// <summary>OCR and the preprocessing pipeline both require BGRA8.</summary>
    public static SoftwareBitmap EnsureBgra8(SoftwareBitmap bitmap, out bool converted)
    {
        if (bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 &&
            bitmap.BitmapAlphaMode != BitmapAlphaMode.Straight)
        {
            converted = false;
            return bitmap;
        }

        converted = true;
        return SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}

/// <summary>Cheap, allocation-free-ish content hash used to skip redundant OCR runs (spec §5.1).</summary>
public static class ContentHash
{
    public static ulong Compute(SoftwareBitmap bitmap)
    {
        try
        {
            return Compute(BitmapPixels.ToBgraBytes(bitmap), bitmap.PixelWidth, bitmap.PixelHeight);
        }
        catch (Exception)
        {
            return 0; // hashing is an optimisation, never a correctness requirement
        }
    }

    public static ulong Compute(ReadOnlySpan<byte> data, int width, int height)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;

        ulong hash = offsetBasis;
        hash = (hash ^ (uint)width) * prime;
        hash = (hash ^ (uint)height) * prime;

        var words = MemoryMarshal.Cast<byte, ulong>(data);
        for (int i = 0; i < words.Length; i++)
        {
            hash = (hash ^ words[i]) * prime;
        }

        // Tail bytes that did not fit into a ulong.
        for (int i = words.Length * sizeof(ulong); i < data.Length; i++)
        {
            hash = (hash ^ data[i]) * prime;
        }

        return hash;
    }
}

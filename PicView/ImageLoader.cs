using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PicView;

/// <summary>
/// Result of decoding one file. Exactly one of <see cref="Bitmap"/> / <see cref="Error"/> is set.
/// </summary>
internal sealed record LoadedImage(BitmapSource? Bitmap, string InfoText, string? Error, DateTime LastWriteUtc);

/// <summary>
/// Decodes images off the UI thread, fully, with EXIF orientation applied.
/// </summary>
internal static class ImageLoader
{
    public static LoadedImage Load(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            var lastWrite = fileInfo.LastWriteTimeUtc;

            BitmapSource source;
            ushort orientation;

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);

                // .ico files hold several sizes; show the largest. Everything else: the first frame.
                var frame = decoder is IconBitmapDecoder
                    ? decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).First()
                    : decoder.Frames[0];

                orientation = ReadOrientation(frame);
                source = frame;
            }

            source = ApplyOrientation(source, orientation);

            // Force the pixels to be decoded (and rotated) now, on this background thread,
            // rather than lazily on the UI/render thread the first time the image is drawn.
            var bitmap = new CachedBitmap(source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            bitmap.Freeze();

            var info = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} px | {FormatFileSize(fileInfo.Length)} | {fileInfo.LastWriteTime:g}";
            return new LoadedImage(bitmap, info, null, lastWrite);
        }
        catch (Exception ex)
        {
            return new LoadedImage(null, "", $"Can't display this file\n{ex.Message}", SafeLastWriteUtc(path));
        }
    }

    /// <summary>EXIF orientation tag (1–8), or 1 if absent or unreadable.</summary>
    private static ushort ReadOrientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata
                && metadata.GetQuery("System.Photo.Orientation") is ushort value
                && value is >= 1 and <= 8)
            {
                return value;
            }
        }
        catch
        {
            // Formats without metadata support (BMP, some PNGs, ...) throw here
        }
        return 1;
    }

    /// <summary>
    /// Turns the stored pixels upright. EXIF values:
    /// 1 normal, 2 mirror, 3 rotate 180, 4 flip vertical,
    /// 5 mirror + rotate 270 CW, 6 rotate 90 CW, 7 mirror + rotate 90 CW, 8 rotate 270 CW.
    /// </summary>
    internal static BitmapSource ApplyOrientation(BitmapSource source, ushort orientation)
    {
        if (orientation <= 1 || orientation > 8) return source;

        bool mirror = orientation is 2 or 4 or 5 or 7;
        double angle = orientation switch
        {
            3 or 4 => 180,
            6 or 7 => 90,
            5 or 8 => 270,
            _ => 0
        };

        var transform = new TransformGroup();
        if (mirror) transform.Children.Add(new ScaleTransform(-1, 1));
        if (angle != 0) transform.Children.Add(new RotateTransform(angle));

        return new TransformedBitmap(source, transform);
    }

    internal static DateTime SafeLastWriteUtc(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

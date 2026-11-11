using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EarthWatch3D.Core.Globe;

namespace EarthWatch3D.App.Rendering;

public static class ImageConversions
{
    /// <summary>Decodes a JPEG/PNG into straight BGRA. Safe to call off the UI thread.</summary>
    public static Bgra32Image DecodeToBgra32(Stream jpegOrPng)
    {
        var decoder = BitmapDecoder.Create(jpegOrPng, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        int w = converted.PixelWidth, h = converted.PixelHeight;
        var pixels = new byte[checked(w * h * 4)];
        converted.CopyPixels(pixels, w * 4, 0);
        return new Bgra32Image(w, h, pixels);
    }

    public static WriteableBitmap CreateBitmap(int w, int h) =>
        new(w, h, 96, 96, PixelFormats.Bgra32, null);

    /// <summary>Copies the image into the bitmap. UI thread only.</summary>
    public static void CopyInto(Bgra32Image src, WriteableBitmap dst)
    {
        if (src.Width != dst.PixelWidth || src.Height != dst.PixelHeight)
            throw new ArgumentException("Image and bitmap sizes differ.");
        dst.WritePixels(new Int32Rect(0, 0, src.Width, src.Height), src.Pixels, src.Width * 4, 0);
    }
}

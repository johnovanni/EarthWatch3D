namespace EarthWatch3D.Core.Globe;

/// <summary>Straight (not premultiplied) BGRA image; rows top to bottom, stride = 4 * width.</summary>
public sealed class Bgra32Image
{
    public Bgra32Image(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("Pixel buffer length must be 4 * width * height.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public static Bgra32Image Create(int w, int h) => new(w, h, new byte[checked(w * h * 4)]);
}

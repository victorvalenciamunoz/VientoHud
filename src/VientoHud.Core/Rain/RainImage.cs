namespace VientoHud.Core.Rain;

/// <summary>An RGBA picture, row by row from the top, 4 bytes per pixel. Immutable.</summary>
public sealed class RgbaImage
{
    private readonly byte[] _pixels;

    public RgbaImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != width * height * 4)
        {
            throw new ArgumentException($"Expected {width * height * 4} bytes, got {pixels.Length}.", nameof(pixels));
        }
        Width = width;
        Height = height;
        _pixels = (byte[])pixels.Clone();
    }

    public int Width { get; }
    public int Height { get; }

    public (byte R, byte G, byte B, byte A) this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            {
                throw new ArgumentOutOfRangeException(x < 0 || x >= Width ? nameof(x) : nameof(y));
            }
            var i = (y * Width + x) * 4;
            return (_pixels[i], _pixels[i + 1], _pixels[i + 2], _pixels[i + 3]);
        }
    }

    /// <summary>A copy of the pixels.</summary>
    public byte[] ToArray() => (byte[])_pixels.Clone();
}

/// <summary>
/// Draws a rain grid with the colours of AEMET's radar maps, so the two can be compared by eye: north up, black where
/// it is dry, grey where there is no data, rings every 50 km around the mark and a white cross on it.
/// </summary>
public static class RainImage
{
    /// <summary>
    /// AEMET's legend (sampled from its radar images, 2026-10-16): one colour per 6 dBZ from 12 dBZ. Rates are turned
    /// back to dBZ with Marshall-Palmer; as they are capped at 55 dBZ, nothing is drawn above the 54-60 dBZ colour.
    /// </summary>
    public static readonly IReadOnlyList<(double FromDbz, byte R, byte G, byte B)> Legend =
    [
        (12, 0, 0, 252), (18, 0, 148, 252), (24, 0, 252, 252), (30, 67, 131, 35), (36, 0, 192, 0), (42, 0, 255, 0),
        (48, 255, 255, 0), (54, 255, 187, 0), (60, 255, 127, 0), (66, 255, 0, 0), (72, 200, 0, 90),
    ];

    public const double RingSpacingKm = 50;

    private static readonly (byte R, byte G, byte B) Dry = (0, 0, 0);
    private static readonly (byte R, byte G, byte B) NoData = (127, 127, 127);
    private static readonly (byte R, byte G, byte B) Ring = (90, 90, 90);
    private static readonly (byte R, byte G, byte B) Mark = (255, 255, 255);

    /// <param name="pixelsPerKm">Size of the picture: pixels per km (cells of 1 km at 2 pixels per km are 2 × 2).</param>
    public static RgbaImage Render(RainGrid grid, Coordinate mark, double pixelsPerKm = 2)
    {
        if (!double.IsFinite(pixelsPerKm) || pixelsPerKm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerKm), pixelsPerKm, "Scale must be positive.");
        }
        var scale = Math.Max(1, (int)Math.Round(grid.CellSizeMeters / 1000 * pixelsPerKm));
        int width = grid.Columns * scale, height = grid.Rows * scale;
        var pixels = new byte[width * height * 4];
        var metresPerPixel = grid.CellSizeMeters / scale;
        var (markX, markY) = grid.Projection.Forward(mark);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = ColorOf(grid[y / scale, x / scale]);
                if (color == Dry || color == NoData)
                {
                    // Rings only over empty cells, so they never hide rain. Distances in the projection are within
                    // ~2 % of true distances across Spain.
                    var dx = grid.Left + (x + 0.5) * metresPerPixel - markX;
                    var dy = grid.Top - (y + 0.5) * metresPerPixel - markY;
                    var km = Math.Sqrt(dx * dx + dy * dy) / 1000;
                    var nearestRing = Math.Round(km / RingSpacingKm) * RingSpacingKm;
                    if (nearestRing > 0 && Math.Abs(km - nearestRing) * 1000 < metresPerPixel / 2)
                    {
                        color = Ring;
                    }
                }
                Put(pixels, width, x, y, color);
            }
        }

        // Cross: 7 pixels each way, with a black outline so it shows over yellow and cyan too.
        var cx = (int)Math.Floor((markX - grid.Left) / metresPerPixel);
        var cy = (int)Math.Floor((grid.Top - markY) / metresPerPixel);
        const int arm = 7;
        foreach (var (offset, color) in new[] { (1, Dry), (-1, Dry), (0, Mark) })
        {
            for (var i = -arm; i <= arm; i++)
            {
                PutIfInside(pixels, width, height, cx + i, cy + offset, color);
                PutIfInside(pixels, width, height, cx + offset, cy + i, color);
            }
        }

        return new RgbaImage(width, height, pixels);
    }

    /// <summary>Colour of a rain rate in mm/h: AEMET's legend, black if dry (below 12 dBZ), grey if no data.</summary>
    public static (byte R, byte G, byte B) ColorOf(float mmPerHour)
    {
        if (float.IsNaN(mmPerHour))
        {
            return NoData;
        }
        if (mmPerHour <= 0)
        {
            return Dry;
        }
        var dbz = Reflectivity.FromRainRate(mmPerHour);
        var color = Dry;
        foreach (var (fromDbz, r, g, b) in Legend)
        {
            // A small tolerance: 12 dBZ turned into a rate and back must land in its own colour.
            if (dbz >= fromDbz - 1e-6)
            {
                color = (r, g, b);
            }
        }
        return color;
    }

    private static void PutIfInside(byte[] pixels, int width, int height, int x, int y, (byte R, byte G, byte B) color)
    {
        if (x >= 0 && x < width && y >= 0 && y < height)
        {
            Put(pixels, width, x, y, color);
        }
    }

    private static void Put(byte[] pixels, int width, int x, int y, (byte R, byte G, byte B) color)
    {
        var i = (y * width + x) * 4;
        pixels[i] = color.R;
        pixels[i + 1] = color.G;
        pixels[i + 2] = color.B;
        pixels[i + 3] = 255;
    }
}

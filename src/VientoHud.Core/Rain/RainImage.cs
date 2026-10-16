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
/// it is dry, grey where there is no data, rings every 50 km around the mark and a white cross on it. With a motion, an
/// arrow from the mark to where the rain over it will be in an hour (<see cref="ArrowHours"/>).
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

    /// <summary>The arrow is as long as the way the rain travels in this many hours.</summary>
    public const double ArrowHours = 1;

    private static readonly (byte R, byte G, byte B) Dry = (0, 0, 0);
    private static readonly (byte R, byte G, byte B) NoData = (127, 127, 127);
    private static readonly (byte R, byte G, byte B) Ring = (90, 90, 90);
    private static readonly (byte R, byte G, byte B) Mark = (255, 255, 255);

    /// <param name="pixelsPerKm">Size of the picture: pixels per km (cells of 1 km at 2 pixels per km are 2 × 2).</param>
    public static RgbaImage Render(RainGrid grid, Coordinate mark, double pixelsPerKm = 2, RainMotion? motion = null)
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

        if (motion is not null)
        {
            // Pixels per km/h: x towards the east, y towards the south.
            var pixelsPerKmh = ArrowHours * 1000 / metresPerPixel;
            double tipX = cx + motion.EastKmh * pixelsPerKmh, tipY = cy - motion.NorthKmh * pixelsPerKmh;
            var angle = Math.Atan2(tipY - cy, tipX - cx);
            var head = Math.Min(10, Math.Sqrt((tipX - cx) * (tipX - cx) + (tipY - cy) * (tipY - cy)) / 2);
            (double X, double Y)[] wings =
            [
                (tipX - head * Math.Cos(angle - 0.5), tipY - head * Math.Sin(angle - 0.5)),
                (tipX - head * Math.Cos(angle + 0.5), tipY - head * Math.Sin(angle + 0.5)),
            ];
            // Black under white, one pixel wider, so the arrow shows over any colour.
            foreach (var (thickness, color) in new[] { (1, Dry), (0, Mark) })
            {
                Line(pixels, width, height, cx, cy, tipX, tipY, thickness, color);
                foreach (var (wx, wy) in wings)
                {
                    Line(pixels, width, height, tipX, tipY, wx, wy, thickness, color);
                }
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

    private static void Line(
        byte[] pixels, int width, int height, double x0, double y0, double x1, double y1, int thickness,
        (byte R, byte G, byte B) color)
    {
        var steps = (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0))) + 1;
        for (var i = 0; i <= steps; i++)
        {
            var x = (int)Math.Round(x0 + (x1 - x0) * i / steps);
            var y = (int)Math.Round(y0 + (y1 - y0) * i / steps);
            for (var dy = -thickness; dy <= thickness; dy++)
            {
                for (var dx = -thickness; dx <= thickness; dx++)
                {
                    PutIfInside(pixels, width, height, x + dx, y + dy, color);
                }
            }
        }
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

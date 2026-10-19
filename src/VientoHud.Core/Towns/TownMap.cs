using VientoHud.Core.Routing;

namespace VientoHud.Core.Towns;

/// <summary>Settings for <see cref="TownMap"/>.</summary>
public sealed record TownMapOptions
{
    /// <summary>A cell is built-up from this share of built ground (0.15: about 1,000 m² in a 90 m cell).</summary>
    public double MinBuiltShare { get; init; } = 0.15;

    /// <summary>
    /// Gaps between built-up cells up to twice this many cells (a street, a park, a strip of fields) are filled, so the
    /// blocks of a town join into one.
    /// </summary>
    public int JoinCells { get; init; } = 1;

    /// <summary>Towns smaller than this (a farm, a lone warehouse) are left out.</summary>
    public double MinAreaKm2 { get; init; } = 0.2;

    /// <summary>Outlines are simplified while staying within this distance of the cell edges.</summary>
    public double SimplifyKm { get; init; } = 0.05;
}

/// <summary>A town: its outline, a closed ring of corners (the first is not repeated at the end), and its area.</summary>
public sealed record Town(IReadOnlyList<Coordinate> Outline, double AreaKm2);

/// <summary>
/// Towns of a built-up map (fase 6, step 6e; DECISIONS 045): built-up cells are joined across small gaps, holes inside a
/// town are filled (a park in the middle is still town), small spots are left out, and each town gets a simplified
/// outline.
/// </summary>
public sealed class TownMap
{
    private readonly BuiltUpGrid _grid;
    private readonly bool[] _inTown;

    private TownMap(BuiltUpGrid grid, bool[] inTown, IReadOnlyList<Town> towns)
    {
        _grid = grid;
        _inTown = inTown;
        Towns = towns;
    }

    /// <summary>The towns, largest first.</summary>
    public IReadOnlyList<Town> Towns { get; }

    /// <summary>Whether a position is in a town (false outside the map).</summary>
    public bool Contains(Coordinate position) =>
        _grid.CellOf(position) is var (column, row) && _inTown[row * _grid.Width + column];

    public static TownMap From(BuiltUpGrid grid, TownMapOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        options ??= new TownMapOptions();
        int width = grid.Width, height = grid.Height;

        var mask = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                mask[y * width + x] = grid[x, y] >= options.MinBuiltShare;
            }
        }
        if (options.JoinCells > 0)
        {
            // Closing: grow every town by the join distance and shrink it back; gaps narrower than twice it stay filled.
            mask = Grow(mask, width, height, options.JoinCells, value: true);
            mask = Grow(mask, width, height, options.JoinCells, value: false);
        }
        FillHoles(mask, width, height);

        var label = new int[width * height];
        var towns = new List<Town>();
        var next = 0;
        for (var start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || label[start] != 0)
            {
                continue;
            }
            next++;
            var cells = Flood(mask, label, width, height, start, next);
            var area = cells.Sum(c => grid.CellAreaKm2(c / width));
            if (area < options.MinAreaKm2)
            {
                foreach (var c in cells)
                {
                    mask[c] = false;
                    label[c] = -1;
                }
                continue;
            }
            foreach (var ring in Outlines(label, width, height, next, cells))
            {
                towns.Add(new Town(Simplify(grid, ring, options.SimplifyKm), area));
            }
        }
        return new TownMap(grid, mask, towns.OrderByDescending(t => t.AreaKm2).ToList());
    }

    // Sets to `value` every cell within `radius` cells (square) of a cell that already has it.
    private static bool[] Grow(bool[] mask, int width, int height, int radius, bool value)
    {
        // Rows first, then columns: a square of side 2·radius + 1. Outside the map counts as not built when growing and
        // as built when shrinking, so towns at the edge are not cut back.
        var rows = new bool[mask.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var hit = false;
                for (var dx = -radius; dx <= radius && !hit; dx++)
                {
                    var nx = x + dx;
                    hit = nx >= 0 && nx < width && mask[y * width + nx] == value;
                }
                rows[y * width + x] = hit ? value : !value;
            }
        }
        var result = new bool[mask.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var hit = false;
                for (var dy = -radius; dy <= radius && !hit; dy++)
                {
                    var ny = y + dy;
                    hit = ny >= 0 && ny < height && rows[ny * width + x] == value;
                }
                result[y * width + x] = hit ? value : !value;
            }
        }
        return result;
    }

    // Open ground not reachable from the edge of the map (side by side, not corner to corner) is inside a town.
    private static void FillHoles(bool[] mask, int width, int height)
    {
        var outside = new bool[mask.Length];
        var queue = new Queue<int>();
        for (var x = 0; x < width; x++)
        {
            Seed(x);
            Seed((height - 1) * width + x);
        }
        for (var y = 0; y < height; y++)
        {
            Seed(y * width);
            Seed(y * width + width - 1);
        }
        while (queue.TryDequeue(out var cell))
        {
            int x = cell % width, y = cell / width;
            if (x > 0) Seed(cell - 1);
            if (x < width - 1) Seed(cell + 1);
            if (y > 0) Seed(cell - width);
            if (y < height - 1) Seed(cell + width);
        }
        for (var i = 0; i < mask.Length; i++)
        {
            mask[i] |= !outside[i];
        }

        void Seed(int cell)
        {
            if (!mask[cell] && !outside[cell])
            {
                outside[cell] = true;
                queue.Enqueue(cell);
            }
        }
    }

    // The cells of the town that holds `start` (side by side), labelled `id`.
    private static List<int> Flood(bool[] mask, int[] label, int width, int height, int start, int id)
    {
        var cells = new List<int>();
        var stack = new Stack<int>();
        label[start] = id;
        stack.Push(start);
        while (stack.TryPop(out var cell))
        {
            cells.Add(cell);
            int x = cell % width, y = cell / width;
            Visit(x > 0 ? cell - 1 : -1);
            Visit(x < width - 1 ? cell + 1 : -1);
            Visit(y > 0 ? cell - width : -1);
            Visit(y < height - 1 ? cell + width : -1);
        }
        return cells;

        void Visit(int cell)
        {
            if (cell >= 0 && mask[cell] && label[cell] == 0)
            {
                label[cell] = id;
                stack.Push(cell);
            }
        }
    }

    // The outer rings of a town, as corners of the cell grid (column, row), clockwise as seen on a north-up map. Every
    // cell side facing a cell of another town or open ground is an edge; edges are chained, turning right first where
    // two rings touch at a corner, so each ring is simple. Holes were filled, so all rings are outer ones.
    private static IEnumerable<List<(int X, int Y)>> Outlines(int[] label, int width, int height, int id, List<int> cells)
    {
        bool In(int x, int y) => x >= 0 && x < width && y >= 0 && y < height && label[y * width + x] == id;

        var edges = new Dictionary<(int X, int Y), List<(int X, int Y)>>();
        void Edge((int, int) from, (int, int) to)
        {
            if (!edges.TryGetValue(from, out var list))
            {
                edges[from] = list = [];
            }
            list.Add(to);
        }
        foreach (var cell in cells)
        {
            int x = cell % width, y = cell / width;
            if (!In(x, y - 1)) Edge((x, y), (x + 1, y));
            if (!In(x + 1, y)) Edge((x + 1, y), (x + 1, y + 1));
            if (!In(x, y + 1)) Edge((x + 1, y + 1), (x, y + 1));
            if (!In(x - 1, y)) Edge((x, y + 1), (x, y));
        }

        while (edges.Count > 0)
        {
            var start = edges.Keys.Min();
            var ring = new List<(int X, int Y)> { start };
            var at = start;
            (int X, int Y) heading = (0, 0);
            while (true)
            {
                var options = edges[at];
                var to = options.Count == 1 ? options[0] : options.MinBy(o => TurnOrder(heading, (o.X - at.X, o.Y - at.Y)));
                options.Remove(to);
                if (options.Count == 0)
                {
                    edges.Remove(at);
                }
                heading = (to.X - at.X, to.Y - at.Y);
                at = to;
                if (at == start)
                {
                    break;
                }
                ring.Add(at);
            }
            yield return DropStraightCorners(ring);
        }
    }

    // 0 for a right turn (on a map with rows going down: east → south), 1 straight on, 2 left.
    private static int TurnOrder((int X, int Y) heading, (int X, int Y) next) =>
        next == (-heading.Y, heading.X) ? 0 : next == heading ? 1 : 2;

    private static List<(int X, int Y)> DropStraightCorners(List<(int X, int Y)> ring)
    {
        var kept = new List<(int X, int Y)>();
        for (var i = 0; i < ring.Count; i++)
        {
            var before = ring[(i + ring.Count - 1) % ring.Count];
            var corner = ring[i];
            var after = ring[(i + 1) % ring.Count];
            var straight = (corner.X - before.X) * (after.Y - corner.Y) == (corner.Y - before.Y) * (after.X - corner.X);
            if (!straight)
            {
                kept.Add(corner);
            }
        }
        return kept;
    }

    // Douglas-Peucker on a closed ring, in km on a local map; split at the corner farthest from the first.
    private static List<Coordinate> Simplify(BuiltUpGrid grid, List<(int X, int Y)> ring, double toleranceKm)
    {
        var corners = ring.Select(c => grid.Corner(c.X, c.Y)).ToList();
        if (corners.Count <= 4 || toleranceKm <= 0)
        {
            return corners;
        }
        var projection = new LocalProjection(corners[0]);
        var points = corners.Select(projection.Project).ToList();
        var far = Enumerable.Range(1, points.Count - 1).MaxBy(i => points[0].DistanceTo(points[i]));
        var keep = new bool[points.Count];
        keep[0] = keep[far] = true;
        // Stretches still to look at (b may be the ring's end, meaning corner 0); a stack, as rings can be long.
        var stretches = new Stack<(int A, int B)>([(0, far), (far, points.Count)]);
        while (stretches.TryPop(out var stretch))
        {
            var (a, b) = stretch;
            var end = points[b % points.Count];
            var best = -1;
            var bestDistance = toleranceKm;
            for (var i = a + 1; i < b; i++)
            {
                var distance = points[i].DistanceToSegment(points[a], end);
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            if (best >= 0)
            {
                keep[best] = true;
                stretches.Push((a, best));
                stretches.Push((best, b));
            }
        }
        var result = corners.Where((_, i) => keep[i]).ToList();
        return result.Count >= 3 ? result : corners;
    }
}

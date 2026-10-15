namespace AVASarge.Engine;

/// <summary>
/// A battlefield: open ground broken up by boulders, deep ruts and the shells of ruined buildings. It's laid
/// out the same on the left as on the right, so a duel is fair, and every spot where a tank rolls out can
/// reach every other.
/// </summary>
public sealed class Battlefield
{
    public const int Cols = Field.Cols, Rows = Field.Rows, WallHp = 3;
    public readonly Ground[,] Cells = new Ground[Cols, Rows];
    public readonly int[,] Hp = new int[Cols, Rows];

    /// <summary>Where tanks roll out: player one on the left, player two on the right, and the enemy's three on the right.</summary>
    public static readonly (int C, int R) LeftSpawn = (2, 11), RightSpawn = (29, 11);
    public static readonly (int C, int R)[] EnemySpawns = [(29, 11), (29, 2), (29, 19)];

    public static bool Inside(int c, int r) => c >= 0 && c < Cols && r >= 0 && r < Rows;
    public Ground At(int c, int r) => Inside(c, r) ? Cells[c, r] : Ground.Boulder;

    /// <summary>Whether a tank can be in a cell.</summary>
    public bool Drivable(int c, int r) => At(c, r) is Ground.Open or Ground.Rubble;

    /// <summary>Whether a shell is stopped by a cell.</summary>
    public bool Stops(int c, int r) => At(c, r) is Ground.Boulder or Ground.Wall;

    public int[,] Distances(int c, int r)
    {
        var d = new int[Cols, Rows];
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                d[x, y] = -1;
        if (!Drivable(c, r)) return d;
        var q = new Queue<(int, int)>();
        d[c, r] = 0;
        q.Enqueue((c, r));
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (!Inside(nx, ny) || d[nx, ny] >= 0 || !Drivable(nx, ny)) continue;
                d[nx, ny] = d[x, y] + 1;
                q.Enqueue((nx, ny));
            }
        }
        return d;
    }

    /// <summary>Whether a shell could fly straight from one point to another: a lane a shell wide, not just a line, so it can't clip a corner.</summary>
    public bool ClearShot(double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return true;
        double nx = -dy / len, ny = dx / len;
        int n = (int)(len / 5) + 1;
        foreach (double off in new[] { -5.0, 0, 5 })
            for (int i = 1; i < n; i++)
            {
                var (c, r) = Field.CellAt(x0 + dx * i / n + nx * off, y0 + dy * i / n + ny * off);
                if (Stops(c, r)) return false;
            }
        return true;
    }

    public static Battlefield Build(Random rng)
    {
        for (int tries = 0; ; tries++)
        {
            var b = new Battlefield();
            b.Lay(rng);
            if (b.Joined() || tries > 50) return b;
        }
    }

    private void Set(int c, int r, Ground g)
    {
        if (!Inside(c, r)) return;
        // The left half, mirrored onto the right.
        foreach (int x in new[] { c, Cols - 1 - c })
        {
            Cells[x, r] = g;
            Hp[x, r] = g == Ground.Wall ? WallHp : 0;
        }
    }

    private static bool NearSpawn(int c, int r) =>
        new[] { LeftSpawn, (Cols - 1 - LeftSpawn.C, LeftSpawn.R), RightSpawn, (Cols - 1 - RightSpawn.C, RightSpawn.R) }
            .Concat(EnemySpawns).Concat(EnemySpawns.Select(s => (Cols - 1 - s.C, s.R)))
            .Any(s => Math.Abs(s.Item1 - c) <= 2 && Math.Abs(s.Item2 - r) <= 2);

    private void Lay(Random rng)
    {
        // Ruined buildings: rectangles of wall, broken open in places.
        for (int k = 0; k < 3; k++)
        {
            int w = 3 + rng.Next(3), h = 2 + rng.Next(3), c0 = 4 + rng.Next(Cols / 2 - 4 - w), r0 = 1 + rng.Next(Rows - 2 - h);
            for (int c = c0; c < c0 + w; c++)
                for (int r = r0; r < r0 + h; r++)
                {
                    bool edge = c == c0 || c == c0 + w - 1 || r == r0 || r == r0 + h - 1;
                    if (!edge || NearSpawn(c, r)) continue;
                    if (rng.NextDouble() < .25) Set(c, r, Ground.Rubble);
                    else Set(c, r, Ground.Wall);
                }
        }
        // Boulders, in ones, twos and threes.
        for (int k = 0; k < 9; k++)
        {
            int c = 3 + rng.Next(Cols / 2 - 3), r = rng.Next(Rows), n = 1 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                int x = c + rng.Next(2), y = r + rng.Next(2);
                if (Inside(x, y) && x < Cols / 2 && !NearSpawn(x, y) && Cells[x, y] == Ground.Open) Set(x, y, Ground.Boulder);
            }
        }
        // Deep ruts, running across or along.
        for (int k = 0; k < 3; k++)
        {
            bool across = rng.Next(2) == 0;
            int len = 4 + rng.Next(4), c = 3 + rng.Next(Cols / 2 - 4), r = 1 + rng.Next(Rows - 2);
            for (int i = 0; i < len; i++)
            {
                int x = across ? c + i : c, y = across ? r : r + i;
                if (Inside(x, y) && x < Cols / 2 && !NearSpawn(x, y) && Cells[x, y] == Ground.Open) Set(x, y, Ground.Rut);
            }
        }
    }

    /// <summary>Whether every place a tank rolls out can be reached from player one's.</summary>
    private bool Joined()
    {
        var d = Distances(LeftSpawn.C, LeftSpawn.R);
        return d[RightSpawn.C, RightSpawn.R] >= 0 && EnemySpawns.All(s => d[s.C, s.R] >= 0);
    }
}

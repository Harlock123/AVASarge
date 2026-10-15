namespace AVASarge.Engine;

/// <summary>
/// How a computer tank drives. If it has a clear shot at its target it turns to aim (a Scout carelessly, a
/// Hunter or a Brute carefully) and fires. If not, it finds the nearest place it can drive to that has a clear
/// shot, and drives there round the boulders, ruts and ruins. A Hunter (and the computer at your tank, in the
/// demonstration) also swerves out of the way of a shell coming at it.
/// </summary>
public static class TankAi
{
    public const double Range = 480;

    public static Controls Decide(GameEngine g, Tank t, double dt) => Decide(g, t, Target(g, t), dt, dodges: t.Kind == TankKind.Hunter, careful: t.Kind != TankKind.Scout);

    /// <summary>The nearest tank on the other side.</summary>
    public static Tank? Target(GameEngine g, Tank t) =>
        g.Tanks.Where(o => o.Alive && o.Side != t.Side).OrderBy(o => Math.Abs(o.X - t.X) + Math.Abs(o.Y - t.Y)).FirstOrDefault();

    private static double Bearing(double fx, double fy, double tx, double ty) => Math.Atan2(tx - fx, -(ty - fy));
    private static double Diff(double a, double b) => Math.Atan2(Math.Sin(b - a), Math.Cos(b - a));

    public static Controls Decide(GameEngine g, Tank t, Tank? target, double dt, bool dodges, bool careful)
    {
        if (target == null) return default;
        var field = g.Battlefield;
        // A shell coming straight at it: swerve.
        if (dodges)
        {
            t.Dodge -= dt;
            if (t.Dodge <= 0)
                foreach (var s in g.Shells)
                {
                    if (s.Owner.Side == t.Side) continue;
                    double rx = t.X - s.X, ry = t.Y - s.Y, along = (rx * s.Vx + ry * s.Vy) / Shell.Speed;
                    if (along < 0 || along > 170) continue;
                    double across = Math.Abs(rx * s.Vy - ry * s.Vx) / Shell.Speed;
                    if (across < 22) { t.Dodge = .45; t.DodgeDir = Math.Sign(rx * s.Vy - ry * s.Vx) is 0 ? 1 : Math.Sign(rx * s.Vy - ry * s.Vx); break; }
                }
            if (t.Dodge > 0)
            {
                // Turn side-on to the shell and drive.
                var s = g.Shells.Where(x => x.Owner.Side != t.Side).OrderBy(x => Math.Abs(x.X - t.X) + Math.Abs(x.Y - t.Y)).FirstOrDefault();
                if (s != null)
                {
                    double away = Math.Atan2(s.Vx, -s.Vy) + t.DodgeDir * Math.PI / 2, e = Diff(t.Angle, away);
                    bool back = Math.Abs(e) > Math.PI / 2;
                    if (back) e = Diff(t.Angle, away + Math.PI);
                    return new Controls(Math.Abs(e) > .15 ? Math.Sign(e) : 0, back ? 1 : -1, false);
                }
            }
        }
        double dist = Math.Sqrt((target.X - t.X) * (target.X - t.X) + (target.Y - t.Y) * (target.Y - t.Y));
        if (dist < Range && field.ClearShot(t.X, t.Y, target.X, target.Y))
        {
            // Aim, leading a moving target a little, and fire when it's lined up.
            double want = Bearing(t.X, t.Y, target.X, target.Y) + t.AimNoise * (careful ? .5 : 1);
            double err = Diff(t.Angle, want);
            double tol = careful ? .05 : .12;
            int back = dist < 70 ? 1 : 0;
            t.Path.Clear();
            // It holds its aim a moment before it fires (a careful one longer).
            // (Not at a tank that can't be hurt yet.)
            t.AimHold = Math.Abs(err) < tol && target.Safe <= 0 ? t.AimHold + dt : 0;
            return new Controls(Math.Abs(err) > .03 ? Math.Sign(err) : 0, back, t.AimHold >= t.Steady && t.Safe <= 0);
        }
        // Somewhere with a clear shot: plan the way there now and then, and follow it.
        t.Replan -= dt;
        if (t.Replan <= 0 || t.Path.Count == 0)
        {
            t.Replan = .6;
            // A spot in range with a clear shot; failing that one at any range; failing that, just close in.
            t.Path = Plan(field, t, target, Range * .85);
            if (t.Path.Count == 0) t.Path = Plan(field, t, target, 5000);
            if (t.Path.Count == 0) t.Path = Approach(field, t, target);
        }
        while (t.Path.Count > 0)
        {
            var (px, py) = Field.Centre(t.Path[0].C, t.Path[0].R);
            if (Math.Abs(px - t.X) + Math.Abs(py - t.Y) > 10) break;
            t.Path.RemoveAt(0);
        }
        if (t.Path.Count == 0) { double e0 = Diff(t.Angle, Bearing(t.X, t.Y, target.X, target.Y)); return new Controls(Math.Abs(e0) > .05 ? Math.Sign(e0) : 0, 0, false); }
        var (nx, ny) = Field.Centre(t.Path[0].C, t.Path[0].R);
        double head = Diff(t.Angle, Bearing(t.X, t.Y, nx, ny));
        return new Controls(Math.Abs(head) > .08 ? Math.Sign(head) : 0, Math.Abs(head) < .6 ? -1 : 0, false);
    }

    /// <summary>The way, cell by cell, to the nearest cell from which there's a clear shot at the target in range.</summary>
    public static List<(int C, int R)> Plan(Battlefield field, Tank t, Tank target, double range)
    {
        var (sc, sr) = Field.CellAt(t.X, t.Y);
        var from = new (int C, int R)[Field.Cols, Field.Rows];
        var seen = new bool[Field.Cols, Field.Rows];
        var q = new Queue<(int C, int R)>();
        seen[sc, sr] = true;
        q.Enqueue((sc, sr));
        (int C, int R)? goal = null;
        while (q.Count > 0)
        {
            var at = q.Dequeue();
            var (x, y) = Field.Centre(at.C, at.R);
            double d = Math.Sqrt((target.X - x) * (target.X - x) + (target.Y - y) * (target.Y - y));
            if (d < range && d > 60 && field.ClearShot(x, y, target.X, target.Y)) { goal = at; break; }
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = at.C + dx, ny = at.R + dy;
                if (!Battlefield.Inside(nx, ny) || seen[nx, ny] || !field.Drivable(nx, ny)) continue;
                seen[nx, ny] = true;
                from[nx, ny] = at;
                q.Enqueue((nx, ny));
            }
        }
        return Trace(from, (sc, sr), goal);
    }

    /// <summary>The way to whichever cell it can reach that's nearest the target.</summary>
    public static List<(int C, int R)> Approach(Battlefield field, Tank t, Tank target)
    {
        var (sc, sr) = Field.CellAt(t.X, t.Y);
        var (tc, tr) = Field.CellAt(target.X, target.Y);
        var from = new (int C, int R)[Field.Cols, Field.Rows];
        var seen = new bool[Field.Cols, Field.Rows];
        var q = new Queue<(int C, int R)>();
        seen[sc, sr] = true;
        q.Enqueue((sc, sr));
        (int C, int R)? best = null;
        int bd = Math.Abs(sc - tc) + Math.Abs(sr - tr);
        while (q.Count > 0)
        {
            var at = q.Dequeue();
            int d = Math.Abs(at.C - tc) + Math.Abs(at.R - tr);
            if (d < bd) { bd = d; best = at; }
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = at.C + dx, ny = at.R + dy;
                if (!Battlefield.Inside(nx, ny) || seen[nx, ny] || !field.Drivable(nx, ny)) continue;
                seen[nx, ny] = true;
                from[nx, ny] = at;
                q.Enqueue((nx, ny));
            }
        }
        return Trace(from, (sc, sr), best);
    }

    private static List<(int C, int R)> Trace((int C, int R)[,] from, (int C, int R) start, (int C, int R)? goal)
    {
        var (sc, sr) = start;
        var path = new List<(int C, int R)>();
        if (goal is not { } g) return path;
        for (var at = g; at != (sc, sr); at = from[at.C, at.R]) path.Add(at);
        path.Reverse();
        return path;
    }
}

/// <summary>The computer at your tank, for the demonstration (and the tests): a Hunter's brain, steady aim and quick to dodge.</summary>
public sealed class TankPilot
{
    public Controls Decide(GameEngine g)
    {
        if (g.Phase != Phase.Play) return default;
        var t = g.Sarge;
        if (!t.Alive) return default;
        return TankAi.Decide(g, t, TankAi.Target(g, t), Field.Step, dodges: true, careful: true);
    }
}

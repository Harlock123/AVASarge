using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVASarge.Engine;
using Effect = AVASarge.Engine.Effect;

namespace AVASarge.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVASarge/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2EEE0, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFFA8A898, Khaki = 0xFFD8C890;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF14140E), new Rect(0, 0, Field.Width, Field.Height));
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        if (inGame)
        {
            DrawField(dc);
            foreach (var t in g.Tanks.Where(t => t.Alive)) DrawTank(dc, t, new Point(t.X, t.Y), 1);
            foreach (var s in g.Shells)
            {
                double sp = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
                var tail = new Point(s.X - s.Vx / sp * 10, s.Y - s.Vy / sp * 10);
                dc.DrawLine(Pen(Alpha(0xFFFFD070, .5), 4), tail, new Point(s.X, s.Y));
                dc.DrawEllipse(Brush(0xFFFFF4C0), Pen(0xFF8A6A20, 1), new Point(s.X, s.Y), 3.2, 3.2);
            }
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver && g.Players == 1)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS 1 OR 2 TO PLAY", new Point(Field.Width / 2, Field.Height - 18), 26, Yellow, center: true);
    }

    // ───────────────────────────── the battlefield ─────────────────────────────

    private void DrawField(DrawingContext dc)
    {
        var g = Engine;
        var b = g.Battlefield;
        double cell = Field.Cell;
        // The ground: churned earth and patches of dry grass.
        dc.FillRectangle(Brush(0xFF5C5638), new Rect(0, Field.Top, Field.Width, Field.Bottom - Field.Top));
        for (int c = 0; c < Field.Cols; c++)
            for (int r = 0; r < Field.Rows; r++)
            {
                uint h = Hash(c, r, 11);
                var (x, y) = (c * cell, Field.Top + r * cell);
                if (h % 4 == 0) dc.FillRectangle(Brush(0xFF625C3C), new Rect(x, y, cell, cell));
                if (h % 5 == 0) dc.FillRectangle(Brush(0xFF544E32), new Rect(x + h % 16, y + (h >> 4) % 16, 14, 10));
                if (h % 7 < 2)
                    for (int k = 0; k < 3; k++)
                    {
                        double gx = x + 6 + (h >> (k * 3)) % 20, gy = y + 8 + (h >> (k * 3 + 9)) % 18;
                        dc.DrawLine(Pen(0xFF7A7A44, 1.2), new Point(gx, gy), new Point(gx - 2, gy - 5));
                        dc.DrawLine(Pen(0xFF7A7A44, 1.2), new Point(gx, gy), new Point(gx + 2, gy - 5));
                    }
            }
        foreach (var s in g.Scorches) dc.DrawEllipse(Brush(0x48100C08), null, new Point(s.X, s.Y), s.Size, s.Size * .8);
        for (int c = 0; c < Field.Cols; c++)
            for (int r = 0; r < Field.Rows; r++)
            {
                var rect = new Rect(c * cell, Field.Top + r * cell, cell, cell);
                switch (b.Cells[c, r])
                {
                    case Ground.Rut: DrawRut(dc, b, c, r, rect); break;
                    case Ground.Rubble: DrawRubble(dc, c, r, rect); break;
                    case Ground.Wall: DrawWall(dc, b, c, r, rect); break;
                    case Ground.Boulder: DrawBoulder(dc, c, r, rect); break;
                }
            }
    }

    /// <summary>A deep rut: a dark trench, its banks joined up with the cells beside it.</summary>
    private static void DrawRut(DrawingContext dc, Battlefield b, int c, int r, Rect rect)
    {
        bool l = b.At(c - 1, r) == Ground.Rut, rt = b.At(c + 1, r) == Ground.Rut, u = b.At(c, r - 1) == Ground.Rut, d = b.At(c, r + 1) == Ground.Rut;
        var inner = new Rect(rect.X + (l ? 0 : 5), rect.Y + (u ? 0 : 5), rect.Width - (l ? 0 : 5) - (rt ? 0 : 5), rect.Height - (u ? 0 : 5) - (d ? 0 : 5));
        dc.FillRectangle(Brush(0xFF2A2214), inner, 3);
        dc.FillRectangle(Brush(0xFF18120A), inner.Deflate(4), 2);
        if (!u) dc.DrawLine(Pen(0xFF8A7A50, 2), inner.TopLeft, inner.TopRight);
        if (!d) dc.DrawLine(Pen(0xFF3A3020, 2), inner.BottomLeft, inner.BottomRight);
        if (!l) dc.DrawLine(Pen(0xFF8A7A50, 2), inner.TopLeft, inner.BottomLeft);
        if (!rt) dc.DrawLine(Pen(0xFF3A3020, 2), inner.TopRight, inner.BottomRight);
    }

    private static void DrawBoulder(DrawingContext dc, int c, int r, Rect rect)
    {
        var at = rect.Center;
        var pts = Enumerable.Range(0, 9).Select(i =>
        {
            double a = i * Math.PI * 2 / 9, rr = 12 + Hash(c + i, r, 5) % 4;
            return new Point(at.X + Math.Cos(a) * rr, at.Y + Math.Sin(a) * rr * .9);
        }).ToArray();
        dc.DrawEllipse(Brush(0x50000000), null, new Point(at.X + 3, at.Y + 4), 14, 11);
        Poly(dc, 0xFF7A766E, 0xFF3A3834, 1.5, pts);
        dc.DrawEllipse(Brush(0xFF9A968E), null, new Point(at.X - 4, at.Y - 4), 6, 4);
        dc.DrawLine(Pen(0xFF4A4844, 1.2), new Point(at.X + 2, at.Y - 2), new Point(at.X + 6, at.Y + 5));
    }

    /// <summary>A ruined wall of bricks, cracking more with each hit.</summary>
    private static void DrawWall(DrawingContext dc, Battlefield b, int c, int r, Rect rect)
    {
        dc.FillRectangle(Brush(0xFF6A3A28), rect.Deflate(1));
        for (int row = 0; row < 4; row++)
        {
            double y = rect.Y + 1 + row * 7.5;
            for (int k = -1; k < 4; k++)
            {
                double x = rect.X + 1 + k * 10 + (row % 2) * 5;
                var br = new Rect(Math.Max(rect.X + 1, x), y, Math.Min(9, rect.Right - 1 - Math.Max(rect.X + 1, x)), 6.5);
                if (br.Width <= 1) continue;
                dc.FillRectangle(Brush((Hash(c * 4 + k, r * 4 + row, 2) % 3) switch { 0 => 0xFF9A5038, 1 => 0xFF8A4430, _ => 0xFFA85A40 }), br);
            }
        }
        int damage = Battlefield.WallHp - b.Hp[c, r];
        for (int k = 0; k < damage; k++)
        {
            var p0 = new Point(rect.X + 6 + Hash(c, r, k) % 20, rect.Y + 4);
            dc.DrawLine(Pen(0xFF201008, 2), p0, new Point(p0.X + 5, rect.Center.Y));
            dc.DrawLine(Pen(0xFF201008, 2), new Point(p0.X + 5, rect.Center.Y), new Point(p0.X - 2, rect.Bottom - 4));
        }
    }

    private static void DrawRubble(DrawingContext dc, int c, int r, Rect rect)
    {
        for (int k = 0; k < 7; k++)
        {
            uint h = Hash(c, r, k + 20);
            var p = new Point(rect.X + 4 + h % 24, rect.Y + 4 + (h >> 6) % 24);
            Poly(dc, k % 2 == 0 ? 0xFF8A4A34 : 0xFF6A5A48, null, 0, p, new Point(p.X + 6, p.Y + 1), new Point(p.X + 4, p.Y + 5), new Point(p.X - 1, p.Y + 4));
        }
    }

    // ───────────────────────────── the tanks ─────────────────────────────

    /// <summary>A tank from above: treads that roll as it moves, a hull in its colours, and the turret and gun.</summary>
    private void DrawTank(DrawingContext dc, Tank t, Point at, double scale)
    {
        if (t.Safe > 0 && (int)(_clock * 12) % 2 == 0) return;
        double k = scale * (t.Kind == TankKind.Brute ? 1.18 : 1), sn = Math.Sin(t.Angle), cs = Math.Cos(t.Angle);
        Point P(double x, double y) => new(at.X + (x * cs - y * sn) * k, at.Y + (x * sn + y * cs) * k);
        uint col = t.Hurt > 0 && (int)(_clock * 20) % 2 == 0 ? 0xFFFFFFFF : GameEngine.Colour(t.Kind);
        uint dark = 0xFF1C1C18;
        // Treads.
        foreach (int side in new[] { -1, 1 })
        {
            Poly(dc, dark, null, 0, P(side * 15, -15), P(side * 9, -15), P(side * 9, 15), P(side * 15, 15));
            double off = (t.Rolling * .6) % 6;
            for (double y = -14 + off; y < 14; y += 6) dc.DrawLine(Pen(0xFF4A4A40, 1.5), P(side * 15, y), P(side * 9, y));
        }
        // Hull, turret and gun.
        Poly(dc, col, 0xFF101008, 1.4, P(-10, -13), P(10, -13), P(11, 14), P(-11, 14));
        Poly(dc, Alpha(0xFFFFFFFF, .18), null, 0, P(-9, -12), P(9, -12), P(8, -6), P(-8, -6));
        dc.DrawLine(Pen(0xFF101008, 5), P(0, -2), P(0, -22));
        dc.DrawLine(Pen(col, 3), P(0, -2), P(0, -21));
        dc.DrawEllipse(Brush(col), Pen(0xFF101008, 1.4), P(0, 1), 7.5 * k, 7.5 * k);
        // Markings: a star for you, a ring for player two, a stripe for the enemy.
        switch (t.Kind)
        {
            case TankKind.Sarge:
            {
                var c0 = P(0, 1);
                var star = Enumerable.Range(0, 10).Select(i =>
                {
                    double a = t.Angle - Math.PI / 2 + i * Math.PI / 5, rr = i % 2 == 0 ? 4.5 * k : 1.8 * k;
                    return new Point(c0.X + Math.Cos(a) * rr, c0.Y + Math.Sin(a) * rr);
                }).ToArray();
                Poly(dc, 0xFFF4F0E0, null, 0, star);
                break;
            }
            case TankKind.Rival:
                dc.DrawEllipse(null, Pen(0xFFF4F0E0, 1.6), P(0, 1), 3.5 * k, 3.5 * k);
                break;
            default:
                dc.DrawLine(Pen(0xFF101008, 2), P(-9, 9), P(9, 9));
                break;
        }
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (4 + 16 * t) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFFFFE080, (1 - t) * .7)), null, c, r, r);
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2), c, r * 1.4, r * 1.4);
                break;
            }
            case EffectKind.Dust:
                for (int k = 0; k < 6; k++)
                {
                    double a = k * Math.PI / 3 + Hash(k, (int)e.X, 3) % 10 * .1, d = (4 + 22 * t) * e.Size;
                    dc.DrawEllipse(Brush(Alpha(e.Colour, (1 - t) * .55)), null, new Point(c.X + Math.Cos(a) * d, c.Y + Math.Sin(a) * d), 5 * e.Size, 5 * e.Size);
                }
                break;
            case EffectKind.Blast:
            {
                double r = (10 + 50 * Math.Sqrt(t)) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFFFFE080, (1 - t) * .7)), null, c, r * .6, r * .6);
                dc.DrawEllipse(null, Pen(Alpha(0xFFFF7A2A, 1 - t), 4), c, r, r);
                for (int k = 0; k < 12; k++)
                {
                    double a = k * Math.PI / 6 + Hash(k, (int)e.X, 7) % 10 * .05, d = r * (.6 + Hash(k, 3, 7) % 10 * .05);
                    dc.FillRectangle(Brush(Alpha(k % 3 == 0 ? e.Colour : 0xFF2A2A20, 1 - t)), new Rect(c.X + Math.Cos(a) * d - 3, c.Y + Math.Sin(a) * d - 3, 6, 6));
                }
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - 18 - t * 26), 30, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 36, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        if (inGame && g.Players == 2)
        {
            Text(dc, "PLAYER 1", new Point(16, 18), 22, GameEngine.Colour(TankKind.Sarge));
            Text(dc, g.Wins[0].ToString(), new Point(16, 48), 40, Yellow);
            Text(dc, "PLAYER 2", new Point(Field.Width - 16, 18), 22, 0xFF6A9AFA, right: true);
            Text(dc, g.Wins[1].ToString(), new Point(Field.Width - 16, 48), 40, Yellow, right: true);
            Text(dc, $"DUEL - FIRST TO {GameEngine.DuelTarget}", new Point(Field.Width / 2, 36), 36, White, center: true);
            return;
        }
        Text(dc, "SCORE", new Point(16, 18), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 48), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 18), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 48), 30, Dim, right: true);
        if (g.State is GameState.Playing or GameState.Attract or GameState.Paused)
        {
            Text(dc, $"ROUND {g.Round}", new Point(Field.Width / 2, 36), 38, White, center: true);
            int left = g.ToCome + g.Tanks.Count(t => t.Computer);
            Text(dc, $"ENEMY {left}", new Point(Field.Width - 190, 36), 28, Khaki, right: true);
            int spare = Math.Max(0, g.Lives - (g.Tanks.Any(t => t.Kind == TankKind.Sarge && t.Alive) ? 1 : 0));
            for (int i = 0; i < Math.Min(spare, 6); i++)
                DrawTank(dc, new Tank { Kind = TankKind.Sarge }, new Point(190 + i * 34, 36), .75);
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 300;
        var (w, h) = (Math.Max(msg.Length * 30, (g.SubMessage?.Length ?? 0) * 16) + 70, g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xC0100E08), new Rect(cx - w / 2, cy - 48, w, h), 6);
        dc.DrawRectangle(null, Pen(0x80D8C890, 2), new Rect(cx - w / 2, cy - 48, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), 56, g.Phase == Phase.Dying && g.Players == 1 ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 28, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "AVA SARGE", new Point(cx, 112), 120, Khaki, center: true);
        Text(dc, "AFTER THE 1985 ARCADE CLASSIC", new Point(cx, 184), 28, Dim, center: true);
        DrawTank(dc, new Tank { Kind = TankKind.Sarge, Angle = Math.PI / 2, Rolling = _clock * 60 }, new Point(cx - 240, 262), 2.2);
        DrawTank(dc, new Tank { Kind = TankKind.Rival, Angle = -Math.PI / 2, Rolling = _clock * 60 }, new Point(cx + 240, 262), 2.2);
        double sx = cx - 240 + 60 + (_clock * 260) % 360;
        dc.DrawEllipse(Brush(0xFFFFF4C0), null, new Point(sx, 262), 4, 4);
        string[] lines =
        [
            "ONE PLAYER (PRESS 1 OR ENTER): HOLD OFF ROUND AFTER ROUND OF ENEMY TANKS",
            "TWO PLAYERS (PRESS 2): A DUEL - FIRST TO FIVE HITS WINS",
            "BOULDERS STOP TANKS AND SHELLS - SHELLS FLY OVER THE RUTS - WALLS CRUMBLE",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 350 + i * 30), 24, White, center: true);
        TankKind[] kinds = [TankKind.Scout, TankKind.Hunter, TankKind.Brute];
        string[] notes = ["QUICK, CARELESS", "SHARP, DODGES", "SLOW, TWO HITS"];
        for (int i = 0; i < kinds.Length; i++)
        {
            double x = cx - 240 + i * 240;
            DrawTank(dc, new Tank { Kind = kinds[i] }, new Point(x, 470), 1.4);
            Text(dc, $"{GameEngine.NameOf(kinds[i])}  {GameEngine.Points(kinds[i])}", new Point(x, 512), 26, Yellow, center: true);
            Text(dc, notes[i], new Point(x, 536), 22, Dim, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS 1 OR 2", new Point(cx, 604), 46, Yellow, center: true);
        Text(dc, "PLAYER 1: ARROWS TO DRIVE, SPACE TO FIRE    PLAYER 2: W A S D, LEFT SHIFT", new Point(cx, 664), 24, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE TOUGHEST SARGES", new Point(cx, 130), 64, Khaki, center: true);
        Text(dc, "RANK NAME    SCORE   ROUND", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4} ", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS 1 OR 2 TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

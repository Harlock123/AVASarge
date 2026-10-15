namespace AVASarge.Engine;

public sealed partial class GameEngine
{
    public const double StartSeconds = 2.6, DyingSeconds = 2.2, ClearSeconds = 3.0, SafeSeconds = 2.5, SpawnEvery = 2.5;
    public const double PlayerReload = .45;
    public const int DuelTarget = 5, RoundBonus = 1000;
    public const int FirstExtraLife = 20000, ExtraLifeEvery = 50000;

    public int Round { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Battlefield Battlefield { get; private set; } = new();
    public List<Tank> Tanks { get; } = [];
    public List<Shell> Shells { get; } = [];
    public List<Scorch> Scorches { get; } = [];
    /// <summary>A duel's score: hits by player one and by player two.</summary>
    public int[] Wins { get; } = new int[2];
    public TankPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double RoundTime { get; private set; }
    /// <summary>Enemy tanks still to come this round (one-player).</summary>
    public int ToCome { get; private set; }
    /// <summary>For tests: no enemy tanks roll out, so a test can set up just what it wants.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra;
    private double _spawnIn;
    private bool _fire1Prev, _fire2Prev;
    private Tank? _hit;

    public Tank Sarge => Tanks.First(t => t.Kind == TankKind.Sarge);
    public Tank? Rival => Tanks.FirstOrDefault(t => t.Kind == TankKind.Rival);

    public static int EnemiesIn(int round) => Math.Min(12, 3 + round);
    public static int OnField(int round) => Math.Min(4, 1 + (round + 1) / 2);

    public static int Points(TankKind k) => k switch { TankKind.Scout => 300, TankKind.Hunter => 500, TankKind.Brute => 800, _ => 0 };

    public static double SpeedOf(TankKind k) => k switch { TankKind.Scout => 115, TankKind.Hunter => 88, TankKind.Brute => 62, _ => 96 };
    public static double TurnOf(TankKind k) => k switch { TankKind.Scout => 3.0, TankKind.Hunter => 2.5, TankKind.Brute => 1.7, _ => 2.6 };
    public static double ReloadOf(TankKind k) => k switch { TankKind.Scout => 1.1, TankKind.Hunter => .9, TankKind.Brute => 1.4, _ => PlayerReload };

    public static uint Colour(TankKind k) => k switch
    {
        TankKind.Sarge => 0xFF6A9A3A, TankKind.Rival => 0xFF3A6ACA, TankKind.Scout => 0xFFC8A860, TankKind.Hunter => 0xFF8A8C90, _ => 0xFF9A3A2A,
    };

    public static string NameOf(TankKind k) => k switch
    {
        TankKind.Sarge => "SARGE", TankKind.Rival => "PLAYER 2", TankKind.Scout => "SCOUT", TankKind.Hunter => "HUNTER", _ => "BRUTE",
    };

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Round = 1;
        Wins[0] = Wins[1] = 0;
        _nextExtra = FirstExtraLife;
        LayOut();
        SetPhase(Phase.Start);
    }

    private void BeginRound(int round)
    {
        Round = round;
        LayOut();
        SetPhase(Phase.Start);
        if (Players == 2) Say("DUEL", $"FIRST TO {DuelTarget} HITS WINS", StartSeconds);
        else Say($"ROUND {round}", $"{ToCome} ENEMY TANKS", StartSeconds);
        Play(Sfx.RoundStart);
    }

    /// <summary>A fresh battlefield, with your tank (and player two's) rolled out.</summary>
    private void LayOut()
    {
        Battlefield = Battlefield.Build(_rng);
        Tanks.Clear();
        Shells.Clear();
        Scorches.Clear();
        Effects.Clear();
        RoundTime = 0;
        ToCome = Players == 2 || Calm ? 0 : EnemiesIn(Round);
        _spawnIn = 1.2;
        Tanks.Add(NewTank(TankKind.Sarge, Battlefield.LeftSpawn, Math.PI / 2, 0));
        if (Players == 2) Tanks.Add(NewTank(TankKind.Rival, Battlefield.RightSpawn, -Math.PI / 2, 1));
    }

    private static Tank NewTank(TankKind kind, (int C, int R) at, double angle, int side)
    {
        var (x, y) = Field.Centre(at.C, at.R);
        return new Tank { Kind = kind, X = x, Y = y, Angle = angle, Side = side, Hp = kind == TankKind.Brute ? 2 : 1, Safe = kind is TankKind.Sarge or TankKind.Rival ? SafeSeconds : 0 };
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c1, Controls c2)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Start:
                _fire1Prev = c1.Fire;
                _fire2Prev = c2.Fire;
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                RoundTime += dt;
                Drive(Sarge, c1, ref _fire1Prev, dt);
                if (Rival is { } rival) Drive(rival, c2, ref _fire2Prev, dt);
                foreach (var t in Tanks.Where(t => t.Computer && t.Alive).ToList())
                {
                    bool unused = false;
                    Drive(t, TankAi.Decide(this, t, dt), ref unused, dt);
                }
                MoveShells(dt);
                if (Phase == Phase.Play && Players == 1) { Reinforce(dt); RoundWon(); }
                break;
            case Phase.Dying:
                MoveShells(dt);
                if (PhaseTime > DyingSeconds) Recover();
                break;
            case Phase.Clear:
                MoveShells(dt);
                if (PhaseTime > ClearSeconds)
                {
                    if (Players == 2) GameOver();
                    else BeginRound(Round + 1);
                }
                break;
        }
        foreach (var t in Tanks) { t.Safe = Math.Max(0, t.Safe - dt); t.Hurt = Math.Max(0, t.Hurt - dt); }
    }

    // ───────────────────────────── driving and firing ─────────────────────────────

    /// <summary>Turn, roll forward or back (slower over rubble), and fire if a shell's ready and none of its own is in the air.</summary>
    private void Drive(Tank t, Controls c, ref bool firePrev, double dt)
    {
        if (!t.Alive) return;
        t.Angle += c.Dx * TurnOf(t.Kind) * dt;
        double speed = c.Dy < 0 ? SpeedOf(t.Kind) : c.Dy > 0 ? -SpeedOf(t.Kind) * .6 : 0;
        var (cc, cr) = Field.CellAt(t.X, t.Y);
        if (Battlefield.At(cc, cr) == Ground.Rubble) speed *= .6;
        if (speed != 0)
        {
            var (ax, ay) = t.Ahead;
            double nx = t.X + ax * speed * dt, ny = t.Y + ay * speed * dt;
            if (Free(t, nx, ny)) (t.X, t.Y) = (nx, ny);
            else if (Free(t, nx, t.Y)) t.X = nx;
            else if (Free(t, t.X, ny)) t.Y = ny;
            t.Rolling += Math.Abs(speed) * dt;
        }
        else if (c.Dx != 0) t.Rolling += 20 * dt;
        bool pressed = c.Fire && !firePrev;
        firePrev = c.Fire;
        t.Reload -= dt;
        bool computerFires = t.Computer && c.Fire;
        if ((pressed || computerFires || (c.Fire && t.Reload <= -.2)) && t.Reload <= 0 && !Shells.Any(s => s.Owner == t))
        {
            var (ax, ay) = t.Ahead;
            Shells.Add(new Shell { X = t.X + ax * 18, Y = t.Y + ay * 18, Vx = ax * Shell.Speed, Vy = ay * Shell.Speed, Owner = t });
            t.Reload = ReloadOf(t.Kind);
            // A computer tank's aim wanders a little afresh with every shot.
            if (t.Computer)
            {
                t.AimNoise = (_rng.NextDouble() - .5) * (t.Kind == TankKind.Scout ? .22 : .07);
                t.Steady = (t.Kind == TankKind.Scout ? .15 : .3) + _rng.NextDouble() * .4;
            }
            Effects.Add(new Effect { Kind = EffectKind.Dust, X = t.X + ax * 22, Y = t.Y + ay * 22, Life = .25, Size = .5, Colour = 0xFFFFE0A0 });
            Play(t.Computer ? Sfx.EnemyFire : Sfx.Fire);
        }
    }

    /// <summary>Whether a tank can be at a point: on the field, clear of anything solid and of the other tanks.</summary>
    public bool Free(Tank t, double x, double y)
    {
        const double r = Tank.Radius;
        if (x < r || x > Field.Width - r || y < Field.Top + r || y > Field.Bottom - r) return false;
        var (c, row) = Field.CellAt(x, y);
        for (int dc = -1; dc <= 1; dc++)
            for (int dr = -1; dr <= 1; dr++)
            {
                if (Battlefield.Drivable(c + dc, row + dr)) continue;
                double cx0 = (c + dc) * Field.Cell, cy0 = Field.Top + (row + dr) * Field.Cell;
                double px = Math.Clamp(x, cx0, cx0 + Field.Cell), py = Math.Clamp(y, cy0, cy0 + Field.Cell);
                if ((px - x) * (px - x) + (py - y) * (py - y) < r * r) return false;
            }
        foreach (var o in Tanks)
            if (o != t && o.Alive && (o.X - x) * (o.X - x) + (o.Y - y) * (o.Y - y) < (2 * r - 2) * (2 * r - 2)) return false;
        return true;
    }

    private void MoveShells(double dt)
    {
        foreach (var s in Shells)
        {
            s.Age += dt;
            if (s.Age > Shell.Life) { s.Dead = true; Puff(s.X, s.Y, .4); continue; }
            for (int k = 0; k < 4 && !s.Dead; k++)
            {
                s.X += s.Vx * dt / 4;
                s.Y += s.Vy * dt / 4;
                if (s.X < 0 || s.X > Field.Width || s.Y < Field.Top || s.Y > Field.Bottom) { s.Dead = true; break; }
                var (c, r) = Field.CellAt(s.X, s.Y);
                if (Battlefield.Stops(c, r)) { Strike(s, c, r); break; }
                // Only while the fighting's on: no stray shell counts once a round is won or lost.
                if (Phase != Phase.Play) continue;
                foreach (var t in Tanks)
                {
                    if (!t.Alive || t == s.Owner || t.Safe > 0 || (t.Side == s.Owner.Side && Players == 1)) continue;
                    if ((t.X - s.X) * (t.X - s.X) + (t.Y - s.Y) * (t.Y - s.Y) > (Tank.Radius + 2) * (Tank.Radius + 2)) continue;
                    s.Dead = true;
                    Hit(t, s.Owner);
                    break;
                }
            }
        }
        Shells.RemoveAll(s => s.Dead);
    }

    /// <summary>A shell bursting on a boulder or a wall; walls crumble to rubble after a few.</summary>
    private void Strike(Shell s, int c, int r)
    {
        s.Dead = true;
        Puff(s.X, s.Y, .6);
        if (Battlefield.At(c, r) == Ground.Wall)
        {
            if (--Battlefield.Hp[c, r] <= 0)
            {
                Battlefield.Cells[c, r] = Ground.Rubble;
                var (x, y) = Field.Centre(c, r);
                Effects.Add(new Effect { Kind = EffectKind.Dust, X = x, Y = y, Life = .9, Size = 1.4, Colour = 0xFFB89070 });
                Play(Sfx.Rumble);
            }
            else Play(Sfx.Wall);
        }
        else Play(Sfx.Rock);
    }

    private void Puff(double x, double y, double size)
    {
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Life = .35, Size = size, Colour = 0xFFFFC060 });
        if (Scorches.Count > 80) Scorches.RemoveAt(0);
        Scorches.Add(new Scorch(x, y, 5 + size * 6));
    }

    private void Hit(Tank t, Tank by)
    {
        if (--t.Hp > 0)
        {
            t.Hurt = .4;
            Puff(t.X, t.Y, .8);
            Play(Sfx.Hit);
            return;
        }
        t.Alive = false;
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = t.X, Y = t.Y, Life = 1.3, Size = t.Kind == TankKind.Brute ? 1.4 : 1, Colour = Colour(t.Kind) });
        Scorches.Add(new Scorch(t.X, t.Y, 22));
        Play(Sfx.Boom);
        if (Players == 2)
        {
            Wins[by.Side]++;
            _hit = t;
            Play(Sfx.Point);
            if (Wins[by.Side] >= DuelTarget)
            {
                SetPhase(Phase.Clear);
                Music(false);
                Say($"PLAYER {by.Side + 1} WINS", $"{Wins[0]} - {Wins[1]}", ClearSeconds);
                Play(Sfx.Clear);
            }
            else
            {
                SetPhase(Phase.Dying);
                Say($"HIT FOR PLAYER {by.Side + 1}", $"{Wins[0]} - {Wins[1]}", DyingSeconds);
            }
            return;
        }
        if (t.Kind == TankKind.Sarge)
        {
            Cause = by.Kind.ToString().ToUpperInvariant();
            Lives--;
            _hit = t;
            SetPhase(Phase.Dying);
            Music(false);
            Play(Sfx.Death);
            return;
        }
        AddScore(Points(t.Kind), t.X, t.Y);
        Tanks.Remove(t);
        RoundWon();
    }

    /// <summary>Every enemy tank of the round destroyed: the round's won.</summary>
    private void RoundWon()
    {
        if (Players != 1 || Calm || Phase != Phase.Play || ToCome > 0 || Tanks.Any(o => o.Computer)) return;
        int bonus = RoundBonus * Round;
        AddScore(bonus);
        SetPhase(Phase.Clear);
        Music(false);
        Say("ROUND CLEAR", $"BONUS {bonus}", ClearSeconds);
        Play(Sfx.Clear);
    }

    /// <summary>After a hit: the tank that was hit rolls out again at its starting place, briefly safe.</summary>
    private void Recover()
    {
        if (Players == 1 && Lives <= 0) { GameOver(); return; }
        if (_hit is { } t)
        {
            var at = t.Side == 0 ? Battlefield.LeftSpawn : Battlefield.RightSpawn;
            (t.X, t.Y) = Field.Centre(at.C, at.R);
            t.Angle = t.Side == 0 ? Math.PI / 2 : -Math.PI / 2;
            t.Alive = true;
            t.Hp = 1;
            t.Safe = SafeSeconds;
            if (Players == 1)
            {
                // The enemy tanks pull back, to come again (a few at a time) from their own side.
                foreach (var o in Tanks.Where(o => o.Computer).ToList())
                {
                    Tanks.Remove(o);
                    ToCome++;
                }
                _spawnIn = SpawnEvery;
            }
            else
                foreach (var o in Tanks.Where(o => o != t && Math.Abs(o.X - t.X) + Math.Abs(o.Y - t.Y) < 40)) o.Y += 48;
        }
        _hit = null;
        Shells.Clear();
        SetPhase(Phase.Play);
        if (State == GameState.Playing) Music(true);
    }

    /// <summary>More enemy tanks roll out on the far side, a few at a time, until the round's are all used up.</summary>
    private void Reinforce(double dt)
    {
        if (Calm || ToCome <= 0) return;
        _spawnIn -= dt;
        if (_spawnIn > 0 || Tanks.Count(t => t.Computer) >= OnField(Round)) return;
        var s = Sarge;
        var spots = Battlefield.EnemySpawns.Where(p =>
        {
            var (x, y) = Field.Centre(p.C, p.R);
            return Math.Abs(x - s.X) + Math.Abs(y - s.Y) > 260 && Tanks.All(t => Math.Abs(t.X - x) + Math.Abs(t.Y - y) > 40);
        }).ToList();
        if (spots.Count == 0) return;
        _spawnIn = SpawnEvery;
        double roll = _rng.NextDouble();
        var kind = Round == 1 ? TankKind.Scout
                 : Round == 2 ? (roll < .6 ? TankKind.Scout : TankKind.Hunter)
                 : roll < .4 ? TankKind.Scout : roll < .75 ? TankKind.Hunter : TankKind.Brute;
        var e = NewTank(kind, spots[_rng.Next(spots.Count)], -Math.PI / 2, 1);
        e.AimNoise = (_rng.NextDouble() - .5) * (kind == TankKind.Scout ? .25 : .08);
        e.Steady = .3 + _rng.NextDouble() * .5;
        Tanks.Add(e);
        ToCome--;
    }

    /// <summary>For the computer's tanks: a random number from the game's own stream, so a seed always plays the same.</summary>
    internal double Random() => _rng.NextDouble();

    private void AddScore(int points, double? x = null, double? y = null)
    {
        Score += points;
        if (x is { } px && y is { } py)
            Effects.Add(new Effect { Kind = EffectKind.Score, X = px, Y = py, Text = points.ToString(), Colour = 0xFFFFE14A, Life = 1.1 });
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c, Controls c2 = default)
    {
        for (int i = 0; i < ticks; i++) Tick(c, false, c2);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a round (or, for two players, a duel).</summary>
    internal void Go(int round = 1, int players = 1)
    {
        StartGame(players);
        BeginRound(round);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    /// <summary>Open ground everywhere, and no enemy tanks.</summary>
    internal void ClearField()
    {
        Battlefield = new Battlefield();
        Tanks.RemoveAll(t => t.Computer);
        Shells.Clear();
        ToCome = 0;
    }

    internal void Put(Tank t, double x, double y, double angle)
    {
        (t.X, t.Y, t.Angle) = (x, y, angle);
        t.Safe = 0;
        t.Alive = true;
    }

    internal Tank PutEnemy(TankKind kind, double x, double y, double angle)
    {
        var t = new Tank { Kind = kind, X = x, Y = y, Angle = angle, Side = 1, Hp = kind == TankKind.Brute ? 2 : 1 };
        Tanks.Add(t);
        return t;
    }

    internal void SetCell(int c, int r, Ground g)
    {
        Battlefield.Cells[c, r] = g;
        Battlefield.Hp[c, r] = g == Ground.Wall ? Battlefield.WallHp : 0;
    }

    internal void SetToCome(int n) => ToCome = n;
    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
}

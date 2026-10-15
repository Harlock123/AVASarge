namespace AVASarge.Engine;

/// <summary>A player's controls: turn left or right (Dx), forward (Dy -1) or back (Dy 1), and fire.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>The screen: the battlefield below the score bar, laid out on a grid of cells.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Top = 64, Bottom = 768;
    public const double Cell = 32;
    public const int Cols = 32, Rows = 22;

    public static (double X, double Y) Centre(int c, int r) => ((c + .5) * Cell, Top + (r + .5) * Cell);
    public static (int C, int R) CellAt(double x, double y) => ((int)Math.Floor(x / Cell), (int)Math.Floor((y - Top) / Cell));
}

public enum Ground
{
    Open,
    /// <summary>A boulder: stops tanks and shells.</summary>
    Boulder,
    /// <summary>A deep rut: no tank can cross it, but shells fly over.</summary>
    Rut,
    /// <summary>A ruined wall: stops tanks and shells, and crumbles to rubble under fire.</summary>
    Wall,
    /// <summary>What's left of a wall: tanks can cross it, slowly.</summary>
    Rubble,
}

public enum TankKind
{
    /// <summary>Your tank (player one's).</summary>
    Sarge,
    /// <summary>Player two's tank, in a duel.</summary>
    Rival,
    /// <summary>Quick and careless.</summary>
    Scout,
    /// <summary>Steady, sharp-eyed, and quick to dodge.</summary>
    Hunter,
    /// <summary>Slow and heavy: it takes two hits.</summary>
    Brute,
}

public sealed class Tank
{
    public const double Radius = 13;

    public TankKind Kind;
    public double X, Y;
    /// <summary>Which way it points, clockwise from up.</summary>
    public double Angle;
    public bool Alive = true;
    public int Hp = 1;
    public double Reload, Safe, Rolling, Hurt;
    /// <summary>Which side it fights for: 0 player one, 1 player two (or the enemy).</summary>
    public int Side;

    // The computer's state, for the tanks it drives.
    public List<(int C, int R)> Path = [];
    public double Replan, Dodge, AimNoise, AimHold;
    /// <summary>How long it holds its aim before this shot.</summary>
    public double Steady = .05;
    public int DodgeDir = 1;

    public (double X, double Y) Ahead => (Math.Sin(Angle), -Math.Cos(Angle));
    public bool Computer => Kind is TankKind.Scout or TankKind.Hunter or TankKind.Brute;
}

public sealed class Shell
{
    public const double Speed = 380, Life = 1.8;

    public double X, Y, Vx, Vy, Age;
    public Tank Owner = null!;
    public bool Dead;
}

/// <summary>A blackened crater left where a shell burst.</summary>
public readonly record struct Scorch(double X, double Y, double Size);

public enum EffectKind { Score, Burst, Word, Blast, Dust }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.7, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}

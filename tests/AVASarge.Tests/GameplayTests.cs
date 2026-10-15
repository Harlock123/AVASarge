using AVASarge.Engine;

namespace AVASarge.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avasarge-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A one-player round on open ground, no enemy tanks unless a test puts one there, your tank facing right.</summary>
    private static GameEngine Game()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(1);
        g.ClearField();
        g.Put(g.Sarge, 200, 400, Math.PI / 2);
        return g;
    }

    private static readonly Controls Fire = new(Fire: true), Forward = new(Dy: -1);

    /// <summary>An enemy tank that won't fire (unless a test lets it).</summary>
    private static Tank Target(GameEngine g, TankKind kind, double x, double y)
    {
        var t = g.PutEnemy(kind, x, y, -Math.PI / 2);
        t.Steady = 99;
        return t;
    }

    // ───────────────────────────── the battlefield ─────────────────────────────

    [Fact]
    public void Every_battlefield_is_the_same_both_sides_with_every_spawn_reachable_and_clear()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            var b = Battlefield.Build(new Random(seed));
            for (int c = 0; c < Field.Cols; c++)
                for (int r = 0; r < Field.Rows; r++)
                    Assert.Equal(b.Cells[c, r], b.Cells[Field.Cols - 1 - c, r]);
            var d = b.Distances(Battlefield.LeftSpawn.C, Battlefield.LeftSpawn.R);
            Assert.True(d[Battlefield.RightSpawn.C, Battlefield.RightSpawn.R] >= 0);
            Assert.All(Battlefield.EnemySpawns, s => Assert.True(d[s.C, s.R] >= 0));
            var cells = b.Cells.Cast<Ground>().ToList();
            Assert.Contains(Ground.Boulder, cells);
            Assert.Contains(Ground.Rut, cells);
            Assert.Contains(Ground.Wall, cells);
        }
    }

    // ───────────────────────────── driving ─────────────────────────────

    [Fact]
    public void A_tank_rolls_forward_and_back_and_turns()
    {
        var g = Game();
        var s = g.Sarge;
        g.Run(60, Forward);
        Assert.Equal(200 + GameEngine.SpeedOf(TankKind.Sarge), s.X, 1);
        g.Run(60, new Controls(Dy: 1));
        Assert.Equal(200 + GameEngine.SpeedOf(TankKind.Sarge) * .4, s.X, 1);
        g.Run(30, new Controls(Dx: 1));
        Assert.Equal(Math.PI / 2 + GameEngine.TurnOf(TankKind.Sarge) * .5, s.Angle, 2);
    }

    [Fact]
    public void Boulders_and_ruts_stop_a_tank_and_rubble_slows_it()
    {
        foreach (var g0 in new[] { Ground.Boulder, Ground.Rut })
        {
            var g = Game();
            g.SetCell(10, 10, g0);
            g.SetCell(10, 11, g0);
            g.Run(120, Forward);
            Assert.InRange(g.Sarge.X, 300, 10 * Field.Cell - Tank.Radius + .5);
        }
        var r = Game();
        for (int c = 6; c < 14; c++) { r.SetCell(c, 10, Ground.Rubble); r.SetCell(c, 11, Ground.Rubble); }
        r.Run(60, Forward);
        Assert.InRange(r.Sarge.X, 200 + GameEngine.SpeedOf(TankKind.Sarge) * .55, 200 + GameEngine.SpeedOf(TankKind.Sarge) * .7);
    }

    // ───────────────────────────── shells ─────────────────────────────

    [Fact]
    public void One_shell_at_a_time_flies_over_ruts_and_bursts_on_boulders()
    {
        var g = Game();
        g.Run(1, Fire);
        Assert.Single(g.Shells);
        g.Run(10, Fire);
        Assert.Single(g.Shells);
        g.Idle(120);
        var t = Target(g, TankKind.Scout, 600, 400);
        g.SetCell(10, 10, Ground.Rut);
        g.SetCell(11, 10, Ground.Rut);
        g.Run(1, Fire);
        g.Idle(90);
        Assert.False(t.Alive);
        g.SetCell(10, 10, Ground.Boulder);
        var u = Target(g, TankKind.Scout, 600, 400);
        g.Run(1, Fire);
        g.Idle(90);
        Assert.True(u.Alive);
    }

    [Fact]
    public void A_ruined_wall_crumbles_to_rubble_under_fire()
    {
        var g = Game();
        g.SetCell(10, 10, Ground.Wall);
        for (int i = 0; i < Battlefield.WallHp; i++)
        {
            Assert.Equal(Ground.Wall, g.Battlefield.Cells[10, 10]);
            g.Run(1, Fire);
            g.Idle(60);
        }
        Assert.Equal(Ground.Rubble, g.Battlefield.Cells[10, 10]);
    }

    [Fact]
    public void Enemy_tanks_go_down_for_their_points_and_a_brute_takes_two()
    {
        var g = Game();
        Target(g, TankKind.Scout, 520, 400);
        g.Run(1, Fire);
        g.Idle(60);
        Assert.Equal(GameEngine.Points(TankKind.Scout), g.Score);
        var b = Target(g, TankKind.Brute, 520, 400);
        g.Run(1, Fire);
        g.Idle(60);
        Assert.True(b.Alive);
        Assert.Equal(1, b.Hp);
        g.Run(1, Fire);
        g.Idle(60);
        Assert.False(b.Alive);
        Assert.Equal(GameEngine.Points(TankKind.Scout) + GameEngine.Points(TankKind.Brute), g.Score);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    [Fact]
    public void A_computer_tank_works_its_way_round_to_a_clear_shot_and_fires()
    {
        var g = Game();
        // A wall of boulders between them, open at the far end.
        for (int r = 0; r < Field.Rows - 4; r++) g.SetCell(16, r, Ground.Boulder);
        g.Put(g.Sarge, 200, 200, Math.PI / 2);
        var e = g.PutEnemy(TankKind.Hunter, 800, 200, -Math.PI / 2);
        bool fired = false;
        for (int t = 0; t < 60 * 20 && !fired; t++)
        {
            g.Tick(default);
            fired = g.Shells.Any(s => s.Owner == e);
        }
        Assert.True(fired);
    }

    [Fact]
    public void An_enemy_shell_ends_your_tank_and_the_next_rolls_out_safe_with_the_enemy_pulled_back()
    {
        var g = Game();
        g.SetLives(3);
        g.PutEnemy(TankKind.Hunter, 500, 400, -Math.PI / 2);
        for (int t = 0; t < 600 && g.Phase == Phase.Play; t++) g.Tick(default);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal(2, g.Lives);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.True(g.Sarge.Alive);
        Assert.True(g.Sarge.Safe > 0);
        var (sx, sy) = Field.Centre(Battlefield.LeftSpawn.C, Battlefield.LeftSpawn.R);
        Assert.Equal((sx, sy), (g.Sarge.X, g.Sarge.Y));
        Assert.DoesNotContain(g.Tanks, t => t.Computer);
        Assert.Equal(1, g.ToCome);
    }

    [Fact]
    public void Losing_the_last_tank_ends_the_game()
    {
        var g = Game();
        g.SetLives(1);
        g.PutEnemy(TankKind.Hunter, 500, 400, -Math.PI / 2);
        for (int t = 0; t < 600 && g.Phase == Phase.Play; t++) g.Tick(default);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameState.GameOver, g.State);
    }

    // ───────────────────────────── rounds ─────────────────────────────

    [Fact]
    public void Enemy_tanks_roll_out_a_few_at_a_time_and_destroying_them_all_wins_the_round()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        g.SetLives(99);
        g.Sarge.Safe = 1e9;
        Assert.Equal(GameEngine.EnemiesIn(1), g.ToCome);
        for (int t = 0; t < 60 * 12; t++)
        {
            g.Tick(default);
            Assert.True(g.Tanks.Count(x => x.Computer) <= GameEngine.OnField(1));
        }
        Assert.True(g.ToCome < GameEngine.EnemiesIn(1));
        g.SetToCome(0);
        g.Tanks.RemoveAll(x => x.Computer);
        if (g.Phase != Phase.Play) g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        g.Idle(1);
        Assert.Equal(Phase.Clear, g.Phase);
        g.Idle((int)(60 * GameEngine.ClearSeconds) + 5);
        Assert.Equal(2, g.Round);
        Assert.Equal(GameEngine.EnemiesIn(2), g.ToCome);
    }

    [Fact]
    public void Extra_tanks_come_at_20000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.FirstExtraLife - 100);
        Target(g, TankKind.Scout, 520, 400);
        g.Run(1, Fire);
        g.Idle(60);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── the duel ─────────────────────────────

    [Fact]
    public void Pressing_2_starts_a_duel_with_player_two_on_the_other_side()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, false, default, true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(2, g.Players);
        Assert.NotNull(g.Rival);
        Assert.True(g.Rival!.X > Field.Width / 2);
        Assert.Equal(0, g.ToCome);
    }

    [Fact]
    public void In_a_duel_each_hit_scores_for_the_shooter_and_the_first_to_five_wins()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1, players: 2);
        g.ClearField();
        g.Put(g.Sarge, 200, 400, Math.PI / 2);
        g.Put(g.Rival!, 600, 400, -Math.PI / 2);
        g.Run(1, default, Fire);
        g.Idle(60);
        Assert.Equal(1, g.Wins[1]);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.True(g.Sarge.Alive);
        for (int i = 0; i < GameEngine.DuelTarget - 1; i++)
        {
            g.Put(g.Sarge, 200, 400, Math.PI / 2);
            g.Put(g.Rival!, 600, 400, -Math.PI / 2);
            g.Idle(30);
            g.Run(1, default, Fire);
            g.Idle(60);
            if (g.Phase == Phase.Dying) g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        }
        Assert.Equal(GameEngine.DuelTarget, g.Wins[1]);
        Assert.Equal("PLAYER 2 WINS", g.Message);
        g.Idle((int)(60 * (GameEngine.ClearSeconds + 4)));
        Assert.Equal(GameState.Title, g.State);
    }

    // ───────────────────────────── around the game ─────────────────────────────

    [Fact]
    public void The_computer_holds_its_own_at_your_tank()
    {
        var g = new GameEngine(1, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        g.SetLives(9);
        for (int t = 0; t < 60 * 150 && g.State == GameState.Playing && g.Round < 3; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Round >= 3, $"round {g.Round}");
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_back()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 25);
        Assert.True(g.Score > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 25)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        static (int, double, double, int) Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 40; t++) g.Tick(g.Pilot.Decide(g));
            return (g.Score, g.Sarge.X, g.Sarge.Y, g.Round);
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.Go(3);
        g.ClearField();
        g.SetScore(99999);
        g.SetLives(1);
        g.Put(g.Sarge, 200, 400, Math.PI / 2);
        g.PutEnemy(TankKind.Hunter, 500, 400, -Math.PI / 2);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 8)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "SRG") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("SRG", 99999, 3), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}

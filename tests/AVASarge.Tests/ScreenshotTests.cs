using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASarge.Engine;
using AVASarge.Views;

namespace AVASarge.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set SARGE_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avasarge-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("SARGE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVASarge-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("SGT", 84250, 9);
        g.HighScores.Insert("AVA", 52800, 6);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_battle()
    {
        var (w, c, g) = Open(2);
        g.StartGame();
        g.SetLives(9);
        g.Idle(60);
        c.Advance(.5);
        Shot(w, c, "round-start");
        Assert.True(PlayUntil(g, c, g => g.Round == 3 && g.Tanks.Count(t => t.Computer) >= 3 && g.Shells.Count >= 2 && g.Sarge.Alive, 300));
        Shot(w, c, "battle");
        Assert.True(PlayUntil(g, c, g => g.Effects.Any(e => e.Kind == EffectKind.Blast && e.Age > .2 && e.Age < .5), 200));
        Shot(w, c, "blast");
    }

    [AvaloniaFact]
    public void A_duel()
    {
        var (w, c, g) = Open(4);
        g.Go(1, players: 2);
        // Both sides driven by the computer, for the picture.
        for (int t = 0; t < 60 * 60; t++)
        {
            if (g.Wins[0] + g.Wins[1] >= 3 && g.Phase == Phase.Play && g.Shells.Count >= 1 && g.PhaseTime > 2) break;
            var c1 = TankAi.Decide(g, g.Sarge, g.Rival, Field.Step, true, true);
            var c2 = TankAi.Decide(g, g.Rival!, g.Sarge, Field.Step, true, true);
            g.Tick(c1, false, c2);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        Shot(w, c, "duel");
    }
}

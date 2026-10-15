using AVASarge.Engine;

namespace AVASarge.Tests;

/// <summary>How the computer gets on at your tank, round by round (set SARGE_PROBE to run it, for tuning; SARGE_PROBE=lazy for a player who just sits and fires).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("SARGE_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "sarge-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var rounds = new List<string>();
            var last = g.Phase;
            int round = 1, t = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Round <= 6; t++)
            {
                var c = mode == "lazy" ? new Controls(t % 240 < 120 ? 1 : 0, 0, t % 30 < 2) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"R{g.Round} {g.Cause}"] = deaths.GetValueOrDefault($"R{g.Round} {g.Cause}") + 1;
                if (g.Round != round) { rounds.Add($"R{round}:{(t - start) / 60:0}s"); round = g.Round; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: round {g.Round} after {t / 60:0}s score {g.Score} enemy left {g.ToCome + g.Tanks.Count(x => x.Computer)} | {string.Join(" ", rounds)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
        }
    }
}

using Avalonia.Input;

namespace AVASarge.Engine;

/// <summary>
/// Keyboard and gamepads boiled down to each player's controls: turn left or right, forward or back, and fire.
/// Player one has the arrow keys (or the numeric keypad) with Space or Right Ctrl, and the first gamepad; player
/// two has W A S D with Left Shift (or Tab or Q), and the second gamepad. Enter or 1 starts a one-player game,
/// 2 a two-player one.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly ControllerManager _pad = new(0), _pad2 = new(1);

    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
    }

    /// <summary>A key comes up. The key it went down as is let go too: with the keyboard option
    /// shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock, and the Shift would stay held.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        _held.Remove(key);
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was)) _held.Remove(was);
    }

    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
        _pad2.Update();
    }

    private static Controls FromPad(ControllerManager p, int dx, int dy, bool fire)
    {
        if (dx == 0) dx = p.DPadLeft || p.LeftStickX < -0.4f ? -1 : p.DPadRight || p.LeftStickX > 0.4f ? 1 : 0;
        if (dy == 0) dy = p.DPadUp || p.LeftStickY < -0.4f ? -1 : p.DPadDown || p.LeftStickY > 0.4f ? 1 : 0;
        return new Controls(dx, dy, fire || p.ButtonA || p.ButtonB || p.ButtonX);
    }

    public Controls Controls
    {
        get
        {
            int dx = (Any(Key.Right, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.NumPad4) ? 1 : 0);
            int dy = (Any(Key.Down, Key.NumPad2, Key.NumPad5) ? 1 : 0) - (Any(Key.Up, Key.NumPad8) ? 1 : 0);
            return FromPad(_pad, dx, dy, Any(Key.Space, Key.RightCtrl, Key.Z, Key.NumPad0));
        }
    }

    public Controls Controls2
    {
        get
        {
            int dx = (Any(Key.D) ? 1 : 0) - (Any(Key.A) ? 1 : 0);
            int dy = (Any(Key.S) ? 1 : 0) - (Any(Key.W) ? 1 : 0);
            return FromPad(_pad2, dx, dy, Any(Key.LeftShift, Key.Tab, Key.Q));
        }
    }

    public bool Start => Any(Key.Enter, Key.D1) || _pad.ButtonStart;
    public bool Start2 => Any(Key.D2) || _pad2.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart || _pad2.ButtonStart;
    public bool PadBack => _pad.ButtonBack || _pad2.ButtonBack;

    public void Dispose()
    {
        _pad.Dispose();
        _pad2.Dispose();
    }
}

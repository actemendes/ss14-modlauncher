using System.Numerics;

namespace SS14LocalMods.DeathRattle;

// A server lunge has no victim. Correlation is labelled uncertain, never confirmed Origin.
public sealed class FaunaEvidence
{
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(800);
    private readonly Dictionary<object, (Attacker Attacker, TimeSpan Time)> _swings = new();
    private object? _player;
    private TimeSpan? _damage;

    public void SetPlayer(object? player)
    {
        if (Equals(_player, player)) return;
        Clear();
        _player = player;
    }
    public void Clear() { _player = null; _damage = null; _swings.Clear(); }

    public void Damage(object player, TimeSpan now)
    {
        SetPlayer(player);
        _damage = now;
    }

    public void Swing(object player, object source, Attacker attacker, Vector2 offset, Vector2 targetOffset, TimeSpan now)
    {
        SetPlayer(player);
        if (Equals(source, player) || attacker.IsHumanoid || offset.LengthSquared() < 0.01f
            || !float.IsFinite(offset.X) || !float.IsFinite(offset.Y)
            || !float.IsFinite(targetOffset.X) || !float.IsFinite(targetOffset.Y)
            || targetOffset.LengthSquared() > 9 || Vector2.DistanceSquared(offset, targetOffset) > 0.3025f)
            return;
        Prune(now);
        _swings[source] = (attacker with { Confirmed = false }, now);
    }

    public Attacker? Match(object player, TimeSpan now)
    {
        SetPlayer(player);
        Prune(now);
        if (_damage is not { } damage || now < damage || now - damage > Window || _swings.Count != 1) return null;
        var swing = _swings.Values.Single();
        return (swing.Time - damage).Duration() <= Window ? swing.Attacker : null;
    }
    private void Prune(TimeSpan now)
    {
        foreach (var key in _swings.Where(p => now < p.Value.Time || now - p.Value.Time > Window).Select(p => p.Key).ToArray())
            _swings.Remove(key);
    }
}

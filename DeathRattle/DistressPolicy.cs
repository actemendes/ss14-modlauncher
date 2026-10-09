namespace SS14LocalMods.DeathRattle;

public sealed record Attacker(string Name, bool IsHumanoid);

// Monotonic local time, one attempt per awful-health episode, shared cooldown across bodies.
public sealed class DistressPolicy
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan AttackerLifetime = TimeSpan.FromSeconds(5);
    private object? _player;
    private bool _announced;
    private TimeSpan? _lastSent;
    private (Attacker Attacker, TimeSpan Time)? _attacker;

    public void SetPlayer(object? player)
    {
        if (Equals(_player, player)) return;
        _player = player;
        _announced = false;
        _attacker = null;
    }

    public void Clear()
    {
        _player = null;
        _announced = false;
        _attacker = null;
    }

    public void ClearAttacker() => _attacker = null;

    public void RecordDamage(object player, Attacker? attacker, TimeSpan now)
    {
        SetPlayer(player);
        // An unknown new source must not blame an earlier attacker.
        _attacker = attacker == null ? null : (attacker, now);
    }

    public string? Observe(object? player, bool awful, TimeSpan now, string? language = "ru")
    {
        SetPlayer(player);
        if (player == null) return null;
        if (!awful)
        {
            if (_announced) _attacker = null;
            _announced = false;
            return null;
        }
        if (_announced || _lastSent is { } last && now - last < Cooldown) return null;
        // Reserve before calling chat: re-entrant updates and failed attempts cannot spam.
        _announced = true;
        _lastSent = now;
        var english = language == "en";
        var message = english ? "Help, I'm dying!" : "Помогите, я умираю!";
        if (_attacker is not { } source || now < source.Time || now - source.Time > AttackerLifetime)
            return message;
        var name = CleanName(source.Attacker.Name);
        if (name.Length == 0) return message;
        return english ? "Help, " + name + " is killing me!" : "Помогите, меня убивает " + name + "!";
    }

    private static string CleanName(string name) => new string(name.Where(c => !char.IsControl(c)
        && c is not ('\u2028' or '\u2029')).Take(80).ToArray()).Trim();
}

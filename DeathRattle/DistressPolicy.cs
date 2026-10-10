namespace SS14LocalMods.DeathRattle;

public sealed record Attacker(string Name, bool IsHumanoid, bool Confirmed = true);

// All times are monotonic. Damage is total injury, so decreasing values mean healing.
public sealed class DistressPolicy
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan AttackerLifetime = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DamageLifetime = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan SafeRecoveryTime = TimeSpan.FromSeconds(5);
    public const double InjuryThreshold = 5, RecoveryThreshold = 10;
    private object? _player;
    private bool _announced, _recovering;
    private double? _damage;
    private short? _severity;
    private double _minimum, _burstMinimum;
    private TimeSpan? _lastSent, _lastWorsened, _pending, _safeSince;
    private (Attacker Attacker, TimeSpan Time)? _attacker;

    public void SetPlayer(object? player)
    {
        if (Equals(_player, player)) return;
        Clear();
        _player = player;
    }

    public void Clear()
    {
        _player = null;
        _announced = _recovering = false;
        _damage = null;
        _severity = null;
        _minimum = _burstMinimum = 0;
        _lastWorsened = _pending = _safeSince = null;
        _attacker = null;
        // Keep the send cooldown even across detachment and body changes.
    }

    public void ClearAttacker() => _attacker = null;

    public void RecordDamage(object player, Attacker? attacker, TimeSpan now)
    {
        SetPlayer(player);
        _attacker = attacker == null ? null : (attacker, now);
    }

    public void ObserveDamage(object player, double total, TimeSpan now)
    {
        SetPlayer(player);
        if (!double.IsFinite(total) || total < 0) return;
        if (_damage is not { } previous)
        {
            _damage = _minimum = _burstMinimum = total;
            return; // Attaching to an already injured body is not a new injury.
        }
        if (_lastWorsened is not { } recent || now < recent || now - recent > DamageLifetime)
            _burstMinimum = previous;
        _damage = total;
        if (total < previous - 0.01)
        {
            _minimum = _burstMinimum = total;
            _recovering = true;
            _announced = false;
            _pending = _lastWorsened = null;
            _attacker = null;
        }
        else if (total > previous + 0.01)
            _lastWorsened = now;
    }

    public string? Observe(object? player, short? severity, bool incapacitated, double? total,
        TimeSpan now, string? language = "ru")
    {
        SetPlayer(player);
        if (player == null) return null;
        if (total is { } sample) ObserveDamage(player, sample, now);
        if (incapacitated)
        {
            _announced = false;
            _recovering = true;
            if (_damage is { } damage) _minimum = _burstMinimum = damage;
            _pending = _lastWorsened = _safeSince = null;
            _attacker = null;
            _severity = null;
            return null;
        }
        if (severity is { } level)
        {
            // An improving alert is additional evidence of recovery even if injury replication is late.
            if (_severity is { } previousLevel && level < previousLevel)
            {
                _recovering = true;
                _announced = false;
                if (_damage is { } damage) _minimum = _burstMinimum = damage;
                _pending = _lastWorsened = null;
                _attacker = null;
            }
            _severity = level;
        }
        if (severity is >= 0 and <= 2)
        {
            _safeSince ??= now;
            if (now - _safeSince >= SafeRecoveryTime)
            {
                _announced = _recovering = false;
                if (_damage is { } damage) _minimum = _burstMinimum = damage;
                _attacker = null;
            }
        }
        else _safeSince = null;
        if (severity != 4 || total is not { } current || !double.IsFinite(current) || current < 0)
        {
            _pending = null;
            return null;
        }
        // Healing protection persists even after the cooldown. Only a fresh net loss can break it.
        if (_announced || current - _minimum < (_recovering ? RecoveryThreshold : InjuryThreshold)
            || current - _burstMinimum < InjuryThreshold
            || _lastWorsened is not { } worsened || now < worsened || now - worsened > DamageLifetime)
        {
            _pending = null;
            return null;
        }
        _pending ??= now;
        if (now - _pending < SettleTime || _lastSent is { } last && now - last < Cooldown) return null;
        _announced = true;
        _lastSent = now;
        _pending = null;
        var english = language == "en";
        var message = english ? "Help, I'm dying!" : "Помогите, я умираю!";
        if (_attacker is not { } source || now < source.Time || now - source.Time > AttackerLifetime)
            return message;
        var name = CleanName(source.Attacker.Name);
        if (name.Length == 0) return message;
        if (!source.Attacker.Confirmed)
            return english ? "Help, I think " + name + " is attacking me!" : "Помогите, похоже, меня атакует " + name + "!";
        return english ? "Help, " + name + " is killing me!" : "Помогите, меня убивает " + name + "!";
    }

    private static string CleanName(string name) => new string(name.Where(c => !char.IsControl(c)
        && c is not ('\u2028' or '\u2029')).Take(80).ToArray()).Trim();
}

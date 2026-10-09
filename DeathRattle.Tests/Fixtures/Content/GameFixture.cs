using System.Runtime.CompilerServices;
using Robust.Shared.GameObjects;

namespace Content.Shared.Chat { public enum ChatSelectChannel { Local, Radio } }
namespace Content.Shared.Alert
{
    public abstract class AlertsSystem
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual void Update(float frameTime) { }
    }
    public readonly record struct AlertKey(string Id);
    public struct AlertState { public short? Severity; public string Type; }
}
namespace Content.Shared.Humanoid { public class HumanoidProfileComponent : IComponent; }
namespace Content.Shared.IdentityManagement
{
    public static class Identity
    {
        public static readonly Dictionary<EntityUid, string> Names = new();
        public static string Name(EntityUid uid, IEntityManager ent, EntityUid? viewer = null) => Names[uid];
    }
}
namespace Content.Shared.FixedPoint
{
    public readonly struct FixedPoint2(float value) { public float Float() => value; }
}
namespace Content.Shared.Damage.Components { public class DamageableComponent; }
namespace Content.Shared.Damage
{
    public class DamageSpecifier(float total)
    {
        public Content.Shared.FixedPoint.FixedPoint2 GetTotal() => new(total);
    }
}
namespace Content.Shared.Damage.Systems
{
    public class DamageableSystem
    {
        public Action? DuringDamage;
        public void Change(EntityUid victim, float delta, EntityUid? origin) =>
            OnEntityDamageChanged(new(victim, new()), new(delta), true, origin);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void OnEntityDamageChanged(Entity<Components.DamageableComponent> ent,
            DamageSpecifier? damageDelta = null, bool interruptsDoAfters = true, EntityUid? origin = null) => DuringDamage?.Invoke();
    }
}

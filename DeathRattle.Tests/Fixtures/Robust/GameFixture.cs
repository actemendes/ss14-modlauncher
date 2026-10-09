// Minimal test-only contracts; no game binaries are redistributed.
namespace Robust.Shared.IoC
{
    public static class IoCManager
    {
        public static readonly Dictionary<Type, object> Services = new();
        public static object ResolveType(Type type) => Services[type];
    }
}
namespace Robust.Shared.GameObjects
{
    public readonly record struct EntityUid(int Id);
    public struct Entity<T>(EntityUid owner, T comp)
    {
        public EntityUid Owner = owner;
        public T Comp = comp;
    }
    public interface IComponent;
    public interface IEntityManager
    {
        bool TryGetComponent(EntityUid uid, Type type, out IComponent? component);
    }
    public class EntityManager : IEntityManager
    {
        public readonly Dictionary<(EntityUid, Type), IComponent> Components = new();
        public bool TryGetComponent(EntityUid uid, Type type, out IComponent? component) => Components.TryGetValue((uid, type), out component);
    }
}
namespace Robust.Shared.Timing
{
    public interface IGameTiming { bool IsFirstTimePredicted { get; } }
    public class GameTiming : IGameTiming { public bool IsFirstTimePredicted { get; set; } = true; }
}

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Robust.Shared.Graphics
{
    public class Eye
    {
        private bool _fov = true;
        private Vector2 _scale = Vector2.One;
        public bool DrawFov { [MethodImpl(MethodImplOptions.NoInlining)] get => _fov; set => _fov = value; }
        public Vector2 Zoom
        {
            get => Vector2.One / _scale;
            [MethodImpl(MethodImplOptions.NoInlining)] set => _scale = Vector2.One / value;
        }
        public Vector2 Scale
        {
            get => _scale;
            [MethodImpl(MethodImplOptions.NoInlining)] set => _scale = value;
        }
    }
}
namespace Robust.Shared.IoC
{
    public static class IoCManager
    {
        public static Dictionary<Type, object> Services = [];
        public static object ResolveType(Type type) => Services[type];
    }
}
namespace Robust.Shared.Player
{
    public interface ICommonSession { }
}
namespace Robust.Shared.Timing
{
    public interface IGameTiming { bool IsFirstTimePredicted { get; } }
    public class GameTiming : IGameTiming { public bool IsFirstTimePredicted { get; set; } = true; }
}

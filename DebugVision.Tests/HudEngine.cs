using System.Runtime.CompilerServices;

namespace Robust.Client.Graphics
{
    public interface IOverlayManager
    {
        bool AddOverlay(Overlay overlay);
        bool RemoveOverlay(Overlay overlay);
        bool HasOverlay(Type type);
        Overlay GetOverlay(Type type);
    }
    public sealed class OverlayManager : IOverlayManager
    {
        public Dictionary<Type, Overlay> Overlays = [];
        public bool AddOverlay(Overlay overlay) => Overlays.TryAdd(overlay.GetType(), overlay);
        public bool RemoveOverlay(Overlay overlay) => Overlays.Remove(overlay.GetType());
        public bool HasOverlay(Type type) => Overlays.ContainsKey(type);
        public Overlay GetOverlay(Type type) => Overlays[type];
    }
}
namespace Content.Shared.StatusIcon
{
    public sealed class HealthIconPrototype;
}

namespace Content.Client.Overlays
{
    public abstract class EquipmentHudSystem<T>
    {
        public bool IsActive { get; private set; }
        public void SetEquipment(bool active) => IsActive = active;
        protected virtual void DeactivateInternal() { }
    }
    public sealed class ShowJobIconsSystem : EquipmentHudSystem<object>;
    public sealed class ShowHealthBarsSystem(Robust.Client.Graphics.OverlayManager manager) : EquipmentHudSystem<int>
    {
        private readonly Robust.Client.Graphics.IOverlayManager _overlayMan = manager;
        private readonly EntityHealthBarOverlay _overlay = new();
        public EntityHealthBarOverlay Bar => _overlay;
        public void Equip(params string[] containers)
        {
            SetEquipment(true);
            _overlay.DamageContainers.Clear(); _overlay.DamageContainers.UnionWith(containers);
            _overlay.StatusIcon = new("EquipmentIcon"); manager.AddOverlay(_overlay);
        }
        public void Unequip() { SetEquipment(false); DeactivateInternal(); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        protected override void DeactivateInternal() { _overlay.DamageContainers.Clear(); manager.RemoveOverlay(_overlay); }
    }
    public sealed class EntityHealthBarOverlay : Robust.Client.Graphics.Overlay
    {
        public HashSet<string> DamageContainers = [];
        public Robust.Shared.Prototypes.ProtoId<Content.Shared.StatusIcon.HealthIconPrototype>? StatusIcon;
        public string? Container = "Biological";
        public bool Visible = true, Fail;
        public string? UsedVisibilityIcon;
        public float? Ratio;
        public float Damage, CriticalThreshold = 100, DeadThreshold = 200;
        [MethodImpl(MethodImplOptions.NoInlining)]
        protected override void Draw(in Robust.Client.Graphics.OverlayDrawArgs args)
        {
            UsedVisibilityIcon = StatusIcon?.Id; Ratio = null;
            if (Fail) throw new InvalidOperationException("Native renderer failed");
            if (!Visible || Container == null || !DamageContainers.Contains(Container)) return;
            Ratio = Damage < CriticalThreshold ? 1 - Damage / CriticalThreshold
                : Damage < DeadThreshold ? 1 - (Damage - CriticalThreshold) / (DeadThreshold - CriticalThreshold) : 0;
            Draws++;
        }
    }
    public sealed class ShowHealthIconsSystem : EquipmentHudSystem<string>
    {
        public HashSet<string> DamageContainers = [];
        public List<string> Collect(string? container, string state = "Healthy", bool fail = false)
        {
            var icons = new List<string>(); OnGetStatusIconsEvent((container, state, fail), ref icons); return icons;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void OnGetStatusIconsEvent((string? Container, string State, bool Fail) entity, ref List<string> icons)
        {
            if (!IsActive) return;
            if (entity.Fail) throw new InvalidOperationException("Prototype lookup failed");
            if (entity.Container != null && DamageContainers.Contains(entity.Container)) icons.Add(entity.State);
        }
    }
}
namespace Robust.Client.GameObjects
{
    public sealed class EntitySystemManager : Robust.Shared.GameObjects.IEntitySystemManager
    {
        public Dictionary<Type, object> Systems = [];
        public object GetEntitySystem(Type type) => Systems[type];
    }
}
namespace Content.Client.Access.Systems
{
    public sealed class JobStatusSystem(Content.Client.Overlays.ShowJobIconsSystem hud)
    {
        private readonly Content.Client.Overlays.ShowJobIconsSystem _showJobIcons = hud;
        public bool CrewHud;
        public List<string> Collect(string? job, bool fail = false)
        {
            var icons = new List<string>(); OnGetStatusIconsEvent((job, fail), ref icons); return icons;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void OnGetStatusIconsEvent((string? Job, bool Fail) entity, ref List<string> icons)
        {
            if (_showJobIcons.IsActive && entity.Job != null)
            {
                if (entity.Fail) throw new InvalidOperationException("Prototype lookup failed");
                icons.Add(entity.Job);
            }
            if (CrewHud) icons.Add("Crew border");
        }
    }
}

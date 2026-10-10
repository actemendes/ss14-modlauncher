using System.Runtime.CompilerServices;

namespace Content.Client.Overlays
{
    public abstract class EquipmentHudSystem<T>
    {
        public bool IsActive { get; private set; }
        public void SetEquipment(bool active) => IsActive = active;
    }
    public sealed class ShowJobIconsSystem : EquipmentHudSystem<object>;
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

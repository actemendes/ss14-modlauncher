using System.Runtime.CompilerServices;
using Content.Shared.Alert;
using Content.Shared.Chat;
using Robust.Shared.GameObjects;

namespace Robust.Client.Player
{
    public interface IPlayerManager { EntityUid? LocalEntity { get; } }
    public class PlayerManager : IPlayerManager { public EntityUid? LocalEntity { get; set; } }
}
namespace Content.Client.Chat.Managers
{
    public interface IChatManager { void SendMessage(string text, ChatSelectChannel channel); }
    public class ChatManager : IChatManager
    {
        public readonly List<(string Text, ChatSelectChannel Channel)> Sent = new();
        public bool Throw;
        public void SendMessage(string text, ChatSelectChannel channel)
        {
            if (Throw) throw new InvalidOperationException("fixture send failure");
            Sent.Add((text, channel));
        }
    }
}
namespace Content.Client.Alerts
{
    public class ClientAlertsSystem(Robust.Client.Player.IPlayerManager player) : Content.Shared.Alert.AlertsSystem
    {
        private readonly Robust.Client.Player.IPlayerManager _playerManager = player;
        public IReadOnlyDictionary<AlertKey, AlertState>? ActiveAlerts { get; set; }
    }
}
namespace Content.Client.UserInterface.Systems.Alerts
{
    public class AlertsUIController(Robust.Client.Player.IPlayerManager player)
    {
        private readonly Robust.Client.Player.IPlayerManager _player = player;
        public int NativeSyncs;
        public void Sync(string type, short? severity) => SystemOnSyncAlerts(this,
            new Dictionary<AlertKey, AlertState> { [new(type)] = new() { Type = type, Severity = severity } });
        public void Clear() => SystemOnClearAlerts(this, EventArgs.Empty);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SystemOnSyncAlerts(object? sender, IReadOnlyDictionary<AlertKey, AlertState> e) => NativeSyncs++;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SystemOnClearAlerts(object? sender, EventArgs e) { }
    }
}

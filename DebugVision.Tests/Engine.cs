using System.Numerics;
using System.Runtime.CompilerServices;
using Robust.Shared.Graphics;
using Robust.Shared.Player;
using Robust.Shared.IoC;

namespace Robust.Client.Graphics
{
    public interface IEyeManager { }
    public interface ILightManager { }
    public class EyeManager : IEyeManager
    {
        private Eye _eye = new();
        public Eye CurrentEye { get => _eye; [MethodImpl(MethodImplOptions.NoInlining)] set => _eye = value; }
    }
    public class LightManager : ILightManager
    {
        public bool Enabled { get; set; } = true;
        private bool _lighting = true, _shadows = true;
        public bool DrawLighting { [MethodImpl(MethodImplOptions.NoInlining)] get => _lighting; set => _lighting = value; }
        public bool DrawShadows { [MethodImpl(MethodImplOptions.NoInlining)] get => _shadows; set => _shadows = value; }
    }
}
namespace Robust.Client.Graphics
{
    public readonly ref struct OverlayDrawArgs { }
    public abstract class Overlay
    {
        public int Draws, Frames;
        public bool Active = true;
        public void Render() { if (BeforeDraw()) Draw(default); }
        public void FrameUpdate() => Frames++;
        protected virtual bool BeforeDraw() => Active;
        protected abstract void Draw(in OverlayDrawArgs args);
    }
    public sealed class UnrelatedOverlay : Overlay
    {
        [MethodImpl(MethodImplOptions.NoInlining)] protected override void Draw(in OverlayDrawArgs args) => Draws++;
    }
}
namespace Content.Shared.Eye.Blinding.Components
{
    public class BlindableComponent { public bool IsBlind = true, LightSetup, GraceFrame; public int EyeDamage = 9; }
}
namespace Content.Client.Flash
{
    public sealed class FlashOverlay : Robust.Client.Graphics.Overlay
    {
        [MethodImpl(MethodImplOptions.NoInlining)] protected override void Draw(in Robust.Client.Graphics.OverlayDrawArgs args) => Draws++;
    }
}
namespace Content.Client.Eye.Blinding
{
    public sealed class BlurryVisionOverlay : Robust.Client.Graphics.Overlay
    {
        [MethodImpl(MethodImplOptions.NoInlining)] protected override void Draw(in Robust.Client.Graphics.OverlayDrawArgs args) => Draws++;
    }
    public sealed class BlindOverlay : Robust.Client.Graphics.Overlay
    {
        private readonly Content.Shared.Eye.Blinding.Components.BlindableComponent _blindableComponent = new();
        private readonly Robust.Client.Graphics.LightManager _lightManager = (Robust.Client.Graphics.LightManager)IoCManager.ResolveType(typeof(Robust.Client.Graphics.ILightManager));
        public Content.Shared.Eye.Blinding.Components.BlindableComponent Blindable => _blindableComponent;
        protected override bool BeforeDraw()
        {
            if (!_blindableComponent.IsBlind && _blindableComponent.LightSetup)
            {
                _lightManager.Enabled = true; _blindableComponent.LightSetup = false; _blindableComponent.GraceFrame = true;
                return true;
            }
            return _blindableComponent.IsBlind;
        }
        [MethodImpl(MethodImplOptions.NoInlining)] protected override void Draw(in Robust.Client.Graphics.OverlayDrawArgs args)
        {
            Draws++;
            if (!_blindableComponent.GraceFrame) { _blindableComponent.LightSetup = true; _lightManager.Enabled = false; }
            else _blindableComponent.GraceFrame = false;
        }
    }
}
namespace Content.Client.Drunk
{
    public sealed class DrunkOverlay : Robust.Client.Graphics.Overlay
    {
        [MethodImpl(MethodImplOptions.NoInlining)] protected override void Draw(in Robust.Client.Graphics.OverlayDrawArgs args) => Draws++;
    }
}
namespace Content.Client.Drowsiness
{
    public sealed class DrowsinessOverlay : Robust.Client.Graphics.Overlay
    {
        [MethodImpl(MethodImplOptions.NoInlining)] protected override void Draw(in Robust.Client.Graphics.OverlayDrawArgs args) => Draws++;
    }
}
namespace Content.Client.Drugs
{
    public sealed class RainbowOverlay : Robust.Client.Graphics.Overlay
    {
        [MethodImpl(MethodImplOptions.NoInlining)] protected override void Draw(in Robust.Client.Graphics.OverlayDrawArgs args) => Draws++;
    }
}
namespace Content.Client.UserInterface.Systems.DamageOverlays.Overlays
{
    public sealed class DamageOverlay : Robust.Client.Graphics.Overlay
    {
        [MethodImpl(MethodImplOptions.NoInlining)] protected override void Draw(in Robust.Client.Graphics.OverlayDrawArgs args) => Draws++;
    }
}
namespace Robust.Client.Input
{
    public class KeyEventArgs
    {
        public string Key = "";
        public bool Alt, Control, Shift, IsRepeat;
    }
    public class InputManager
    {
        public int Downs, Ups;
        public Dictionary<string, Action> Bindings = [];
        [MethodImpl(MethodImplOptions.NoInlining)] public void KeyDown(KeyEventArgs args)
        {
            Downs++;
            if (Bindings.TryGetValue(args.Key, out var action)) action();
        }
        [MethodImpl(MethodImplOptions.NoInlining)] public void KeyUp(KeyEventArgs args) => Ups++;
    }
}
namespace Robust.Client.Player
{
    public interface IPlayerManager { }
    public class Session : ICommonSession { }
    public class PlayerManager : IPlayerManager { public ICommonSession? LocalSession { get; set; } = new Session(); }
}
namespace Content.Shared.Movement.Systems
{
    public class SharedContentEyeSystem
    {
        public const float ZoomMod = 1.5f;
        public int NativeCalls;
        public float TargetZoom = 1;
        [MethodImpl(MethodImplOptions.NoInlining)] private void ZoomIn(ICommonSession? session) => Apply(session, TargetZoom / ZoomMod);
        [MethodImpl(MethodImplOptions.NoInlining)] private void ZoomOut(ICommonSession? session) => Apply(session, TargetZoom * ZoomMod);
        [MethodImpl(MethodImplOptions.NoInlining)] private void ResetZoom(ICommonSession? session) => Apply(session, 1);
        public void ResetZoom(int entity) { }
        private void Apply(ICommonSession? session, float zoom)
        {
            NativeCalls++;
            var player = (Robust.Client.Player.PlayerManager)IoCManager.ResolveType(typeof(Robust.Client.Player.IPlayerManager));
            if (session == null || !ReferenceEquals(session, player.LocalSession)) return;
            TargetZoom = Math.Clamp(zoom, 0.3f, 1);
            var manager = (Robust.Client.Graphics.EyeManager)IoCManager.ResolveType(typeof(Robust.Client.Graphics.IEyeManager));
            manager.CurrentEye.Zoom = new Vector2(TargetZoom);
        }
        public void RunCommand(string command, ICommonSession? session)
        {
            if (command == "in") ZoomIn(session);
            else if (command == "out") ZoomOut(session);
            else ResetZoom(session);
        }
    }
}
namespace Content.Client.Movement.Systems
{
    public class ContentEyeSystem : Content.Shared.Movement.Systems.SharedContentEyeSystem { }
}
namespace Content.Client.Eye
{
    public class EyeLerpingSystem
    {
        public int Frames;
        [MethodImpl(MethodImplOptions.NoInlining)] public void FrameUpdate(float frameTime) => Frames++;
    }
}
namespace Content.Client.Gameplay
{
    public class GameplayState
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public void Startup() { }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Shutdown() { }
    }
}
namespace Robust.Client.UserInterface
{
    public interface IUserInterfaceManager { }
    public class UserInterfaceManager : IUserInterfaceManager { public object? KeyboardFocused { get; set; } }
    public class Control
    {
        public bool HorizontalExpand { get; set; }
        public bool VerticalExpand { get; set; }
        public string HorizontalAlignment { get; set; } = "";
        public string VerticalAlignment { get; set; } = "";
        public bool Disposed { get; private set; }
        public List<Control> Children { get; } = [];
        public void AddChild(Control child) => Children.Add(child);
        public void AddStyleClass(string name) { }
        public void Dispose() => Disposed = true;
    }
}
namespace Robust.Client.UserInterface.Controls
{
    public class LineEdit : Control { }
    public class TextEdit : Control { }
    public class BoxContainer : Control
    {
        public string Orientation { get; set; } = "";
        public int SeparationOverride { get; set; }
    }
    public class Label : Control
    {
        public string Text { get; set; } = "";
        public bool ClipText { get; set; }
    }
    public class Button : Control
    {
        public string Text { get; set; } = "";
        public bool ToggleMode { get; set; }
        public bool Pressed { get; set; }
        public event Action<object>? OnPressed, OnToggled;
        public void Click()
        {
            if (ToggleMode) { Pressed = !Pressed; OnToggled?.Invoke(this); }
            else OnPressed?.Invoke(this);
        }
    }
}
namespace Robust.Client.UserInterface.CustomControls
{
    public class DefaultWindow : Control
    {
        public static DefaultWindow? Last;
        public string Title { get; set; } = "";
        public Vector2 SetSize { get; set; }
        public Control Contents { get; } = new();
        public bool IsOpen { get; private set; }
        public DefaultWindow() => Last = this;
        public void OpenCentered() => IsOpen = true;
        public void Close() => IsOpen = false;
    }
}

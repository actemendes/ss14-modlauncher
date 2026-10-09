// Test-only contract fixture. This assembly is never included in launcher payloads.
using System.Runtime.CompilerServices;

namespace Robust.Client.UserInterface.Controls
{
    public class Control : IDisposable
    {
        public List<Control> Children { get; } = [];
        public Control? Parent { get; private set; }
        public bool HorizontalExpand { get; set; }
        public bool Visible { get; set; } = true;
        public float SetHeight { get; set; }
        public string ToolTip { get; set; } = "";
        public void AddChild(Control child) { child.Parent = this; Children.Add(child); }
        public void SetPositionInParent(int index) { var parent = Parent!; parent.Children.Remove(this); parent.Children.Insert(index, this); }
        public void Orphan() { Parent?.Children.Remove(this); Parent = null; }
        public void DisposeAllChildren() { foreach (var child in Children.ToArray()) child.Dispose(); }
        public void Dispose() { DisposeAllChildren(); Orphan(); }
        public void AddStyleClass(string _) { }
    }
    public class BoxContainer : Control
    {
        public enum LayoutOrientation { Vertical, Horizontal }
        public LayoutOrientation Orientation { get; set; }
        public int SeparationOverride { get; set; }
    }
    public class Label : Control { public string Text { get; set; } = ""; public bool ClipText { get; set; } }
    public class Button : Label
    {
        public bool ToggleMode { get; set; }
        public bool Pressed { get; set; }
        public event Action<Button>? OnPressed;
        public event Action<Button>? OnToggled;
        public void Click() { if (ToggleMode) { Pressed = !Pressed; OnToggled?.Invoke(this); } else OnPressed?.Invoke(this); }
    }
    public class LineEdit : Control
    {
        public string Text { get; set; } = "";
        public string PlaceHolder { get; set; } = "";
        public event Action<LineEdit>? OnTextChanged;
        public void Edit(string text) { Text = text; OnTextChanged?.Invoke(this); }
    }
    public class ScrollContainer : Control { public bool HScrollEnabled { get; set; } }
    public class OptionButton : Control
    {
        public sealed record ItemSelectedEventArgs(int Id);
        public int SelectedId { get; private set; }
        public List<string> Items { get; } = [];
        public void AddItem(string label, int? id = null) => Items.Add(label);
        public void SelectId(int id) => SelectedId = id;
        public event Action<ItemSelectedEventArgs>? OnItemSelected;
        public void Choose(int id) => OnItemSelected?.Invoke(new(id));
    }
    public class OutputPanel : Control
    {
        public List<string> Lines { get; } = [];
        public void Clear() => Lines.Clear();
    }
}

namespace Content.Client.UserInterface.Systems.Chat.Widgets
{
    using Robust.Client.UserInterface.Controls;
    public sealed record ChatMessage(string Channel, string Message, string WrappedMessage);
    public readonly record struct GameTick(uint Value);
    public sealed class Timing
    {
        public (TimeSpan, GameTick) TimeBase => (TimeSpan.FromSeconds(10), new(100));
        public TimeSpan TickPeriod => TimeSpan.FromSeconds(1);
    }
    public sealed class Controller
    {
        public Timing _timing = new();
        public List<(GameTick, ChatMessage)> History = [];
    }
    public sealed class ChatBox : Control
    {
        private readonly Controller _controller = new();
        public OutputPanel Contents = new();
        public BoxContainer Layout = new();
        public bool RadioEnabled = true, ThrowNext;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public ChatBox() { Layout.AddChild(Contents); AddChild(Layout); }
        public void Receive(ChatMessage message, uint tick) { _controller.History.Add((new(tick), message)); OnMessageAdded(message); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void OnMessageAdded(ChatMessage msg)
        {
            if (msg.Channel == "Radio" && !RadioEnabled) return;
            if (ThrowNext) { ThrowNext = false; throw new InvalidOperationException("Native failure"); }
            AddLine(msg.WrappedMessage, "white");
        }
        public void Repopulate() { Contents.Clear(); foreach (var (_, message) in _controller.History) OnMessageAdded(message); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void AddLine(string message, string color) => Contents.Lines.Add(message);
    }
}

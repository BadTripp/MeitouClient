using System.Numerics;
using Meitou.Data.Ui;

namespace Meitou.Ui;

public sealed record MyGuiEvent(string Screen, string Widget, string Action, string Text = "", float Value = 0);
public sealed record MyGuiScreenFrame(string Id, IReadOnlyList<UiControl> Controls);

/// <summary>Window lifetime, z-order, capture, focus and template events for the original MyGUI layouts.</summary>
public sealed class MyGuiScreenHost(MyGuiResources resources)
{
    sealed class Screen(MyGuiLayout layout, bool modal, bool centreRoot)
    {
        public MyGuiLayout Layout { get; } = layout;
        public bool Modal { get; } = modal;
        public bool CentreRoot { get; } = centreRoot;
        public Vector2 Offset;
        public bool Pinned;
        public Dictionary<string, string> Text { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Tabs { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Selected { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, float> Scroll { get; } = new(StringComparer.Ordinal);
    }
    readonly Dictionary<string, Screen> screens = new(StringComparer.Ordinal);
    readonly List<string> open = [];
    IReadOnlyList<MyGuiScreenFrame> frame = [];
    (string Screen, string Widget)? focus;
    UiTextBuffer? edit;
    (string Screen, Vector2 At, Vector2 Offset)? drag;
    /// <summary>The slider being dragged: its screen, widget, action, argument and track (a control with the property <c>Slider</c>).</summary>
    (string Screen, string Widget, string Action, string Argument, UiRect Track)? slide;
    public event Action<MyGuiEvent>? Activated;
    public bool HasWindows => open.Count > 0;
    public bool HasModal => open.Any(id => screens[id].Modal);
    public bool CapturesPointer => drag is not null || slide is not null;
    public bool HasKeyboardFocus => focus is not null;
    public IReadOnlyList<string> Registered => screens.Keys.ToArray();
    public IReadOnlyList<string> OpenScreens => open.ToArray();
    public void Register(string id, MyGuiLayout layout, bool modal = false, bool centreRoot = false) => screens.Add(id, new(MyGuiTemplates.Expand(layout, resources), modal, centreRoot));
    public MyGuiLayout Layout(string id) => screens[id].Layout;
    public bool IsOpen(string id) => open.Contains(id);
    public void Open(string id)
    {
        if (!screens.ContainsKey(id)) throw new KeyNotFoundException(id);
        open.Remove(id);
        open.Add(id);
        focus = null; edit = null;
    }
    public void Close(string id)
    {
        open.Remove(id);
        if (focus?.Screen == id) { focus = null; edit = null; }
        if (drag?.Screen == id) drag = null;
        if (slide?.Screen == id) slide = null;
        frame = frame.Where(f => f.Id != id).ToArray();
    }
    public void CloseAll() { foreach (string id in open.ToArray()) Close(id); }
    public void SetTab(string id, string widget, int index) => screens[id].Tabs[widget] = Math.Max(0, index);
    /// <summary>Where a screen has been moved from its layout position (a dragged window keeps its offset; the host may place a window beside another).</summary>
    public Vector2 GetOffset(string id) => screens[id].Offset;
    public void SetOffset(string id, Vector2 offset) => screens[id].Offset = offset;
    public void SetText(string id, string widget, string text) { screens[id].Text[widget] = text; if (focus == (id, widget)) edit?.Set(text); }
    public string GetText(string id, string widget) => screens[id].Text.GetValueOrDefault(widget) ?? "";
    public int GetTab(string id, string widget) => screens[id].Tabs.GetValueOrDefault(widget);
    /// <summary>Ends typing in the focused edit box (a press the screen's decoration took).</summary>
    public void Blur() { focus = null; edit = null; }
    /// <summary>The edit box being typed in (also one a screen's decoration added): its screen, name, caret and selection; null: none.</summary>
    public (string Screen, string Widget, int Caret, int SelectionStart, int SelectionLength)? Focus =>
        focus is { } f && edit is not null ? (f.Screen, f.Widget, edit.Caret, edit.SelectionStart, edit.SelectionLength) : null;

    public IReadOnlyList<MyGuiScreenFrame> Arrange(int width, int height, Func<string, UiBindings> bind, Action<string, List<UiControl>>? decorate = null)
    {
        var result = new List<MyGuiScreenFrame>();
        foreach (string id in open.OrderBy(i => screens[i].Modal ? 1 : 0).ToArray())
        {
            var s = screens[id];
            var b = bind(id);
            foreach (var t in s.Tabs) b.SelectedTabs[t.Key] = t.Value;
            foreach (var t in s.Text) b.Text[t.Key] = t.Value;
            foreach (var t in s.Selected) b.SelectedItems[t.Key] = t.Value;
            foreach (var t in s.Scroll) b.ScrollOffsets[t.Key] = t.Value;
            if (focus is { } f && f.Screen == id && edit is not null)
            {
                foreach (var widget in s.Layout.Widgets.Where(w => w.Name == f.Widget || w.Properties.GetValueOrDefault("CaptionOwner") == f.Widget))
                {
                    var properties = new Dictionary<string, string>(b.Properties.GetValueOrDefault(widget.Name) ?? new Dictionary<string, string>(), StringComparer.Ordinal)
                    {
                        ["Focused"] = "true", ["CaretIndex"] = edit.Caret.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["SelectionStart"] = edit.SelectionStart.ToString(System.Globalization.CultureInfo.InvariantCulture), ["SelectionLength"] = edit.SelectionLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    };
                    if (widget.Properties.GetValueOrDefault("SkipCaption") != "true") b.Properties[widget.Name] = properties;
                }
            }
            foreach (var root in s.Layout.Roots.Where(r => r.Type == "Window" || s.CentreRoot))
            {
                var initial = b.Positions.GetValueOrDefault(root.Name, root.Position.RelativeTo(new(0, 0, width, height), root.RealPosition));
                float w = Math.Clamp(initial.Width, 0, Math.Max(0, width - 24)), h = Math.Clamp(initial.Height, 0, Math.Max(0, height - 24));
                b.Positions[root.Name] = new(Math.Clamp((width - w) / 2 + s.Offset.X, 0, Math.Max(0, width - w)),
                    Math.Clamp((height - h) / 2 + s.Offset.Y, 0, Math.Max(0, height - h)), w, h);
            }
            var controls = MyGuiPresentation.Arrange(s.Layout, width, height, b, resources.Layers).ToList();
            decorate?.Invoke(id, controls);
            result.Add(new(id, controls));
        }
        frame = result;
        return frame;
    }

    (string Id, UiControl Control)? Pick(Vector2 p)
    {
        foreach (var screen in frame.Reverse())
        {
            if (MyGuiPresentation.HitTest(screen.Controls, p.X, p.Y) is { } hit) return (screen.Id, hit);
            if (screens[screen.Id].Modal) return (screen.Id, new("modal", "Widget", default, "", "", "", false, new Dictionary<string, string>(), default));
        }
        return null;
    }
    public bool Contains(Vector2 p) => Pick(p) is not null;
    public (string Screen, UiControl Control)? HitTest(Vector2 p) => Pick(p);
    public bool PointerDown(Vector2 p)
    {
        if (Pick(p) is not { } picked) { focus = null; edit = null; return false; }
        var (id, c) = picked;
        var s = screens[id];
        open.Remove(id); open.Add(id);
        if (!c.Enabled) return true;
        if (c.Action.StartsWith("mygui:", StringComparison.Ordinal))
        {
            var parts = c.Action.Split(':');
            if (parts[1] == "close") Close(id);
            else if (parts[1] == "pin") s.Pinned = !s.Pinned;
            else if (parts[1] == "tab" && parts.Length == 4) s.Tabs[parts[2]] = int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
            else if (parts[1] == "list" && parts.Length == 4)
            {
                int selected = int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                s.Selected[parts[2]] = selected;
                Activated?.Invoke(new(id, parts[2], "select", c.Argument, selected));
            }
            return true;
        }
        if (c.Type == "EditBox" && c.Properties.GetValueOrDefault("Static") != "true" && c.Properties.GetValueOrDefault("ReadOnly") != "true")
        {
            focus = (id, c.Name);
            s.Text.TryAdd(c.Name, c.Caption);
            edit = new(s.Text[c.Name]);
            return true;
        }
        focus = null; edit = null;
        if (c.Properties.GetValueOrDefault("Slider") == "true")
        {
            slide = (id, c.Name, c.Action, c.Argument, c.Bounds);
            Slide(p);
            return true;
        }
        if (c.Type == "Button") { Activated?.Invoke(new(id, c.Name, c.Action)); return true; }
        var root = frame.First(f => f.Id == id).Controls.FirstOrDefault(n => n.Type == "Window");
        if (root is not null && p.Y < root.Bounds.Y + 38 && !s.Pinned) drag = (id, p, s.Offset);
        return true;
    }
    public bool PointerMove(Vector2 p)
    {
        if (slide is not null) { Slide(p); return true; }
        if (drag is not { } d) return false;
        screens[d.Screen].Offset = d.Offset + p - d.At;
        return true;
    }
    public void PointerUp() { drag = null; slide = null; }

    /// <summary>A slider drag at <paramref name="p"/>: the event carries the slider's argument and where along its track the pointer is (0 at the left, 1 at the right).</summary>
    void Slide(Vector2 p)
    {
        if (slide is not { } s) return;
        float t = s.Track.Width > 0 ? Math.Clamp((p.X - s.Track.X) / s.Track.Width, 0, 1) : 0;
        Activated?.Invoke(new(s.Screen, s.Widget, s.Action, s.Argument, t));
    }
    public bool Wheel(Vector2 p, float steps)
    {
        if (Pick(p) is not { } picked) return false;
        var row = frame.First(f => f.Id == picked.Id).Controls.LastOrDefault(c => c.Properties.ContainsKey("ListOwner") && (c.Clip?.Contains(p.X, p.Y) ?? false));
        if (row is not null)
        {
            string owner = row.Properties["ListOwner"];
            int count = int.Parse(row.Properties["ListCount"], System.Globalization.CultureInfo.InvariantCulture);
            float max = Math.Max(0, count * 22 - row.Clip!.Value.Height);
            screens[picked.Id].Scroll[owner] = Math.Clamp(screens[picked.Id].Scroll.GetValueOrDefault(owner) - steps * 44, 0, max);
        }
        return true;
    }
    public bool Escape()
    {
        if (open.Count == 0) return false;
        string id = frame.LastOrDefault()?.Id ?? open[^1];
        Close(id);
        return true;
    }
    public bool TextInput(char value)
    {
        if (focus is null || edit is null || char.IsControl(value)) return false;
        edit.Insert(value.ToString()); PublishEdit();
        return true;
    }
    void PublishEdit()
    {
        if (focus is not { } f || edit is null) return;
        screens[f.Screen].Text[f.Widget] = edit.Text;
        Activated?.Invoke(new(f.Screen, f.Widget, "text", edit.Text));
    }
    public bool EditKey(UiEditKey key, bool extend = false)
    {
        if (focus is null || edit is null) return false;
        edit.Key(key, extend); PublishEdit();
        return true;
    }
    public bool Backspace() => EditKey(UiEditKey.Backspace);
    public bool FocusNext(bool backwards = false)
    {
        if (frame.LastOrDefault() is not { } top) return false;
        var fields = top.Controls.Where(c => c.Enabled && c.Type == "EditBox" && c.Properties.GetValueOrDefault("Static") != "true" && c.Properties.GetValueOrDefault("ReadOnly") != "true").ToArray();
        if (fields.Length == 0) return false;
        int index = Array.FindIndex(fields, c => focus == (top.Id, c.Name));
        var next = fields[(index + (backwards ? fields.Length - 1 : 1)) % fields.Length];
        focus = (top.Id, next.Name); screens[top.Id].Text.TryAdd(next.Name, next.Caption); edit = new(screens[top.Id].Text[next.Name]);
        return true;
    }
    public bool Submit()
    {
        if (frame.LastOrDefault() is not { } top) return false;
        var button = top.Controls.FirstOrDefault(c => c.Type == "Button" && c.Enabled && c.Properties.GetValueOrDefault("DefaultAction") == "true");
        if (button is null) return false;
        Activated?.Invoke(new(top.Id, button.Name, button.Action)); return true;
    }
}

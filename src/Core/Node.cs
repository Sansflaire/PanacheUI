using System;
using System.Collections.Generic;

namespace PanacheUI.Core;

/// <summary>
/// A single element in a PanacheUI UI tree. Nodes form a hierarchy; the root
/// node is passed to the layout engine and renderer each frame.
/// </summary>
public class Node
{
    // ── Identity ────────────────────────────────────────────────────────────

    public string Id { get; set; } = string.Empty;

    /// <summary>Text content rendered inside this node.</summary>
    public string? NodeValue { get; set; }

    // ── Style ───────────────────────────────────────────────────────────────

    public Style Style { get; set; } = new();

    private NodeAnimState?  _anim;
    private HashSet<string>? _classList;
    private List<Node>?      _children;

    /// <summary>Per-node animation state (hover, press, ripple, entrance, shake, scroll, flash, etc.).</summary>
    /// <remarks>
    /// Allocated on first access. Most nodes in a real window are inert decoration and
    /// never hover, press, scroll or flash — and consumers rebuild their whole tree every
    /// frame, so eagerly allocating this for every node meant hundreds of throwaway
    /// objects per frame. Framework internals read <see cref="AnimOrNull"/> instead, which
    /// never materialises the state.
    /// </remarks>
    public NodeAnimState Anim => _anim ??= new NodeAnimState();

    /// <summary>The animation state if one was ever created, otherwise null. Never allocates.</summary>
    internal NodeAnimState? AnimOrNull => _anim;

    /// <summary>True when this node has animation state worth updating or hashing.</summary>
    internal bool HasAnim => _anim != null;

    /// <summary>CSS-like class names. Checked by stylesheet rules. Allocated on first access.</summary>
    public HashSet<string> ClassList => _classList ??= new HashSet<string>(StringComparer.Ordinal);

    // ── Hierarchy ───────────────────────────────────────────────────────────

    public Node? Parent { get; private set; }

    /// <summary>Child nodes in document order. Leaf nodes return a shared empty list.</summary>
    public IReadOnlyList<Node> Children => (IReadOnlyList<Node>?)_children ?? Array.Empty<Node>();

    public void AppendChild(Node child)
    {
        child.Parent?.RemoveChild(child);
        child.Parent = this;
        (_children ??= new List<Node>(4)).Add(child);
        MarkDirty();
    }

    public void PrependChild(Node child)
    {
        child.Parent?.RemoveChild(child);
        child.Parent = this;
        (_children ??= new List<Node>(4)).Insert(0, child);
        MarkDirty();
    }

    public void InsertBefore(Node child, Node reference)
    {
        int idx = _children?.IndexOf(reference) ?? -1;
        if (idx < 0) { AppendChild(child); return; }
        child.Parent?.RemoveChild(child);
        child.Parent = this;
        _children!.Insert(idx, child);
        MarkDirty();
    }

    public void RemoveChild(Node child)
    {
        if (_children != null && _children.Remove(child))
        {
            child.Parent = null;
            MarkDirty();
        }
    }

    public void Clear()
    {
        if (_children == null || _children.Count == 0) return;
        foreach (var c in _children) c.Parent = null;
        _children.Clear();
        MarkDirty();
    }

    // ── Query ───────────────────────────────────────────────────────────────

    public Node? FindById(string id)
    {
        if (Id == id) return this;
        if (_children == null) return null;
        foreach (var child in _children)
        {
            var found = child.FindById(id);
            if (found != null) return found;
        }
        return null;
    }

    public List<Node> FindByClass(string className)
    {
        var results = new List<Node>();
        CollectByClass(className, results);
        return results;
    }

    private void CollectByClass(string cls, List<Node> results)
    {
        if (_classList != null && _classList.Contains(cls)) results.Add(this);
        if (_children == null) return;
        foreach (var child in _children) child.CollectByClass(cls, results);
    }

    // ── Interaction ─────────────────────────────────────────────────────────

    public bool IsInteractive { get; set; }

    /// <summary>When true, this node can receive keyboard focus via click or programmatic focus.</summary>
    public bool IsFocusable { get; set; }

    /// <summary>
    /// When true, pressing the primary mouse button on this node captures the pointer:
    /// <see cref="OnDrag"/> keeps firing every frame while the button is held, even once the
    /// cursor leaves the node's box, until the button is released (<see cref="OnDragEnd"/>).
    /// While a node holds capture, other nodes stop receiving pressed state.
    /// Required for sliders, scrubbers, and anything drag-driven.
    /// </summary>
    public bool CapturesDrag { get; set; }

    public event Action<Node>? OnClick;

    /// <summary>
    /// Fired when the secondary (right) mouse button clicks this node. Args are the
    /// pointer position in <b>surface-local</b> pixels — the same coordinate space as the
    /// <c>mousePos</c> passed into <see cref="Rendering.PanacheSurface.Render"/> — not
    /// node-relative like <see cref="OnDrag"/>. That is the coordinate a context menu
    /// needs: a menu card is normally attached to the root as a
    /// <see cref="PositionMode.Absolute"/> node, which is positioned relative to the
    /// root's content origin, not the clicked node's.
    /// </summary>
    /// <remarks>
    /// See <see cref="Components.PUI.ContextMenu"/> for a ready-made floating menu built on
    /// this event. Right-click has no drag/capture concept — unlike the primary button,
    /// there is no <c>CapturesDrag</c> equivalent here; it is a discrete action only.
    /// </remarks>
    public event Action<Node, float, float>? OnRightClick;

    public event Action<Node>? OnMouseEnter;
    public event Action<Node>? OnMouseLeave;

    /// <summary>
    /// Fired every frame while this node holds pointer capture (see <see cref="CapturesDrag"/>),
    /// including the frame capture is acquired. Args are the pointer position in node-local
    /// pixels, relative to the node's box origin. Values may be negative or exceed the node's
    /// size when the cursor is dragged outside — clamp in the handler.
    /// </summary>
    public event Action<Node, float, float>? OnDrag;

    /// <summary>Fired once when pointer capture is released.</summary>
    public event Action<Node>? OnDragEnd;

    /// <summary>Fired when a scroll delta is applied over this node (OverflowY.Scroll nodes).</summary>
    public event Action<Node>? OnScroll;

    /// <summary>Fired when a key is pressed and this node has keyboard focus. Arg is the raw key code.</summary>
    public event Action<Node, int>? OnKeyDown;

    /// <summary>Fired when a character is typed and this node has keyboard focus.</summary>
    public event Action<Node, char>? OnKeyChar;

    internal void FireClick()              => OnClick?.Invoke(this);
    internal void FireRightClick(float surfaceX, float surfaceY) => OnRightClick?.Invoke(this, surfaceX, surfaceY);
    internal void FireMouseEnter()         => OnMouseEnter?.Invoke(this);
    internal void FireMouseLeave()         => OnMouseLeave?.Invoke(this);
    internal void FireDrag(float x, float y) => OnDrag?.Invoke(this, x, y);
    internal void FireDragEnd()            => OnDragEnd?.Invoke(this);
    internal void FireScroll()             => OnScroll?.Invoke(this);
    internal void FireKeyDown(int keyCode) => OnKeyDown?.Invoke(this, keyCode);
    internal void FireKeyChar(char c)      => OnKeyChar?.Invoke(this, c);

    // ── Layout result cache ─────────────────────────────────────────────────

    /// <summary>
    /// This node's box from the most recent layout pass, valid only when
    /// <see cref="LayoutStamp"/> equals that pass's stamp.
    /// </summary>
    /// <remarks>
    /// The layout engine's <c>Dictionary&lt;Node, LayoutBox&gt;</c> stays the public
    /// result type — plenty of consumer code hit-tests against it — but the framework's
    /// own per-frame walks would otherwise pay a hash lookup per node just to read back
    /// something layout already knew. The stamp makes the copy self-invalidating: a node
    /// carried over from a previous frame, or never placed at all, simply fails the
    /// comparison instead of returning a stale box.
    /// </remarks>
    internal Layout.LayoutBox CachedBox;

    /// <summary>Identifies which layout pass wrote <see cref="CachedBox"/>. 0 means never placed.</summary>
    internal ulong LayoutStamp;

    /// <summary>
    /// Whether this subtree contains <see cref="TextOverflow.Wrap"/> text, and is therefore
    /// measured against the width it is given rather than in isolation. Valid only when
    /// <see cref="HasWrapStamp"/> matches the current pass.
    /// </summary>
    /// <remarks>
    /// Stamped onto the node for the same reason <see cref="CachedBox"/> is: the measure
    /// pass asks this question several times per node per frame, and a side-table lookup
    /// would put a dictionary probe on the single hottest path in the framework purely to
    /// answer "no" for the ~99% of nodes that hold no wrapping text at all.
    /// </remarks>
    internal bool  CachedHasWrap;

    /// <summary>Identifies which layout pass wrote <see cref="CachedHasWrap"/>.</summary>
    internal ulong HasWrapStamp;

    // ── Dirty tracking ──────────────────────────────────────────────────────

    /// <summary>True when this node or any descendant has changed since the last render.</summary>
    public bool IsDirty { get; private set; } = true;

    public void MarkDirty()
    {
        // Already-dirty short-circuit. ClearDirty always clears the whole tree, so the
        // invariant "a dirty node has dirty ancestors" holds; once this node is dirty
        // there is nothing left to propagate. Without the guard, building a tree walks
        // the full ancestor chain on every AppendChild/WithStyle/WithText call.
        if (IsDirty) return;
        IsDirty = true;
        Parent?.MarkDirty();
    }

    internal void ClearDirty()
    {
        IsDirty = false;
        if (_children == null) return;
        foreach (var c in _children) c.ClearDirty();
    }

    // ── Convenience builder API ─────────────────────────────────────────────

    /// <summary>Fluent style setter — returns this node for chaining.</summary>
    public Node WithStyle(Action<Style> configure)
    {
        configure(Style);
        MarkDirty();
        return this;
    }

    public Node WithClass(params string[] classes)
    {
        foreach (var c in classes) ClassList.Add(c);
        return this;
    }

    public Node WithText(string text)
    {
        NodeValue = text;
        MarkDirty();
        return this;
    }

    public Node WithId(string id)
    {
        Id = id;
        return this;
    }

    public Node WithChildren(params Node[] children)
    {
        foreach (var c in children) AppendChild(c);
        return this;
    }
}

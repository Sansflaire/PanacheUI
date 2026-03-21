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

    /// <summary>Per-node animation state (hover, press, ripple, entrance, shake, etc.).</summary>
    public NodeAnimState Anim { get; } = new();

    /// <summary>CSS-like class names. Checked by stylesheet rules.</summary>
    public HashSet<string> ClassList { get; } = new(StringComparer.Ordinal);

    // ── Hierarchy ───────────────────────────────────────────────────────────

    public Node? Parent { get; private set; }
    public IReadOnlyList<Node> Children => _children;
    private readonly List<Node> _children = new();

    public void AppendChild(Node child)
    {
        child.Parent?.RemoveChild(child);
        child.Parent = this;
        _children.Add(child);
        MarkDirty();
    }

    public void PrependChild(Node child)
    {
        child.Parent?.RemoveChild(child);
        child.Parent = this;
        _children.Insert(0, child);
        MarkDirty();
    }

    public void InsertBefore(Node child, Node reference)
    {
        int idx = _children.IndexOf(reference);
        if (idx < 0) { AppendChild(child); return; }
        child.Parent?.RemoveChild(child);
        child.Parent = this;
        _children.Insert(idx, child);
        MarkDirty();
    }

    public void RemoveChild(Node child)
    {
        if (_children.Remove(child))
        {
            child.Parent = null;
            MarkDirty();
        }
    }

    public void Clear()
    {
        foreach (var c in _children) c.Parent = null;
        _children.Clear();
        MarkDirty();
    }

    // ── Query ───────────────────────────────────────────────────────────────

    public Node? FindById(string id)
    {
        if (Id == id) return this;
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
        if (ClassList.Contains(cls)) results.Add(this);
        foreach (var child in _children) child.CollectByClass(cls, results);
    }

    // ── Interaction ─────────────────────────────────────────────────────────

    public bool IsInteractive { get; set; }

    public event Action<Node>? OnClick;
    public event Action<Node>? OnMouseEnter;
    public event Action<Node>? OnMouseLeave;

    internal void FireClick()      => OnClick?.Invoke(this);
    internal void FireMouseEnter() => OnMouseEnter?.Invoke(this);
    internal void FireMouseLeave() => OnMouseLeave?.Invoke(this);

    // ── Dirty tracking ──────────────────────────────────────────────────────

    /// <summary>True when this node or any descendant has changed since the last render.</summary>
    public bool IsDirty { get; private set; } = true;

    public void MarkDirty()
    {
        IsDirty = true;
        Parent?.MarkDirty();
    }

    internal void ClearDirty() => IsDirty = false;

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

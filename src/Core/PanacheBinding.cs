using System;

namespace PanacheUI.Core;

/// <summary>
/// Reactive value container. When Value changes, all subscribed nodes are marked dirty.
/// </summary>
public class PanacheBinding<T>
{
    private T _value;
    public event Action<T>? OnChanged;

    public PanacheBinding(T initial) => _value = initial;

    public T Value
    {
        get => _value;
        set
        {
            if (!Equals(_value, value))
            {
                _value = value;
                OnChanged?.Invoke(value);
            }
        }
    }

    public void Bind(Node node)
    {
        OnChanged += _ => node.MarkDirty();
    }
}

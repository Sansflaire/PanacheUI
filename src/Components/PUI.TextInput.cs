using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using PanacheUI.Core;

namespace PanacheUI.Components;

public static partial class PUI
{
    // ── Text input ───────────────────────────────────────────────────────────
    //
    // The last widget that forced plugins to break the "no raw ImGui widgets" rule.
    // Everything visible here is a Node: the box, the border, the text, the caret, the
    // placeholder. ImGui contributes exactly two things, neither of them drawn — the
    // character queue the platform layer fills on WM_CHAR, and the WantTextInput /
    // WantCaptureKeyboard flags that tell Dalamud to stop forwarding those keystrokes to
    // the game while a field is focused.

    /// <summary>
    /// Caret index per input Id. Lives here rather than on the Node for the usual reason:
    /// consumers rebuild the tree every frame, so anything stored on the node object is
    /// gone by the next one.
    /// </summary>
    private static readonly Dictionary<string, int> CaretState = new(StringComparer.Ordinal);

    /// <summary>Horizontal text scroll per input Id, so a caret past the right edge stays visible.</summary>
    private static readonly Dictionary<string, float> InputScroll = new(StringComparer.Ordinal);

    /// <summary>Forget an input's caret and scroll position (e.g. when its dialog closes).</summary>
    public static void ResetTextInput(string id)
    {
        CaretState.Remove(id);
        InputScroll.Remove(id);
    }

    /// <summary>True when this input currently holds keyboard focus.</summary>
    public static bool IsTextInputFocused(string id) => InteractionManager.IsFocused(id);

    /// <summary>Give this input keyboard focus on the next frame.</summary>
    public static void FocusTextInput(string id) => InteractionManager.SetFocus(id);

    /// <summary>
    /// Pump ImGui's keyboard state into the focused PanacheUI node. Call once per frame,
    /// from inside your window's <c>Begin</c>/<c>End</c> block, whenever any surface in that
    /// window may contain a <see cref="TextInput"/>.
    /// </summary>
    /// <remarks>
    /// <para>Does nothing at all when no Panache node holds focus, so it is safe to call
    /// unconditionally.</para>
    ///
    /// <para><b>It claims the keyboard.</b> While a field is focused this sets
    /// <c>io.WantTextInput</c> and <c>io.WantCaptureKeyboard</c>, which is how Dalamud is
    /// told to swallow the keystrokes instead of passing them to the game. Without it,
    /// typing into a Panache field would also fire hotbar actions and open chat.</para>
    ///
    /// <para>Key codes routed to <see cref="Node.OnKeyDown"/> are <c>(int)ImGuiKey</c>
    /// values — a handler written against a different numbering will not match.</para>
    /// </remarks>
    public static void PumpKeyboard()
    {
        if (InteractionManager.FocusedNode == null) return;

        var io = ImGui.GetIO();
        io.WantTextInput       = true;
        io.WantCaptureKeyboard = true;

        var queue = io.InputQueueCharacters;
        for (int i = 0; i < queue.Size; i++)
        {
            char c = (char)queue[i];
            // Control characters arrive as key presses below, not as text. Ctrl+V in
            // particular reaches WM_CHAR as 0x16, which must not be inserted literally.
            if (c >= ' ' && c != 0x7F) InteractionManager.RouteKeyChar(c);
        }

        for (int i = 0; i < EditingKeys.Length; i++)
            if (ImGui.IsKeyPressed(EditingKeys[i], true))
                InteractionManager.RouteKeyDown((int)EditingKeys[i]);
    }

    /// <summary>Keys a text field cares about beyond plain characters. Polled with repeat on.</summary>
    private static readonly ImGuiKey[] EditingKeys =
    {
        ImGuiKey.Backspace, ImGuiKey.Delete,
        ImGuiKey.LeftArrow, ImGuiKey.RightArrow,
        ImGuiKey.Home,      ImGuiKey.End,
        ImGuiKey.Enter,     ImGuiKey.Escape,
        ImGuiKey.Tab,       ImGuiKey.V,
    };

    /// <summary>Default height of a <see cref="TextInput"/> row in pixels.</summary>
    public const float TextInputHeight = 24f;

    /// <summary>
    /// A single-line text field, rendered entirely as PanacheUI nodes.
    /// </summary>
    /// <remarks>
    /// <para><b>The value is yours.</b> This is a controlled widget: it draws
    /// <paramref name="value"/> and reports edits through <paramref name="onChange"/>; it
    /// never stores the string itself. Only the caret and the horizontal scroll are kept
    /// here, keyed on <paramref name="id"/>.</para>
    ///
    /// <para><b>Requires <see cref="PumpKeyboard"/>.</b> Nothing routes keystrokes on its
    /// own — call it once per frame in the hosting window. Focus follows a click, and a
    /// click anywhere else blurs.</para>
    ///
    /// <para>The caret does not blink, deliberately: a blinking caret is a repaint twice a
    /// second forever, and this framework's whole redraw model exists to avoid exactly that
    /// for decoration nobody is looking at.</para>
    /// </remarks>
    /// <param name="id">Stable Id — keys focus, caret and scroll. Must be unique in the tree.</param>
    /// <param name="value">Current text.</param>
    /// <param name="accent">Accent for the focused border and the caret.</param>
    /// <param name="onChange">Fired with the new string on every edit.</param>
    /// <param name="onSubmit">Fired with the current string when Enter is pressed.</param>
    /// <param name="onCancel">Fired when Escape is pressed. Focus is dropped either way.</param>
    /// <param name="placeholder">Muted text shown when the value is empty.</param>
    /// <param name="width">Fixed width, or 0 to fill the parent.</param>
    /// <param name="maxLength">Reject input past this many characters. 0 = unlimited.</param>
    public static Node TextInput(
        string id, string value, PColor accent,
        Action<string>? onChange = null,
        Action<string>? onSubmit = null,
        Action?         onCancel = null,
        string placeholder = "",
        float  width       = 0f,
        float  height      = TextInputHeight,
        float  fontSize    = 11.5f,
        int    maxLength   = 0)
    {
        value ??= string.Empty;

        bool focused = InteractionManager.IsFocused(id);
        int  caret   = Math.Clamp(CaretState.TryGetValue(id, out var c) ? c : value.Length, 0, value.Length);
        CaretState[id] = caret;

        const float PadX = 7f;
        float innerW = Math.Max(0f, (width > 0f ? width : 0f) - PadX * 2f);

        // Keep the caret inside the visible strip. With width == 0 (Fill) the real width
        // isn't known until layout, so scrolling is only applied to explicitly-sized
        // fields — a Fill field is normally wide enough that it never comes up.
        float caretX = MeasureText(value[..caret], fontSize);
        float scroll = InputScroll.TryGetValue(id, out var sx) ? sx : 0f;
        if (innerW > 0f)
        {
            if (caretX - scroll > innerW) scroll = caretX - innerW;
            if (caretX - scroll < 0f)     scroll = caretX;
            float textW = MeasureText(value, fontSize);
            scroll = Math.Clamp(scroll, 0f, Math.Max(0f, textW - innerW));
        }
        else scroll = 0f;
        InputScroll[id] = scroll;

        bool showPlaceholder = value.Length == 0 && placeholder.Length > 0;

        var textNode = new Node()
            .WithText(showPlaceholder ? placeholder : value)
            .WithStyle(s =>
            {
                s.Position      = PositionMode.Absolute;
                s.Left          = -scroll;
                s.Top           = 0;
                s.WidthMode     = SizeMode.Fit;
                s.HeightMode    = SizeMode.Fill;
                s.FontSize      = fontSize;
                s.Color         = showPlaceholder ? Theme.TextSubtle : Theme.TextMuted;
                s.PointerEvents = PointerEvents.None;
            });

        var outer = new Node().WithId(id).WithStyle(s =>
        {
            if (width > 0f) { s.WidthMode = SizeMode.Fixed; s.Width = width; }
            else              s.WidthMode = SizeMode.Fill;
            s.HeightMode      = SizeMode.Fixed; s.Height = height;
            s.Flow            = Flow.Horizontal;
            s.Padding         = new EdgeSize(0, PadX);
            s.BackgroundColor = Theme.Panel2;
            s.BorderRadius    = 4f;
            s.BorderWidth     = 1f;
            s.BorderColor     = focused ? accent.WithOpacity(0.85f) : accent.WithOpacity(0.28f);
            // Renderer-painted hover — no handler, no tracking field. Suppressed while
            // focused so the hover cue can't wash out the stronger focus border.
            if (!focused) s.HoverBorderColor = accent.WithOpacity(0.55f);
            s.ClipContent     = true;
        });

        outer.IsInteractive = true;
        outer.IsFocusable   = true;
        outer.CapturesDrag  = true;   // gives us the press position, which OnClick does not
        outer.AppendChild(textNode);

        if (focused)
        {
            outer.AppendChild(new Node().WithStyle(s =>
            {
                s.Position        = PositionMode.Absolute;
                s.Left            = caretX - scroll;
                s.Top             = height * 0.18f;
                s.WidthMode       = SizeMode.Fixed; s.Width  = 1.5f;
                s.HeightMode      = SizeMode.Fixed; s.Height = height * 0.64f;
                s.BackgroundColor = accent.WithOpacity(0.95f);
                s.PointerEvents   = PointerEvents.None;
            }));
        }

        // Click / drag positions the caret at the nearest character boundary.
        outer.OnDrag += (_, localX, _) =>
            CaretState[id] = IndexAtX(value, fontSize, localX - PadX + scroll);

        outer.OnKeyChar += (_, ch) =>
        {
            if (maxLength > 0 && value.Length >= maxLength) return;
            int at = Math.Clamp(CaretState.TryGetValue(id, out var k) ? k : value.Length, 0, value.Length);
            CaretState[id] = at + 1;
            onChange?.Invoke(value.Insert(at, ch.ToString()));
        };

        outer.OnKeyDown += (_, keyCode) =>
        {
            int at = Math.Clamp(CaretState.TryGetValue(id, out var k) ? k : value.Length, 0, value.Length);

            switch ((ImGuiKey)keyCode)
            {
                case ImGuiKey.Backspace when at > 0:
                    CaretState[id] = at - 1;
                    onChange?.Invoke(value.Remove(at - 1, 1));
                    break;

                case ImGuiKey.Delete when at < value.Length:
                    onChange?.Invoke(value.Remove(at, 1));
                    break;

                case ImGuiKey.LeftArrow:  CaretState[id] = Math.Max(0, at - 1); break;
                case ImGuiKey.RightArrow: CaretState[id] = Math.Min(value.Length, at + 1); break;
                case ImGuiKey.Home:       CaretState[id] = 0; break;
                case ImGuiKey.End:        CaretState[id] = value.Length; break;

                case ImGuiKey.V when ImGui.IsKeyDown(ImGuiKey.ModCtrl):
                {
                    string clip = SafeClipboard();
                    if (clip.Length == 0) break;
                    if (maxLength > 0) clip = clip[..Math.Min(clip.Length, Math.Max(0, maxLength - value.Length))];
                    if (clip.Length == 0) break;
                    CaretState[id] = at + clip.Length;
                    onChange?.Invoke(value.Insert(at, clip));
                    break;
                }

                case ImGuiKey.Enter:
                    onSubmit?.Invoke(value);
                    InteractionManager.ClearFocus();
                    break;

                case ImGuiKey.Escape:
                    onCancel?.Invoke();
                    InteractionManager.ClearFocus();
                    break;

                case ImGuiKey.Tab:
                    InteractionManager.ClearFocus();
                    break;
            }
        };

        return outer;
    }

    /// <summary>
    /// A labelled text-input row — label on the left, field filling the rest. The
    /// <see cref="SliderRow"/> of text entry.
    /// </summary>
    public static Node TextInputRow(
        string id, string label, string value, PColor accent,
        Action<string>? onChange = null,
        Action<string>? onSubmit = null,
        string placeholder = "",
        float  labelWidth  = 108f,
        float  fontSize    = 11.5f,
        int    maxLength   = 0)
    {
        var labelNode = new Node().WithText(label).WithStyle(s =>
        {
            s.WidthMode     = SizeMode.Fixed; s.Width = labelWidth;
            s.HeightMode    = SizeMode.Fit;
            s.FontSize      = 10.5f;
            s.Color         = Theme.TextMuted;
            s.TextOverflow  = TextOverflow.Ellipsis;
            s.PointerEvents = PointerEvents.None;
        });

        return new Node().WithId(id + "_row").WithStyle(s =>
        {
            s.Flow       = Flow.Horizontal;
            s.WidthMode  = SizeMode.Fill;
            s.HeightMode = SizeMode.Fixed; s.Height = TextInputHeight;
            s.Gap        = 8;
            s.AlignItems = AlignItems.Center;
        }).WithChildren(
            labelNode,
            TextInput(id, value, accent, onChange, onSubmit,
                placeholder: placeholder, fontSize: fontSize, maxLength: maxLength));
    }

    /// <summary>Character index whose boundary is nearest <paramref name="x"/> pixels from the
    /// text origin — what a click at that position should put the caret on.</summary>
    private static int IndexAtX(string text, float fontSize, float x)
    {
        if (x <= 0f || text.Length == 0) return 0;

        float prev = 0f;
        for (int i = 1; i <= text.Length; i++)
        {
            float w = MeasureText(text[..i], fontSize);
            if (x < (prev + w) * 0.5f) return i - 1;
            prev = w;
        }
        return text.Length;
    }

    private static string SafeClipboard()
    {
        try { return ImGui.GetClipboardText() ?? string.Empty; }
        catch { return string.Empty; }   // no clipboard access is not a reason to crash a UI
    }
}

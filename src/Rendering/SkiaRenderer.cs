using System;
using System.Collections.Generic;
using System.Linq;
using PanacheUI.Core;
using PanacheUI.Layout;
using SkiaSharp;

namespace PanacheUI.Rendering;

/// <summary>
/// Walks a laid-out node tree and issues SkiaSharp draw calls onto an SKCanvas.
///
/// Drawing order per node:
///   1. Drop shadow
///   2. Background (solid, linear gradient, or radial gradient)
///   3. Image bitmap (if set)
///   4. Border
///   5. Clip (ClipContent or ClipPath or OverflowY.Scroll)
///   6. Interaction transforms (hover, press, shake, slide, flip, entrance)
///   7. Opacity layer
///   8. Effects (all entries in Style.Effects, drawn in order)
///   9. Text
///  10. Children (sorted by ZIndex)
///  11. Ripple overlay
///  12. One-shot flash effect (if active)
/// </summary>
public class SkiaRenderer : IDisposable
{
    private readonly SKPaint _fill   = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _text   = new() { IsAntialias = true, Style = SKPaintStyle.Fill, StrokeJoin = SKStrokeJoin.Round };

    // Reused for the opacity / entrance SaveLayer, which otherwise allocated and disposed
    // a native SKPaint for every translucent node, every frame.
    private readonly SKPaint _layer  = new();

    // Reused pixel buffer for the Voronoi effect (64×64×4 = 16 KB); avoids a per-frame allocation.
    private readonly byte[] _voronoiPixels = new byte[64 * 64 * 4];

    // Scratch buffer for ZIndex-ordered child drawing — see DrawNode. Reused across nodes
    // and frames; the recursion depth-slices it rather than allocating per node.
    private Node[] _zSort = new Node[16];

    /// <summary>
    /// Diagnostic switch: set false to paint every node regardless of whether it lies
    /// outside the current clip. Culling is an optimisation only — with it off the
    /// output must be pixel-identical, just slower. Flip this if a node ever goes
    /// missing and you need to know whether culling is to blame.
    /// </summary>
    internal static bool CullOffscreenNodes = true;

    /// <summary>Draw the entire node tree onto <paramref name="canvas"/>.</summary>
    /// <param name="time">Elapsed seconds — drives all animated effects.</param>
    public void Render(SKCanvas canvas, Node root, Dictionary<Node, LayoutBox> layout, float time = 0f)
    {
        canvas.Clear(SKColors.Transparent);
        DrawNode(canvas, root, layout, time);
    }

    private void DrawNode(SKCanvas canvas, Node node, Dictionary<Node, LayoutBox> layout, float time)
    {
        if (!layout.TryGetValue(node, out var box)) return;

        Style s    = node.Style;
        var rect   = box.ToSkRect();

        // ── Off-screen culling ────────────────────────────────────────────────
        // QuickReject tests the rect against the canvas's current matrix and clip, so
        // inside a scroll container (clipped viewport + scroll translate) this discards
        // every row scrolled out of view before a single paint call is issued. A node
        // that clips its own content is safe to discard wholesale — nothing it contains
        // can paint outside the rect we just rejected. A childless node is safe for the
        // same reason. Anything else may still have children escaping its box, so only
        // the subtree walk continues; see selfVisible below.
        bool clipsChildren = s.ClipContent || s.OverflowY == OverflowMode.Scroll
                                            || s.OverflowX == OverflowMode.Scroll || s.ClipPath != null;
        bool selfVisible   = !CullOffscreenNodes || !canvas.QuickReject(InfluenceRect(rect, s));
        if (!selfVisible && (clipsChildren || node.Children.Count == 0)) return;

        bool hasEffects = s.Effects.Count > 0;
        var  anim       = node.AnimOrNull;

        // ── Hover cross-fade ──────────────────────────────────────────────────
        // Resolved once here and used by the background, border and text steps below, so
        // a consumer gets the mandatory hover cue from Style alone — no per-row
        // OnMouseEnter handler, no _hoverId field, no frame of lag.
        float hoverT = s.HasHoverStyle ? anim?.HoverT ?? 0f : 0f;

        int saveCount = canvas.Save();
        try
        {

        // ── Interaction-driven transforms ─────────────────────────────────────

        if (hasEffects && HasEffect(s, NodeEffect.Shake))
        {
            float period = 2.5f / MathF.Max(0.1f, s.EffectSpeed);
            float phase  = (time / period) % 1f;
            if (phase < 0.28f)
            {
                float decay = 1f - phase / 0.28f;
                float amp   = s.EffectIntensity * 9f * decay;
                float ox    = MathF.Sin(time * 73.1f) * amp;
                float oy    = MathF.Sin(time * 61.7f + 1.3f) * amp * 0.55f;
                canvas.Translate(ox, oy);
            }
            else if (anim != null && (anim.ShakeOffsetX != 0f || anim.ShakeOffsetY != 0f))
            {
                canvas.Translate(anim.ShakeOffsetX, anim.ShakeOffsetY);
            }
        }

        if (hasEffects && anim is { HoverT: > 0f } && HasEffect(s, NodeEffect.HoverLift))
        {
            float scale = 1f + anim.HoverT * 0.04f;
            canvas.Translate(rect.MidX, rect.MidY);
            canvas.Scale(scale, scale);
            canvas.Translate(-rect.MidX, -rect.MidY);
        }

        if (hasEffects && anim is { PressT: > 0f } && HasEffect(s, NodeEffect.PressDepress))
        {
            float shrink = 1f - anim.PressT * 0.04f;
            canvas.Translate(rect.MidX, rect.MidY);
            canvas.Scale(shrink, shrink);
            canvas.Translate(-rect.MidX, -rect.MidY);
        }

        if (hasEffects && HasEffect(s, NodeEffect.SlideTransition))
        {
            float period  = 3.2f / MathF.Max(0.1f, s.EffectSpeed);
            float t       = (time / period) % 1f;
            float offset;
            if (t < 0.35f)
                offset = (1f - EaseOutCubic(t / 0.35f)) * -rect.Width;
            else if (t < 0.75f)
                offset = 0f;
            else
                offset = EaseOutCubic((t - 0.75f) / 0.25f) * rect.Width;
            canvas.Translate(offset, 0);
        }

        if (hasEffects && HasEffect(s, NodeEffect.CardFlip))
        {
            float period = 2.4f / MathF.Max(0.1f, s.EffectSpeed);
            float t      = (time / period) % 1f;
            float angle  = t * MathF.PI * 2f;
            float scaleX = MathF.Abs(MathF.Cos(angle));
            canvas.Scale(scaleX, 1f, rect.MidX, rect.MidY);
        }

        if (hasEffects && HasEffect(s, NodeEffect.StaggeredEntrance))
        {
            float et = anim?.EntranceT ?? 0f;
            float slideOffset = (1f - et) * 20f;
            canvas.Translate(0, slideOffset);
            if (et < 1f)
            {
                _layer.Color = new SKColor(255, 255, 255, (byte)(et * 255));
                canvas.SaveLayer(_layer);
            }
        }

        // Opacity layer
        if (s.Opacity < 1f)
        {
            _layer.Color = new SKColor(255, 255, 255, (byte)(s.Opacity * 255));
            canvas.SaveLayer(_layer);
        }

        // ── 1. Drop shadow ────────────────────────────────────────────────────
        if (selfVisible && s.ShadowColor.HasValue && s.ShadowBlur > 0)
        {
            // Use MaskFilter (alpha-only blur) instead of ImageFilter to avoid premultiplied-alpha
            // black fringing: ImageFilter.CreateBlur interpolates RGB channels with transparent
            // black (0,0,0,0) pixels, producing dark halos on colored shadows.
            using var shadowFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, s.ShadowBlur * 0.57f);
            _fill.Shader = null;  // ensure shadow uses solid color, not a stale gradient shader
            _fill.Color = s.ShadowColor.Value;
            _fill.MaskFilter = shadowFilter;
            var shadowRect = SKRect.Create(
                rect.Left  + s.ShadowOffsetX,
                rect.Top   + s.ShadowOffsetY,
                rect.Width, rect.Height);
            DrawShape(canvas, shadowRect, s, _fill);
            _fill.MaskFilter = null;
        }

        // ── 2. Background ─────────────────────────────────────────────────────
        PColor? bgColor = s.BackgroundColor;
        PColor? bgEnd   = s.BackgroundGradientEnd;
        if (hoverT > 0f && s.HoverBackgroundColor.HasValue)
        {
            var target = s.HoverBackgroundColor.Value;
            // A node with no base background fades up from that same color at zero alpha,
            // rather than from Transparent — fading through black is not what "no
            // background yet" should look like.
            bgColor = PColor.Lerp(bgColor ?? target.WithAlpha(0), target, hoverT);

            if (s.HoverBackgroundGradientEnd.HasValue)
            {
                var endTarget = s.HoverBackgroundGradientEnd.Value;
                bgEnd = PColor.Lerp(bgEnd ?? endTarget.WithAlpha(0), endTarget, hoverT);
            }
            else if (bgEnd.HasValue)
            {
                // Base is a gradient but only one hover color was given: carry the far stop
                // along towards it too, so the gradient brightens as a whole instead of
                // lighting only its start and pinching at the other end.
                bgEnd = PColor.Lerp(bgEnd.Value, target, hoverT);
            }
        }

        if (selfVisible && bgColor.HasValue)
        {
            // Hold shader reference outside the if/else so it outlives the block.
            // Using `using var` inside a nested if block would dispose the shader
            // before DrawShape is called, leaving the paint holding a dead C# wrapper.
            SKShader? bgShader = null;
            if (bgEnd.HasValue)
            {
                if (s.BackgroundGradientRadial)
                {
                    float cx = rect.Left + rect.Width  * s.BackgroundGradientCenterX;
                    float cy = rect.Top  + rect.Height * s.BackgroundGradientCenterY;
                    float radius = MathF.Max(rect.Width, rect.Height) * 0.7071f; // half-diagonal
                    bgShader = SKShader.CreateRadialGradient(
                        new SKPoint(cx, cy), radius,
                        new[] { (SKColor)bgColor.Value, (SKColor)bgEnd.Value },
                        SKShaderTileMode.Clamp);
                }
                else
                {
                    var startPt = s.Flow == Flow.Horizontal
                        ? new SKPoint(rect.Left, rect.MidY) : new SKPoint(rect.MidX, rect.Top);
                    var endPt = s.Flow == Flow.Horizontal
                        ? new SKPoint(rect.Right, rect.MidY) : new SKPoint(rect.MidX, rect.Bottom);
                    bgShader = SKShader.CreateLinearGradient(
                        startPt, endPt,
                        new[] { (SKColor)bgColor.Value, (SKColor)bgEnd.Value },
                        SKShaderTileMode.Clamp);
                }
                _fill.Shader = bgShader;
            }
            else
            {
                _fill.Color  = bgColor.Value;
                _fill.Shader = null;
            }

            DrawShape(canvas, rect, s, _fill);
            _fill.Shader = null;
            bgShader?.Dispose();
        }

        // ── 3. Image bitmap ───────────────────────────────────────────────────
        if (selfVisible && s.ImageBitmap != null)
        {
            int imgSave = canvas.Save();
            ClipToShape(canvas, rect, s);
            using var imgPaint = new SKPaint { IsAntialias = true };
            if (s.ImageTint.HasValue)
                imgPaint.ColorFilter = SKColorFilter.CreateBlendMode((SKColor)s.ImageTint.Value, SKBlendMode.Modulate);
            canvas.DrawBitmap(s.ImageBitmap, rect, imgPaint);
            canvas.RestoreToCount(imgSave);
        }

        // ── 4. Border ─────────────────────────────────────────────────────────
        // HoverBorderColor still needs a non-zero BorderWidth to paint — the hover style
        // changes the border's color, it does not conjure one out of nothing.
        PColor? borderColor = s.BorderColor;
        if (hoverT > 0f && s.HoverBorderColor.HasValue)
        {
            var target = s.HoverBorderColor.Value;
            borderColor = PColor.Lerp(borderColor ?? target.WithAlpha(0), target, hoverT);
        }

        if (selfVisible && borderColor.HasValue && s.BorderWidth > 0)
        {
            _stroke.Color       = borderColor.Value;
            _stroke.StrokeWidth = s.BorderWidth;
            DrawShape(canvas, rect, s, _stroke);
        }

        // ── 5. Clip ───────────────────────────────────────────────────────────
        bool needsClip = clipsChildren;
        if (needsClip)
        {
            ClipToShape(canvas, rect, s);

            if (s.ClipPath != null)
            {
                // Apply arbitrary clip path offset to node position
                int pathSave = canvas.Save();
                canvas.Translate(rect.Left, rect.Top);
                canvas.ClipPath(s.ClipPath, antialias: true);
                canvas.RestoreToCount(pathSave);
            }
        }

        // ── Scroll translate ──────────────────────────────────────────────────
        if (anim != null)
        {
            float tx = s.OverflowX == OverflowMode.Scroll ? -anim.ScrollOffsetX : 0f;
            float ty = s.OverflowY == OverflowMode.Scroll ? -anim.ScrollOffsetY : 0f;
            if (tx != 0f || ty != 0f) canvas.Translate(tx, ty);
        }

        // ── 6. Effects ────────────────────────────────────────────────────────
        if (selfVisible && hasEffects)
            DrawEffects(canvas, s, node, box, rect, time);

        // ── 7. Text ───────────────────────────────────────────────────────────
        if (selfVisible && !string.IsNullOrEmpty(node.NodeValue))
        {
            PColor? textColor = s.Color;
            if (hoverT > 0f && s.HoverColor.HasValue)
            {
                var hoverTarget = s.HoverColor.Value;
                textColor = PColor.Lerp(textColor ?? hoverTarget.WithAlpha(0), hoverTarget, hoverT);
            }

            if (s.TextOverflow == TextOverflow.Wrap)
            {
                // Multi-line: the block is laid out by TextLayout, the same call the layout
                // engine already made this frame with the same arguments, so this is a cache
                // hit and the painted lines are exactly the ones the box was sized for.
                DrawWrappedText(canvas, node.NodeValue!, box, s, textColor);
            }
            else if (hasEffects && HasEffect(s, NodeEffect.TypewriterText))
            {
                string full = node.NodeValue!;
                float charsPerSec = s.EffectSpeed * 12f;
                float totalCycle  = full.Length / MathF.Max(0.01f, charsPerSec) + 1.5f;
                float cycleTime   = time % totalCycle;
                int   visible     = System.Math.Min(full.Length, (int)(cycleTime * charsPerSec));
                bool  cursor      = (time * 2f % 1f) < 0.5f && visible < full.Length;
                DrawText(canvas, full[..visible] + (cursor ? "|" : ""), box, s, textColor);
            }
            else if (hasEffects && HasEffect(s, NodeEffect.RollingCounter)
                     && float.TryParse(node.NodeValue, out float target))
            {
                float period   = 4f / MathF.Max(0.01f, s.EffectSpeed);
                float cycleT   = (time % period) / period;
                float t        = cycleT < 0.8f ? EaseOutCubic(cycleT / 0.8f) : 1f;
                float displayed = target * t;
                DrawText(canvas, ((int)displayed).ToString("N0"), box, s, textColor);
            }
            else if (hasEffects && HasEffect(s, NodeEffect.TextWave))
            {
                DrawTextWave(canvas, node.NodeValue!, box, s, time, textColor);
            }
            else
            {
                DrawText(canvas, node.NodeValue!, box, s, textColor);
            }
        }

        // ── 8. Children (ZIndex-sorted) ───────────────────────────────────────
        var children  = node.Children;
        int childCount = children.Count;
        bool needsSort = false;
        for (int i = 0; i < childCount; i++)
            if (children[i].Style.ZIndex != 0) { needsSort = true; break; }

        if (needsSort)
        {
            // Stable insertion sort into a scratch slice. The old LINQ
            // Select/OrderBy/ThenBy chain allocated an index tuple per child plus
            // iterators and a sort buffer on every frame a z-indexed node was drawn.
            int start = RentZSlice(childCount);
            for (int i = 0; i < childCount; i++)
            {
                var child = children[i];
                int z = child.Style.ZIndex;
                int j = start + i - 1;
                while (j >= start && _zSort[j].Style.ZIndex > z)
                {
                    _zSort[j + 1] = _zSort[j];
                    j--;
                }
                _zSort[j + 1] = child;
            }
            for (int i = 0; i < childCount; i++)
                DrawNode(canvas, _zSort[start + i], layout, time);
            ReturnZSlice(childCount);
        }
        else
        {
            for (int i = 0; i < childCount; i++)
                DrawNode(canvas, children[i], layout, time);
        }

        // ── 9. Ripple overlay ─────────────────────────────────────────────────
        if (selfVisible && hasEffects && anim is { RippleAlpha: > 0f } && HasEffect(s, NodeEffect.Ripple))
        {
            int ripSave = canvas.Save();
            ClipToShape(canvas, rect, s);
            using var ripPaint = new SKPaint
            {
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
                Color       = new SKColor(
                    s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B,
                    (byte)(anim.RippleAlpha * 200f)),
            };
            canvas.DrawCircle(
                rect.Left + anim.RippleX,
                rect.Top  + anim.RippleY,
                anim.RippleRadius, ripPaint);
            canvas.RestoreToCount(ripSave);
        }

        // ── 10. One-shot flash effect ──────────────────────────────────────────
        if (selfVisible && anim is { FlashT: > 0f } && anim.FlashEffect != NodeEffect.None)
        {
            float saved = s.EffectIntensity;
            s.EffectIntensity *= anim.FlashT;
            DrawSingleEffect(canvas, anim.FlashEffect, s, node, box, rect, time);
            s.EffectIntensity = saved;
        }

        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }

    // ── Culling / scratch helpers ─────────────────────────────────────────────

    /// <summary>
    /// The rect a node can actually paint into: its box, grown by whatever its style
    /// bleeds outside the box (shadow spread, stroke half-width, blur-based effects).
    /// Used for the QuickReject test so culling never clips something that would have
    /// been visible.
    /// </summary>
    private static SKRect InfluenceRect(SKRect rect, Style s)
    {
        float grow = 0f;

        if (s.ShadowColor.HasValue && s.ShadowBlur > 0f)
            grow = MathF.Max(grow, s.ShadowBlur * 2f
                                 + MathF.Max(MathF.Abs(s.ShadowOffsetX), MathF.Abs(s.ShadowOffsetY)));

        if (s.BorderColor.HasValue && s.BorderWidth > 0f)
            grow = MathF.Max(grow, s.BorderWidth);

        // Blur-based effects (Bloom, PulseGlow, ChaseLight, HoverLift) stroke outside
        // the box with an image filter; 32px covers their worst case at intensity 1.
        if (s.Effects.Count > 0)
            grow = MathF.Max(grow, 32f);

        if (s.TextShadowColor.HasValue)
            grow = MathF.Max(grow, s.TextShadowBlur * 2f
                                 + MathF.Max(MathF.Abs(s.TextShadowOffsetX), MathF.Abs(s.TextShadowOffsetY)));

        return grow > 0f ? SKRect.Inflate(rect, grow, grow) : rect;
    }

    private int _zSortUsed;

    private int RentZSlice(int count)
    {
        int start = _zSortUsed;
        if (start + count > _zSort.Length)
            Array.Resize(ref _zSort, Math.Max(_zSort.Length * 2, start + count));
        _zSortUsed = start + count;
        return start;
    }

    private void ReturnZSlice(int count) => _zSortUsed -= count;

    // ── Shape helpers ─────────────────────────────────────────────────────────

    private static void DrawShape(SKCanvas canvas, SKRect rect, Style s, SKPaint paint)
    {
        if (s.HasAnyRadius)
        {
            var (tl, tr, br, bl) = s.GetCornerRadii();
            var box = new LayoutBox(rect.Left, rect.Top, rect.Width, rect.Height);
            canvas.DrawRoundRect(box.ToSkRoundRect(tl, tr, br, bl), paint);
        }
        else
        {
            canvas.DrawRect(rect, paint);
        }
    }

    private static void ClipToShape(SKCanvas canvas, SKRect rect, Style s)
    {
        if (s.HasAnyRadius)
        {
            var (tl, tr, br, bl) = s.GetCornerRadii();
            var box = new LayoutBox(rect.Left, rect.Top, rect.Width, rect.Height);
            canvas.ClipRoundRect(box.ToSkRoundRect(tl, tr, br, bl), antialias: true);
        }
        else
        {
            canvas.ClipRect(rect);
        }
    }

    private static SKRoundRect GetRoundRect(SKRect rect, Style s)
    {
        var (tl, tr, br, bl) = s.GetCornerRadii();
        var box = new LayoutBox(rect.Left, rect.Top, rect.Width, rect.Height);
        return box.ToSkRoundRect(tl, tr, br, bl);
    }

    private static bool HasEffect(Style s, NodeEffect e)
    {
        var effects = s.Effects;
        for (int i = 0; i < effects.Count; i++)
            if (effects[i] == e) return true;
        return false;
    }

    // ── Effect dispatch ───────────────────────────────────────────────────────

    private void DrawEffects(SKCanvas canvas, Style s, Node node, LayoutBox box, SKRect rect, float time)
    {
        foreach (var effect in s.Effects)
            DrawSingleEffect(canvas, effect, s, node, box, rect, time);
    }

    private void DrawSingleEffect(SKCanvas canvas, NodeEffect effect, Style s, Node node, LayoutBox box, SKRect rect, float time)
    {
        switch (effect)
        {
            case NodeEffect.PerlinNoise:       DrawPerlinNoise(canvas, s, rect, time);              break;
            case NodeEffect.Plasma:            DrawPlasma(canvas, s, rect, time);                   break;
            case NodeEffect.Voronoi:           DrawVoronoi(canvas, s, rect, time);                  break;
            case NodeEffect.Scanline:          DrawScanline(canvas, s, rect);                       break;
            case NodeEffect.DotMatrix:         DrawDotMatrix(canvas, s, rect, time);                break;
            case NodeEffect.Waveform:          DrawWaveform(canvas, s, rect, time);                 break;
            case NodeEffect.Shimmer:           DrawShimmer(canvas, s, rect, time);                  break;
            case NodeEffect.PulseGlow:         DrawPulseGlow(canvas, s, rect, time);                break;
            case NodeEffect.ChaseLight:        DrawChaseLight(canvas, s, rect, time);               break;
            case NodeEffect.RimLight:          DrawRimLight(canvas, s, rect);                       break;
            case NodeEffect.AmbientOcclusion:  DrawAmbientOcclusion(canvas, s, rect);              break;
            case NodeEffect.Bloom:             DrawBloom(canvas, s, rect);                          break;
            case NodeEffect.HeatHaze:          DrawHeatHaze(canvas, s, rect, time);                 break;
            case NodeEffect.Particles:         DrawParticles(canvas, s, rect, time);                break;
            case NodeEffect.SpecularSweep:     DrawSpecularSweep(canvas, s, rect, time);            break;
            case NodeEffect.HoverLift:         DrawHoverLiftOverlay(canvas, s, node, rect);         break;
            case NodeEffect.PressDepress:      DrawPressDepressOverlay(canvas, s, node, rect);      break;
            case NodeEffect.CardFlip:          DrawCardFlipFace(canvas, s, rect, time);             break;
            // Transform-only or text-driven effects — handled in DrawNode directly:
            case NodeEffect.SlideTransition:
            case NodeEffect.TypewriterText:
            case NodeEffect.RollingCounter:
            case NodeEffect.TextWave:
            case NodeEffect.Shake:
            case NodeEffect.Ripple:
            case NodeEffect.StaggeredEntrance:
                break;
        }
    }

    // ── Effect 01: Perlin Noise ───────────────────────────────────────────────

    private void DrawPerlinNoise(SKCanvas canvas, Style s, SKRect rect, float time)
    {
        float freq = 0.012f / s.EffectScale;
        using var noiseShader = SKShader.CreatePerlinNoiseFractalNoise(freq, freq, 4, 42f);
        float dx = time * s.EffectSpeed * 48f;
        float dy = time * s.EffectSpeed * 19f;
        using var scrolled = noiseShader.WithLocalMatrix(SKMatrix.CreateTranslation(dx, dy));

        float cr = s.EffectColor1.R / 255f, cg = s.EffectColor1.G / 255f, cb = s.EffectColor1.B / 255f;
        float amp = s.EffectIntensity * 2.5f;
        var colorMatrix = new float[] { 0,0,0,0,cr, 0,0,0,0,cg, 0,0,0,0,cb, amp,0,0,0,-amp*0.25f };
        using var colorFilter = SKColorFilter.CreateColorMatrix(colorMatrix);
        using var paint = new SKPaint { IsAntialias = true, Shader = scrolled, ColorFilter = colorFilter, BlendMode = SKBlendMode.Screen };

        int save = canvas.Save();
        ClipToShape(canvas, rect, s);
        canvas.DrawRect(rect, paint);
        canvas.RestoreToCount(save);
    }

    // ── Effect 02: Plasma ─────────────────────────────────────────────────────

    private void DrawPlasma(SKCanvas canvas, Style s, SKRect rect, float time)
    {
        int save = canvas.Save();
        ClipToShape(canvas, rect, s);

        const int BlobCount = 6;
        float blobR   = MathF.Max(rect.Width, rect.Height) * (0.35f + 0.15f * s.EffectScale);
        float hueBase = s.EffectColor1.R / 255f * 360f;

        for (int i = 0; i < BlobCount; i++)
        {
            float phase = i * MathF.PI * 2f / BlobCount;
            float bx = rect.MidX + MathF.Sin(time * s.EffectSpeed * 0.65f + phase * 1.37f) * rect.Width  * 0.40f;
            float by = rect.MidY + MathF.Cos(time * s.EffectSpeed * 0.50f + phase * 1.73f) * rect.Height * 0.40f;
            float hue = (hueBase + time * s.EffectSpeed * 22f + i * 60f) % 360f;
            var (br, bg, bb) = HsvToRgb(hue, 0.80f, 0.95f);
            byte a = (byte)(s.EffectIntensity * 210f);
            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(bx, by), blobR,
                new[] { new SKColor(br, bg, bb, a), new SKColor(br, bg, bb, 0) },
                SKShaderTileMode.Clamp);
            using var paint = new SKPaint { IsAntialias = true, Shader = shader, BlendMode = SKBlendMode.Screen };
            canvas.DrawRect(rect, paint);
        }
        canvas.RestoreToCount(save);
    }

    // ── Effect 03: Voronoi ────────────────────────────────────────────────────

    private void DrawVoronoi(SKCanvas canvas, Style s, SKRect rect, float time)
    {
        const int N = 14;
        var sites = new (float x, float y, float hue)[N];
        for (int i = 0; i < N; i++)
        {
            float fx = Hash(i * 7.3f + 1.1f), fy = Hash(i * 13.7f + 2.3f);
            float ox = MathF.Sin(time * s.EffectSpeed * 0.3f + i * 1.7f) * 0.08f;
            float oy = MathF.Cos(time * s.EffectSpeed * 0.2f + i * 2.3f) * 0.08f;
            sites[i] = (fx + ox, fy + oy, i * (360f / N));
        }

        const int BW = 64, BH = 64;
        var pixels  = _voronoiPixels;
        float hueBase = s.EffectColor1.R / 255f * 180f;

        for (int py = 0; py < BH; py++)
        for (int px = 0; px < BW; px++)
        {
            float nx = px / (float)BW, ny = py / (float)BH;
            int nearest = 0; float minDist = float.MaxValue;
            for (int i = 0; i < N; i++)
            {
                float dx = nx - sites[i].x, dy = ny - sites[i].y;
                float d = dx*dx + dy*dy;
                if (d < minDist) { minDist = d; nearest = i; }
            }
            float hue = (hueBase + sites[nearest].hue) % 360f;
            var (cr, cg, cb) = HsvToRgb(hue, 0.60f, 0.70f);
            int idx = (py * BW + px) * 4;
            pixels[idx] = cr; pixels[idx+1] = cg; pixels[idx+2] = cb;
            pixels[idx+3] = (byte)(s.EffectIntensity * 200f);
        }

        // Pin pixels first; bitmap is created inside the try so both are cleaned up together.
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            using var bmp = new SKBitmap();
            // Unpremul: pixel data is straight-alpha HSV→RGB, not premultiplied.
            // Declaring Premul (the default) would cause Skia to composite incorrect colors.
            bmp.InstallPixels(new SKImageInfo(BW, BH, SKColorType.Rgba8888, SKAlphaType.Unpremul), handle.AddrOfPinnedObject());
            using var imgPaint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Screen };
            int save = canvas.Save();
            ClipToShape(canvas, rect, s);
            canvas.DrawBitmap(bmp, rect, imgPaint);
            canvas.RestoreToCount(save);
        }
        finally { handle.Free(); }
    }

    // ── Effect 04: Scanline ───────────────────────────────────────────────────

    private void DrawScanline(SKCanvas canvas, Style s, SKRect rect)
    {
        float spacing = 2f * s.EffectScale;
        byte  alpha   = (byte)(s.EffectIntensity * 80f);
        using var paint = new SKPaint
        {
            Color       = new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, alpha),
            StrokeWidth = 1f,
            IsAntialias = false,
        };
        int save = canvas.Save();
        ClipToShape(canvas, rect, s);
        for (float y = rect.Top; y < rect.Bottom; y += spacing)
            canvas.DrawLine(rect.Left, y, rect.Right, y, paint);
        canvas.RestoreToCount(save);
    }

    // ── Effect 05: Dot Matrix ─────────────────────────────────────────────────

    private void DrawDotMatrix(SKCanvas canvas, Style s, SKRect rect, float time)
    {
        float spacing = 8f * s.EffectScale;
        float maxRadius = spacing * 0.35f;
        float cx = rect.MidX, cy = rect.MidY;
        float maxDist = MathF.Sqrt(rect.Width * rect.Width + rect.Height * rect.Height) * 0.5f;
        float pulse = (MathF.Sin(time * s.EffectSpeed * 2f) + 1f) * 0.5f;

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        int save = canvas.Save();
        ClipToShape(canvas, rect, s);

        for (float y = rect.Top + spacing * 0.5f; y < rect.Bottom; y += spacing)
        for (float x = rect.Left + spacing * 0.5f; x < rect.Right;  x += spacing)
        {
            float dist = MathF.Sqrt((x-cx)*(x-cx) + (y-cy)*(y-cy));
            float t    = 1f - (dist / maxDist);
            float dotR = maxRadius * (t * 0.7f + pulse * 0.3f) * s.EffectIntensity * 2f;
            if (dotR < 0.3f) continue;
            byte a = (byte)(t * s.EffectIntensity * 200f);
            paint.Color = new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, a);
            canvas.DrawCircle(x, y, dotR, paint);
        }
        canvas.RestoreToCount(save);
    }

    // ── Effect 06: Waveform ───────────────────────────────────────────────────

    private void DrawWaveform(SKCanvas canvas, Style s, SKRect rect, float time)
    {
        const int Bars = 48;
        float barW = rect.Width / Bars;
        float maxH = rect.Height * 0.85f;

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        int save = canvas.Save();
        ClipToShape(canvas, rect, s);

        for (int i = 0; i < Bars; i++)
        {
            float fi    = i / (float)Bars;
            float noise = (MathF.Sin(fi * 12.1f + time * s.EffectSpeed * 3f) * 0.5f +
                           MathF.Sin(fi * 7.3f  + time * s.EffectSpeed * 1.7f) * 0.3f +
                           MathF.Sin(fi * 23.7f + time * s.EffectSpeed * 0.9f) * 0.2f + 1f) * 0.5f;
            float bh = noise * maxH * s.EffectIntensity;
            float bx = rect.Left + i * barW + 1f;
            float by = rect.Bottom - bh;

            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(bx, by), new SKPoint(bx, rect.Bottom),
                new[] { (SKColor)s.EffectColor1, s.EffectColor1.WithOpacity(0.15f).ToSkia() },
                SKShaderTileMode.Clamp);
            paint.Shader = shader;
            canvas.DrawRect(SKRect.Create(bx, by, barW - 1.5f, bh), paint);
            paint.Shader = null;
        }
        canvas.RestoreToCount(save);
    }

    // ── Effect 07: Shimmer ────────────────────────────────────────────────────

    private void DrawShimmer(SKCanvas canvas, Style s, SKRect rect, float time)
    {
        float period  = 3f / MathF.Max(0.01f, s.EffectSpeed);
        float cycleT  = (time % period) / period;
        float stripeW = rect.Width * 0.25f;
        float startX  = rect.Left - stripeW + (rect.Width + stripeW) * cycleT;
        float dy      = rect.Height * 0.5f;

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(startX - stripeW * 0.5f, rect.MidY - dy),
            new SKPoint(startX + stripeW * 0.5f, rect.MidY + dy),
            new[] {
                new SKColor(255,255,255,0),
                new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, (byte)(s.EffectIntensity*180)),
                new SKColor(255,255,255,0)
            },
            new[] { 0f, 0.5f, 1f },
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = shader, BlendMode = SKBlendMode.Screen };
        int save = canvas.Save();
        ClipToShape(canvas, rect, s);
        canvas.DrawRect(rect, paint);
        canvas.RestoreToCount(save);
    }

    // ── Effect 08: Pulse Glow ─────────────────────────────────────────────────

    private void DrawPulseGlow(SKCanvas canvas, Style s, SKRect rect, float time)
    {
        float pulse = (MathF.Sin(time * s.EffectSpeed * MathF.PI * 1.5f) + 1f) * 0.5f;
        float blur  = 4f + pulse * 12f * s.EffectIntensity;
        byte  alpha = (byte)(40f + pulse * 160f * s.EffectIntensity);

        using var filter = SKImageFilter.CreateBlur(blur, blur, SKShaderTileMode.Decal);
        using var paint  = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 2f + pulse * 3f,
            Color       = new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, alpha),
            ImageFilter = filter,
            BlendMode   = SKBlendMode.Screen,
        };
        DrawShape(canvas, rect, s, paint);
    }

    // ── Effect 09: Particles ──────────────────────────────────────────────────

    private void DrawParticles(SKCanvas canvas, Style s, SKRect rect, float time)
    {
        const int N = 45;
        int save = canvas.Save();
        ClipToShape(canvas, rect, s);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        for (int i = 0; i < N; i++)
        {
            float fi     = i;
            float spd    = (0.35f + Hash(fi * 7.1f) * 0.65f) * s.EffectSpeed;
            float phase  = Hash(fi * 3.7f);
            float cycleT = Fract(time * spd * 0.22f + phase);
            float px = rect.Left + Hash(fi * 13.3f) * rect.Width
                     + MathF.Sin(time * spd * 1.8f + fi * 2.9f) * 14f * s.EffectScale;
            float py = rect.Bottom - cycleT * (rect.Height + 12f);
            float life = 1f - cycleT;
            float sz   = (1.2f + Hash(fi * 5.3f) * 2.8f) * life * s.EffectScale;
            byte  a    = (byte)(life * life * s.EffectIntensity * 230f);
            paint.Color = new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, a);
            canvas.DrawCircle(px, py, sz, paint);
        }
        canvas.RestoreToCount(save);
    }

    // ── Effect 10: Chase Light ────────────────────────────────────────────────

    private void DrawChaseLight(SKCanvas canvas, Style s, SKRect rect, float time)
    {
        float perim  = 2f * (rect.Width + rect.Height);
        float segLen = perim * 0.14f;
        float pos    = (time * s.EffectSpeed * perim * 0.4f) % perim;
        float phase  = -pos;

        float[] intervals = { segLen, perim - segLen };
        using var dash = SKPathEffect.CreateDash(intervals, phase);
        using var blur = SKImageFilter.CreateBlur(3f, 3f, SKShaderTileMode.Decal);
        byte alpha = (byte)(s.EffectIntensity * 220f);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            Color       = new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, alpha),
            PathEffect  = dash,
            ImageFilter = blur,
            BlendMode   = SKBlendMode.Screen,
        };
        DrawShape(canvas, rect, s, paint);
    }

    // ── Effect 11: Hover Lift Overlay ─────────────────────────────────────────

    private void DrawHoverLiftOverlay(SKCanvas canvas, Style s, Node node, SKRect rect)
    {
        if (node.Anim.HoverT <= 0f) return;
        float blur  = 6f + node.Anim.HoverT * 10f;
        byte  alpha = (byte)(node.Anim.HoverT * 60f);
        using var filter = SKImageFilter.CreateBlur(blur, blur, SKShaderTileMode.Decal);
        using var paint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f,
            Color       = new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, alpha),
            ImageFilter = filter, BlendMode = SKBlendMode.Screen,
        };
        DrawShape(canvas, rect, s, paint);
    }

    // ── Effect 12: Press Depress Overlay ──────────────────────────────────────

    private void DrawPressDepressOverlay(SKCanvas canvas, Style s, Node node, SKRect rect)
    {
        if (node.Anim.PressT <= 0f) return;
        byte alpha = (byte)(node.Anim.PressT * 40f);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0,0,0,alpha) };
        int save = canvas.Save();
        ClipToShape(canvas, rect, s);
        canvas.DrawRect(rect, paint);
        canvas.RestoreToCount(save);
    }

    // ── Effect 13: Specular Sweep ─────────────────────────────────────────────

    private void DrawSpecularSweep(SKCanvas canvas, Style s, SKRect rect, float time)
    {
        float t       = (MathF.Sin(time * s.EffectSpeed * 0.4f) + 1f) * 0.5f;
        float cx      = rect.Left + rect.Width * t;
        float cy      = rect.Top  + rect.Height * 0.3f;
        float ellipseW = rect.Width * 0.35f, ellipseH = rect.Height * 0.45f;

        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy), ellipseW,
            new[] {
                new SKColor(255,255,255, (byte)(s.EffectIntensity*100f)),
                new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, (byte)(s.EffectIntensity*40f)),
                new SKColor(255,255,255,0)
            },
            new[] { 0f, 0.4f, 1f }, SKShaderTileMode.Clamp);

        var scaleMatrix = SKMatrix.CreateScale(1f, ellipseH / MathF.Max(0.001f, ellipseW));
        using var scaledShader = shader.WithLocalMatrix(scaleMatrix);
        using var paint = new SKPaint { IsAntialias = true, Shader = scaledShader, BlendMode = SKBlendMode.Screen };
        int save = canvas.Save();
        ClipToShape(canvas, rect, s);
        canvas.DrawRect(rect, paint);
        canvas.RestoreToCount(save);
    }

    // ── Effect 14: Rim Light ──────────────────────────────────────────────────

    private void DrawRimLight(SKCanvas canvas, Style s, SKRect rect)
    {
        float rimW = rect.Width * 0.25f;
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.MidY), new SKPoint(rect.Left + rimW, rect.MidY),
            new[] {
                new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, (byte)(s.EffectIntensity*160f)),
                new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, 0)
            }, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = shader, BlendMode = SKBlendMode.Screen };
        int save = canvas.Save();
        ClipToShape(canvas, rect, s);
        canvas.DrawRect(rect, paint);
        canvas.RestoreToCount(save);
    }

    // ── Effect 15: Ambient Occlusion ──────────────────────────────────────────

    private void DrawAmbientOcclusion(SKCanvas canvas, Style s, SKRect rect)
    {
        float aoSize = 20f * s.EffectScale;
        byte  alpha  = (byte)(s.EffectIntensity * 120f);
        var corners  = new[] {
            (rect.Left, rect.Top), (rect.Right, rect.Top),
            (rect.Left, rect.Bottom), (rect.Right, rect.Bottom),
        };
        int save = canvas.Save();
        ClipToShape(canvas, rect, s);
        foreach (var (cx, cy) in corners)
        {
            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), aoSize * 2f,
                new[] { new SKColor(0,0,0,alpha), new SKColor(0,0,0,0) },
                SKShaderTileMode.Clamp);
            using var paint = new SKPaint { IsAntialias = true, Shader = shader, BlendMode = SKBlendMode.Multiply };
            canvas.DrawRect(rect, paint);
        }
        canvas.RestoreToCount(save);
    }

    // ── Effect 16: Bloom ──────────────────────────────────────────────────────

    private void DrawBloom(SKCanvas canvas, Style s, SKRect rect)
    {
        float blurAmount = 8f * s.EffectScale;
        using var blur  = SKImageFilter.CreateBlur(blurAmount, blurAmount, SKShaderTileMode.Decal);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color       = new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, (byte)(s.EffectIntensity*120f)),
            ImageFilter = blur,
            BlendMode   = SKBlendMode.Screen,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 3f,
        };
        DrawShape(canvas, rect, s, paint);
    }

    // ── Effect 17: Heat Haze ──────────────────────────────────────────────────

    private void DrawHeatHaze(SKCanvas canvas, Style s, SKRect rect, float time)
    {
        float freq = 0.025f / s.EffectScale;
        using var noiseShader = SKShader.CreatePerlinNoiseTurbulence(freq, freq * 0.5f, 2, 42f);
        float dx = time * s.EffectSpeed * 15f, dy = -time * s.EffectSpeed * 8f;
        using var scrolled = noiseShader.WithLocalMatrix(SKMatrix.CreateTranslation(dx, dy));
        var c = (SKColor)s.EffectColor1;
        float rN = c.Red/255f, gN = c.Green/255f, bN = c.Blue/255f, alphaF = s.EffectIntensity;
        var matrix = new float[] { 0,0,0,0,rN, 0,0,0,0,gN, 0,0,0,0,bN, alphaF,0,0,0,-0.15f };
        using var cf    = SKColorFilter.CreateColorMatrix(matrix);
        using var paint = new SKPaint { IsAntialias = true, Shader = scrolled, ColorFilter = cf, BlendMode = SKBlendMode.Screen };
        int save = canvas.Save();
        ClipToShape(canvas, rect, s);
        canvas.DrawRect(rect, paint);
        canvas.RestoreToCount(save);
    }

    // ── Effect 18: Card Flip face tint ────────────────────────────────────────

    private void DrawCardFlipFace(SKCanvas canvas, Style s, SKRect rect, float time)
    {
        float period = 2.4f / MathF.Max(0.1f, s.EffectSpeed);
        float t      = (time / period) % 1f;
        float angle  = t * MathF.PI * 2f;
        if (MathF.Cos(angle) >= 0f) return;  // only tint back face

        var c = (SKColor)s.EffectColor2;
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color       = new SKColor(c.Red, c.Green, c.Blue, (byte)(s.EffectIntensity * 180)),
            BlendMode   = SKBlendMode.SrcOver,
        };
        int save = canvas.Save();
        ClipToShape(canvas, rect, s);
        canvas.DrawRect(rect, paint);
        canvas.RestoreToCount(save);
    }

    // ── Text ──────────────────────────────────────────────────────────────────

    /// <param name="colorOverride">Resolved fill color — the hover cross-fade's result, or
    /// null to use <see cref="Style.Color"/> unchanged.</param>
    private void DrawText(SKCanvas canvas, string text, LayoutBox box, Style s, PColor? colorOverride = null)
    {
        var entry   = FontCache.Get(s);
        var font    = entry.Font;
        var metrics = entry.Metrics;

        float contentTop   = box.Y + s.Padding.Top;
        float contentH     = box.Height - s.Padding.Vertical;
        float textH        = entry.TextHeight;
        float baselineY    = contentTop + (contentH - textH) / 2f - metrics.Ascent;

        float textW        = font.MeasureText(text);
        float contentLeft  = box.X + s.Padding.Left;
        float contentWidth = box.Width - s.Padding.Horizontal;

        float textX = s.TextAlign switch
        {
            TextAlign.Center => contentLeft + (contentWidth - textW) / 2f,
            TextAlign.Right  => box.Right - s.Padding.Right - textW,
            _                => contentLeft,
        };

        if (s.TextOverflow == TextOverflow.Ellipsis && textW > contentWidth)
        {
            float ellipsisW = font.MeasureText("...");
            float budget    = contentWidth - ellipsisW;
            if (budget <= 0)
            {
                text  = "...";
                textW = font.MeasureText(text);
            }
            else
            {
                // Span-based probing: the binary search used to allocate a substring per
                // step, so an elided label cost ~log2(len) string allocations every frame.
                var span = text.AsSpan();
                int lo = 0, hi = span.Length;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (font.MeasureText(span[..mid]) <= budget) lo = mid;
                    else hi = mid - 1;
                }
                text  = string.Concat(span[..lo], "...");
                textW = font.MeasureText(text);
            }
            textX = s.TextAlign switch
            {
                TextAlign.Center => contentLeft + (contentWidth - textW) / 2f,
                TextAlign.Right  => box.Right - s.Padding.Right - textW,
                _                => contentLeft,
            };
        }

        // Text shadow — MaskFilter (alpha-only blur) prevents premultiplied-alpha dark fringing.
        if (s.TextShadowColor.HasValue)
        {
            using SKMaskFilter? textShadowFilter = s.TextShadowBlur > 0
                ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, s.TextShadowBlur * 0.57f)
                : null;
            _text.Style = SKPaintStyle.Fill;
            _text.Color = s.TextShadowColor.Value;
            _text.MaskFilter = textShadowFilter;
            canvas.DrawText(text, textX + s.TextShadowOffsetX, baselineY + s.TextShadowOffsetY,
                SKTextAlign.Left, font, _text);
            _text.MaskFilter = null;
        }

        // Text outline
        if (s.TextOutlineColor.HasValue && s.TextOutlineSize > 0)
        {
            _text.Style       = SKPaintStyle.StrokeAndFill;
            _text.StrokeWidth = s.TextOutlineSize * 2f;
            _text.Color       = s.TextOutlineColor.Value;
            canvas.DrawText(text, textX, baselineY, SKTextAlign.Left, font, _text);
        }

        // Text fill
        _text.Style       = SKPaintStyle.Fill;
        _text.StrokeWidth = 0;
        _text.Color       = Resolve(colorOverride ?? s.Color);
        canvas.DrawText(text, textX, baselineY, SKTextAlign.Left, font, _text);
    }

    /// <summary>
    /// Paints a <see cref="TextOverflow.Wrap"/> block: one baseline per line, the whole
    /// block vertically centred in the content box the way a single line already is.
    /// </summary>
    /// <remarks>
    /// The line break-up comes from <see cref="TextLayout.Wrap"/> with exactly the
    /// arguments <c>LayoutEngine</c> used when it sized this box, so this resolves to a
    /// cache hit and the painted lines can never disagree with the height that was
    /// reserved for them. Text shadow and outline apply per line, same as the single-line
    /// path.
    /// </remarks>
    private void DrawWrappedText(SKCanvas canvas, string text, LayoutBox box, Style s, PColor? colorOverride)
    {
        var entry   = FontCache.Get(s);
        var font    = entry.Font;
        var metrics = entry.Metrics;

        float contentLeft  = box.X + s.Padding.Left;
        float contentWidth = box.Width  - s.Padding.Horizontal;
        float contentTop   = box.Y + s.Padding.Top;
        float contentH     = box.Height - s.Padding.Vertical;

        var block = TextLayout.Wrap(text, s.FontSize, s.Bold, s.Italic, contentWidth, s.MaxLines);
        var lines = block.Lines;

        float step      = entry.TextHeight * s.LineHeight;
        float blockH    = lines.Length * step;
        float firstTop  = contentTop + (contentH - blockH) / 2f;

        using SKMaskFilter? shadowFilter = s.TextShadowColor.HasValue && s.TextShadowBlur > 0
            ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, s.TextShadowBlur * 0.57f)
            : null;

        var fillColor = Resolve(colorOverride ?? s.Color);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0) continue;

            float lineW    = font.MeasureText(line);
            float baseline = firstTop + i * step + (step - entry.TextHeight) / 2f - metrics.Ascent;

            float lineX = s.TextAlign switch
            {
                TextAlign.Center => contentLeft + (contentWidth - lineW) / 2f,
                TextAlign.Right  => box.Right - s.Padding.Right - lineW,
                _                => contentLeft,
            };

            if (s.TextShadowColor.HasValue)
            {
                _text.Style      = SKPaintStyle.Fill;
                _text.Color      = s.TextShadowColor.Value;
                _text.MaskFilter = shadowFilter;
                canvas.DrawText(line, lineX + s.TextShadowOffsetX, baseline + s.TextShadowOffsetY,
                    SKTextAlign.Left, font, _text);
                _text.MaskFilter = null;
            }

            if (s.TextOutlineColor.HasValue && s.TextOutlineSize > 0)
            {
                _text.Style       = SKPaintStyle.StrokeAndFill;
                _text.StrokeWidth = s.TextOutlineSize * 2f;
                _text.Color       = s.TextOutlineColor.Value;
                canvas.DrawText(line, lineX, baseline, SKTextAlign.Left, font, _text);
            }

            _text.Style       = SKPaintStyle.Fill;
            _text.StrokeWidth = 0;
            _text.Color       = fillColor;
            canvas.DrawText(line, lineX, baseline, SKTextAlign.Left, font, _text);
        }
    }

    private static SKColor Resolve(PColor? c) => c.HasValue ? (SKColor)c.Value : SKColors.White;

    private void DrawTextWave(SKCanvas canvas, string text, LayoutBox box, Style s, float time,
        PColor? colorOverride = null)
    {
        var entry   = FontCache.Get(s);
        var font    = entry.Font;
        var metrics = entry.Metrics;

        float contentTop   = box.Y + s.Padding.Top;
        float contentH     = box.Height - s.Padding.Vertical;
        float textH        = entry.TextHeight;
        float baselineY    = contentTop + (contentH - textH) / 2f - metrics.Ascent;
        float totalW       = font.MeasureText(text);
        float contentLeft  = box.X + s.Padding.Left;
        float contentWidth = box.Width - s.Padding.Horizontal;

        float startX = s.TextAlign switch
        {
            TextAlign.Center => contentLeft + (contentWidth - totalW) / 2f,
            TextAlign.Right  => box.Right - s.Padding.Right - totalW,
            _                => contentLeft,
        };

        float amplitude = s.FontSize * 0.25f * s.EffectIntensity * 3f;
        _text.Style = SKPaintStyle.Fill; _text.StrokeWidth = 0;
        _text.Color = Resolve(colorOverride ?? s.Color);

        float x = startX;
        for (int i = 0; i < text.Length; i++)
        {
            string ch   = text[i].ToString();
            float charW = font.MeasureText(ch);
            float offset = MathF.Sin(time * s.EffectSpeed * 3f + i * 0.6f) * amplitude;
            canvas.DrawText(ch, x, baselineY + offset, SKTextAlign.Left, font, _text);
            x += charW;
        }
    }

    // ── Shared math helpers ───────────────────────────────────────────────────

    private static (byte r, byte g, byte b) HsvToRgb(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
        float m = v - c;
        float r1, g1, b1;
        if      (h < 60f)  { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120f) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180f) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240f) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300f) { r1 = x; g1 = 0; b1 = c; }
        else               { r1 = c; g1 = 0; b1 = x; }
        return ((byte)((r1+m)*255), (byte)((g1+m)*255), (byte)((b1+m)*255));
    }

    private static float Hash(float n)
    {
        float v = MathF.Sin(n) * 43758.5453f;
        return v - MathF.Floor(v);
    }

    private static float Fract(float v) => v - MathF.Floor(v);

    private static float EaseOutCubic(float t) => 1f - (1f-t)*(1f-t)*(1f-t);

    public void Dispose()
    {
        _fill.Dispose();
        _stroke.Dispose();
        _text.Dispose();
        _layer.Dispose();
        // Typefaces and fonts are owned by FontCache and shared by every renderer
        // instance in the process — disposing them here would pull them out from
        // under any other live surface.
    }
}

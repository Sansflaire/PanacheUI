using System;
using System.Collections.Generic;
using PanacheUI.Core;
using PanacheUI.Layout;
using SkiaSharp;

namespace PanacheUI.Rendering;

/// <summary>
/// Walks a laid-out node tree and issues SkiaSharp draw calls onto an SKCanvas.
///
/// Drawing order per node:
///   1. Drop shadow (if configured)
///   2. Background (solid color or gradient)
///   3. Border (stroked rounded rect)
///   4. Text (NodeValue)
///   5. Children (recursive)
///   6. Clip restore (if ClipContent was set)
/// </summary>
public class SkiaRenderer
{
    private readonly SKPaint _fill   = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _text = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

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

        Style s = node.Style;
        var rect = box.ToSkRect();
        float r = s.BorderRadius;

        int saveCount = canvas.Save();

        // ── Interaction-driven transforms ────────────────────────────────────

        // Shake: time-driven auto-repeating jitter + honour external Anim trigger
        if (s.Effect == NodeEffect.Shake)
        {
            float period = 2.5f / MathF.Max(0.1f, s.EffectSpeed);
            float phase  = (time / period) % 1f;           // 0→1 per cycle
            if (phase < 0.28f)                             // shake for first 28% of period
            {
                float decay = 1f - phase / 0.28f;
                float amp   = s.EffectIntensity * 9f * decay;
                float ox    = MathF.Sin(time * 73.1f) * amp;
                float oy    = MathF.Sin(time * 61.7f + 1.3f) * amp * 0.55f;
                canvas.Translate(ox, oy);
            }
            else if (node.Anim.ShakeOffsetX != 0f || node.Anim.ShakeOffsetY != 0f)
            {
                // programmatic one-shot trigger (node.Anim.Shake() called externally)
                canvas.Translate(node.Anim.ShakeOffsetX, node.Anim.ShakeOffsetY);
            }
        }

        // HoverLift: scale toward center on hover
        if (s.Effect == NodeEffect.HoverLift && node.Anim.HoverT > 0f)
        {
            float scale = 1f + node.Anim.HoverT * 0.04f;
            canvas.Translate(rect.MidX, rect.MidY);
            canvas.Scale(scale, scale);
            canvas.Translate(-rect.MidX, -rect.MidY);
        }

        // PressDepress: squish on press
        if (s.Effect == NodeEffect.PressDepress && node.Anim.PressT > 0f)
        {
            float shrink = 1f - node.Anim.PressT * 0.04f;
            canvas.Translate(rect.MidX, rect.MidY);
            canvas.Scale(shrink, shrink);
            canvas.Translate(-rect.MidX, -rect.MidY);
        }

        // SlideTransition: translate X from off-screen left, looping
        if (s.Effect == NodeEffect.SlideTransition)
        {
            float period  = 3.2f / MathF.Max(0.1f, s.EffectSpeed);
            float t       = (time / period) % 1f;               // 0→1
            // 0→0.35: ease in from left; 0.35→0.75: hold; 0.75→1.0: ease out to right
            float offset;
            if (t < 0.35f)
                offset = (1f - EaseOutCubic(t / 0.35f)) * -rect.Width;
            else if (t < 0.75f)
                offset = 0f;
            else
                offset = EaseOutCubic((t - 0.75f) / 0.25f) * rect.Width;
            canvas.Translate(offset, 0);
        }

        // CardFlip: scale X via cos to simulate Y-axis flip, looping
        if (s.Effect == NodeEffect.CardFlip)
        {
            float period = 2.4f / MathF.Max(0.1f, s.EffectSpeed);
            float t      = (time / period) % 1f;               // 0→1 per cycle
            float angle  = t * MathF.PI * 2f;                 // full rotation
            float scaleX = MathF.Abs(MathF.Cos(angle));       // 1→0→1, compress to edge-on
            canvas.Scale(scaleX, 1f, rect.MidX, rect.MidY);
        }

        // StaggeredEntrance: translate Y up and fade in
        if (s.Effect == NodeEffect.StaggeredEntrance)
        {
            float et = node.Anim.EntranceT;
            float slideOffset = (1f - et) * 20f;
            canvas.Translate(0, slideOffset);
            if (et < 1f)
            {
                var opPaint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)(et * 255)) };
                canvas.SaveLayer(opPaint);
                opPaint.Dispose();
            }
        }

        // Opacity — push layer if < 1
        if (s.Opacity < 1f)
        {
            var paint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)(s.Opacity * 255)) };
            canvas.SaveLayer(paint);
            paint.Dispose();
        }

        // 1. Drop shadow
        if (s.ShadowColor.HasValue && s.ShadowBlur > 0)
        {
            _fill.Color = s.ShadowColor.Value;
            _fill.ImageFilter = SKImageFilter.CreateBlur(s.ShadowBlur, s.ShadowBlur);
            var shadowRect = SKRect.Create(
                rect.Left  + s.ShadowOffsetX,
                rect.Top   + s.ShadowOffsetY,
                rect.Width, rect.Height);
            if (r > 0)
                canvas.DrawRoundRect(shadowRect, r, r, _fill);
            else
                canvas.DrawRect(shadowRect, _fill);
            _fill.ImageFilter = null;
        }

        // 2. Background
        if (s.BackgroundColor.HasValue)
        {
            if (s.BackgroundGradientEnd.HasValue)
            {
                // Gradient in flow direction
                var startPt = s.Style_Flow_for_gradient_start(rect);
                var endPt   = s.Style_Flow_for_gradient_end(rect);
                _fill.Shader = SKShader.CreateLinearGradient(
                    startPt, endPt,
                    new[] { (SKColor)s.BackgroundColor.Value, (SKColor)s.BackgroundGradientEnd.Value },
                    SKShaderTileMode.Clamp);
            }
            else
            {
                _fill.Color  = s.BackgroundColor.Value;
                _fill.Shader = null;
            }

            if (r > 0)
                canvas.DrawRoundRect(rect, r, r, _fill);
            else
                canvas.DrawRect(rect, _fill);

            _fill.Shader = null;
        }

        // 3. Border
        if (s.BorderColor.HasValue && s.BorderWidth > 0)
        {
            _stroke.Color       = s.BorderColor.Value;
            _stroke.StrokeWidth = s.BorderWidth;

            if (r > 0)
                canvas.DrawRoundRect(rect, r, r, _stroke);
            else
                canvas.DrawRect(rect, _stroke);
        }

        // Clip children to this node's bounds
        if (s.ClipContent)
        {
            if (r > 0)
                canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
            else
                canvas.ClipRect(rect);
        }

        // 3b. Generative / animated effect overlay
        if (s.Effect != NodeEffect.None)
            DrawEffect(canvas, s, node, box, rect, r, time);

        // 4. Text — special handling for Typewriter, RollingCounter, TextWave
        if (!string.IsNullOrEmpty(node.NodeValue))
        {
            if (s.Effect == NodeEffect.TypewriterText)
            {
                string full = node.NodeValue!;
                float charsPerSec = s.EffectSpeed * 12f;
                float totalCycle = full.Length / MathF.Max(0.01f, charsPerSec) + 1.5f;
                float cycleTime = time % totalCycle;
                int visible = System.Math.Min(full.Length, (int)(cycleTime * charsPerSec));
                bool cursor = (time * 2f % 1f) < 0.5f && visible < full.Length;
                DrawText(canvas, full.Substring(0, visible) + (cursor ? "|" : ""), box, s);
            }
            else if (s.Effect == NodeEffect.RollingCounter
                     && float.TryParse(node.NodeValue, out float target))
            {
                float period = 4f / MathF.Max(0.01f, s.EffectSpeed);
                float cycleT = (time % period) / period;
                float t = cycleT < 0.8f ? EaseOutCubic(cycleT / 0.8f) : 1f;
                float displayed = target * t;
                DrawText(canvas, ((int)displayed).ToString("N0"), box, s);
            }
            else if (s.Effect == NodeEffect.TextWave)
            {
                DrawTextWave(canvas, node.NodeValue!, box, s, time);
            }
            else
            {
                DrawText(canvas, node.NodeValue!, box, s);
            }
        }

        // 5. Children
        foreach (var child in node.Children)
            DrawNode(canvas, child, layout, time);

        // 6. Ripple — drawn after children as an overlay
        if (s.Effect == NodeEffect.Ripple && node.Anim.RippleAlpha > 0f)
        {
            int ripSave = canvas.Save();
            if (r > 0) canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
            else canvas.ClipRect(rect);

            using var ripPaint = new SKPaint
            {
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
                Color       = new SKColor(
                    s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B,
                    (byte)(node.Anim.RippleAlpha * 200f)),
            };
            canvas.DrawCircle(
                rect.Left + node.Anim.RippleX,
                rect.Top  + node.Anim.RippleY,
                node.Anim.RippleRadius, ripPaint);
            canvas.RestoreToCount(ripSave);
        }

        canvas.RestoreToCount(saveCount);
    }

    // ── Effect Dispatch ──────────────────────────────────────────────────────

    private void DrawEffect(SKCanvas canvas, Style s, Node node, LayoutBox box, SKRect rect, float r, float time)
    {
        switch (s.Effect)
        {
            case NodeEffect.PerlinNoise:       DrawPerlinNoise(canvas, s, rect, r, time);      break;
            case NodeEffect.Plasma:            DrawPlasma(canvas, s, rect, r, time);           break;
            case NodeEffect.Voronoi:           DrawVoronoi(canvas, s, rect, r, time);          break;
            case NodeEffect.Scanline:          DrawScanline(canvas, s, rect, r);               break;
            case NodeEffect.DotMatrix:         DrawDotMatrix(canvas, s, rect, r, time);        break;
            case NodeEffect.Waveform:          DrawWaveform(canvas, s, rect, r, time);         break;
            case NodeEffect.Shimmer:           DrawShimmer(canvas, s, rect, r, time);          break;
            case NodeEffect.PulseGlow:         DrawPulseGlow(canvas, s, rect, r, time);        break;
            case NodeEffect.ChaseLight:        DrawChaseLight(canvas, s, rect, r, time);       break;
            case NodeEffect.RimLight:          DrawRimLight(canvas, s, rect, r);               break;
            case NodeEffect.AmbientOcclusion:  DrawAmbientOcclusion(canvas, s, rect, r);       break;
            case NodeEffect.Bloom:             DrawBloom(canvas, s, rect, r);                  break;
            case NodeEffect.HeatHaze:          DrawHeatHaze(canvas, s, rect, r, time);         break;
            case NodeEffect.Particles:         DrawParticles(canvas, s, rect, r, time);        break;
            case NodeEffect.SpecularSweep:     DrawSpecularSweep(canvas, s, rect, r, time);    break;
            case NodeEffect.HoverLift:         DrawHoverLiftOverlay(canvas, s, node, rect, r); break;
            case NodeEffect.PressDepress:      DrawPressDepressOverlay(canvas, s, node, rect, r); break;
            case NodeEffect.SlideTransition:   break;  // transform applied in DrawNode
            case NodeEffect.CardFlip:          DrawCardFlipFace(canvas, s, rect, r, time); break;
            // Effects handled in DrawNode or via Anim — no direct draw pass needed:
            case NodeEffect.TypewriterText:    break;
            case NodeEffect.RollingCounter:    break;
            case NodeEffect.TextWave:          break;
            case NodeEffect.Shake:             break;
            case NodeEffect.Ripple:            break;
            case NodeEffect.StaggeredEntrance: break;
        }
    }

    // ── Feature 01: Perlin Noise Background ─────────────────────────────────

    private void DrawPerlinNoise(SKCanvas canvas, Style s, SKRect rect, float r, float time)
    {
        float freq = 0.012f / s.EffectScale;

        using var noiseShader = SKShader.CreatePerlinNoiseFractalNoise(freq, freq, 4, 42f);

        float dx = time * s.EffectSpeed * 48f;
        float dy = time * s.EffectSpeed * 19f;
        using var scrolled = noiseShader.WithLocalMatrix(SKMatrix.CreateTranslation(dx, dy));

        float cr  = s.EffectColor1.R / 255f;
        float cg  = s.EffectColor1.G / 255f;
        float cb  = s.EffectColor1.B / 255f;
        float amp = s.EffectIntensity * 2.5f;

        var colorMatrix = new float[]
        {
            0,   0, 0, 0, cr,
            0,   0, 0, 0, cg,
            0,   0, 0, 0, cb,
            amp, 0, 0, 0, -amp * 0.25f,
        };

        using var colorFilter = SKColorFilter.CreateColorMatrix(colorMatrix);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Shader      = scrolled,
            ColorFilter = colorFilter,
            BlendMode   = SKBlendMode.Screen,
        };

        if (r > 0)
            canvas.DrawRoundRect(rect, r, r, paint);
        else
            canvas.DrawRect(rect, paint);
    }

    // ── Feature 02: Plasma / Lava Lamp ──────────────────────────────────────

    private void DrawPlasma(SKCanvas canvas, Style s, SKRect rect, float r, float time)
    {
        int save = canvas.Save();

        if (r > 0) canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
        else        canvas.ClipRect(rect);

        const int BlobCount = 6;
        float blobR = MathF.Max(rect.Width, rect.Height) * (0.35f + 0.15f * s.EffectScale);

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

    // ── Feature 03: Voronoi ──────────────────────────────────────────────────

    private void DrawVoronoi(SKCanvas canvas, Style s, SKRect rect, float r, float time)
    {
        const int N = 14;
        var sites = new (float x, float y, float hue)[N];
        for (int i = 0; i < N; i++)
        {
            float fx = Hash(i * 7.3f + 1.1f);
            float fy = Hash(i * 13.7f + 2.3f);
            float ox = MathF.Sin(time * s.EffectSpeed * 0.3f + i * 1.7f) * 0.08f;
            float oy = MathF.Cos(time * s.EffectSpeed * 0.2f + i * 2.3f) * 0.08f;
            sites[i] = (fx + ox, fy + oy, i * (360f / N));
        }

        const int BW = 64, BH = 64;
        var pixels = new byte[BW * BH * 4];
        float hueBase = s.EffectColor1.R / 255f * 180f;

        for (int py = 0; py < BH; py++)
        for (int px = 0; px < BW; px++)
        {
            float nx = px / (float)BW, ny = py / (float)BH;
            int nearest = 0;
            float minDist = float.MaxValue;
            for (int i = 0; i < N; i++)
            {
                float dx = nx - sites[i].x, dy = ny - sites[i].y;
                float d = dx*dx + dy*dy;
                if (d < minDist) { minDist = d; nearest = i; }
            }
            float hue = (hueBase + sites[nearest].hue) % 360f;
            var (cr, cg, cb) = HsvToRgb(hue, 0.60f, 0.70f);
            int idx = (py * BW + px) * 4;
            pixels[idx]   = cr;
            pixels[idx+1] = cg;
            pixels[idx+2] = cb;
            pixels[idx+3] = (byte)(s.EffectIntensity * 200f);
        }

        var bmp = new SKBitmap();
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bmp.InstallPixels(new SKImageInfo(BW, BH, SKColorType.Rgba8888), handle.AddrOfPinnedObject());
            using var imgPaint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Screen };
            int save = canvas.Save();
            if (r > 0) canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
            else canvas.ClipRect(rect);
            canvas.DrawBitmap(bmp, rect, imgPaint);
            canvas.RestoreToCount(save);
        }
        finally { handle.Free(); bmp.Dispose(); }
    }

    // ── Feature 04: Scanline ─────────────────────────────────────────────────

    private void DrawScanline(SKCanvas canvas, Style s, SKRect rect, float r)
    {
        float spacing = 2f * s.EffectScale;
        byte alpha = (byte)(s.EffectIntensity * 80f);
        using var paint = new SKPaint
        {
            Color       = new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, alpha),
            StrokeWidth = 1f,
            IsAntialias = false,
        };
        int save = canvas.Save();
        if (r > 0) canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
        else canvas.ClipRect(rect);

        for (float y = rect.Top; y < rect.Bottom; y += spacing)
            canvas.DrawLine(rect.Left, y, rect.Right, y, paint);

        canvas.RestoreToCount(save);
    }

    // ── Feature 05: Dot Matrix ───────────────────────────────────────────────

    private void DrawDotMatrix(SKCanvas canvas, Style s, SKRect rect, float r, float time)
    {
        float spacing = 8f * s.EffectScale;
        float maxRadius = spacing * 0.35f;
        float cx = rect.MidX, cy = rect.MidY;
        float maxDist = MathF.Sqrt(rect.Width * rect.Width + rect.Height * rect.Height) * 0.5f;

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        int save = canvas.Save();
        if (r > 0) canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
        else canvas.ClipRect(rect);

        float pulse = (MathF.Sin(time * s.EffectSpeed * 2f) + 1f) * 0.5f;

        for (float y = rect.Top + spacing * 0.5f; y < rect.Bottom; y += spacing)
        for (float x = rect.Left  + spacing * 0.5f; x < rect.Right; x += spacing)
        {
            float dist = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            float t = 1f - (dist / maxDist);
            float dotR = maxRadius * (t * 0.7f + pulse * 0.3f) * s.EffectIntensity * 2f;
            if (dotR < 0.3f) continue;

            byte alpha = (byte)(t * s.EffectIntensity * 200f);
            paint.Color = new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, alpha);
            canvas.DrawCircle(x, y, dotR, paint);
        }

        canvas.RestoreToCount(save);
    }

    // ── Feature 06: Waveform ─────────────────────────────────────────────────

    private void DrawWaveform(SKCanvas canvas, Style s, SKRect rect, float r, float time)
    {
        const int Bars = 48;
        float barW = rect.Width / Bars;
        float maxH = rect.Height * 0.85f;

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        int save = canvas.Save();
        if (r > 0) canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
        else canvas.ClipRect(rect);

        for (int i = 0; i < Bars; i++)
        {
            float fi = i / (float)Bars;
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

    // ── Feature 07: Shimmer ──────────────────────────────────────────────────

    private void DrawShimmer(SKCanvas canvas, Style s, SKRect rect, float r, float time)
    {
        float period  = 3f / MathF.Max(0.01f, s.EffectSpeed);
        float cycleT  = (time % period) / period;
        float stripeW = rect.Width * 0.25f;
        float startX  = rect.Left - stripeW + (rect.Width + stripeW) * cycleT;

        float dy = rect.Height * 0.5f;
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
        if (r > 0) canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
        else canvas.ClipRect(rect);
        canvas.DrawRect(rect, paint);
        canvas.RestoreToCount(save);
    }

    // ── Feature 08: Pulse Glow ───────────────────────────────────────────────

    private void DrawPulseGlow(SKCanvas canvas, Style s, SKRect rect, float r, float time)
    {
        float pulse = (MathF.Sin(time * s.EffectSpeed * MathF.PI * 1.5f) + 1f) * 0.5f;
        float blur  = 4f + pulse * 12f * s.EffectIntensity;
        byte  alpha = (byte)(40f + pulse * 160f * s.EffectIntensity);

        using var filter = SKImageFilter.CreateBlur(blur, blur);
        using var paint  = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 2f + pulse * 3f,
            Color       = new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, alpha),
            ImageFilter = filter,
            BlendMode   = SKBlendMode.Screen,
        };

        if (r > 0) canvas.DrawRoundRect(rect, r, r, paint);
        else       canvas.DrawRect(rect, paint);
    }

    // ── Feature 09: Particles ────────────────────────────────────────────────

    private void DrawParticles(SKCanvas canvas, Style s, SKRect rect, float r, float time)
    {
        const int N = 45;
        int save = canvas.Save();
        if (r > 0) canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
        else canvas.ClipRect(rect);

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        for (int i = 0; i < N; i++)
        {
            float fi      = i;
            float spd     = (0.35f + Hash(fi * 7.1f) * 0.65f) * s.EffectSpeed;
            float phase   = Hash(fi * 3.7f);
            float cycleT  = Fract(time * spd * 0.22f + phase);

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

    // ── Feature 12: Chase Light ──────────────────────────────────────────────

    private void DrawChaseLight(SKCanvas canvas, Style s, SKRect rect, float r, float time)
    {
        float perim = 2f * (rect.Width + rect.Height);
        float segLen = perim * 0.14f;
        float pos = (time * s.EffectSpeed * perim * 0.4f) % perim;
        float phase = -pos;

        float[] intervals = { segLen, perim - segLen };
        using var dash = SKPathEffect.CreateDash(intervals, phase);
        using var blur = SKImageFilter.CreateBlur(3f, 3f);

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

        if (r > 0) canvas.DrawRoundRect(rect, r, r, paint);
        else       canvas.DrawRect(rect, paint);
    }

    // ── Feature 13: Hover Lift Overlay ──────────────────────────────────────

    private void DrawHoverLiftOverlay(SKCanvas canvas, Style s, Node node, SKRect rect, float r)
    {
        if (node.Anim.HoverT <= 0f) return;

        float blur = 6f + node.Anim.HoverT * 10f;
        byte alpha = (byte)(node.Anim.HoverT * 60f);

        using var filter = SKImageFilter.CreateBlur(blur, blur);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            Color       = new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, alpha),
            ImageFilter = filter,
            BlendMode   = SKBlendMode.Screen,
        };

        if (r > 0) canvas.DrawRoundRect(rect, r, r, paint);
        else       canvas.DrawRect(rect, paint);
    }

    // ── Feature 14: Press Depress Overlay ───────────────────────────────────

    private void DrawPressDepressOverlay(SKCanvas canvas, Style s, Node node, SKRect rect, float r)
    {
        if (node.Anim.PressT <= 0f) return;

        byte alpha = (byte)(node.Anim.PressT * 40f);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Fill,
            Color       = new SKColor(0, 0, 0, alpha),
        };

        int save = canvas.Save();
        if (r > 0) canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
        else canvas.ClipRect(rect);
        canvas.DrawRect(rect, paint);
        canvas.RestoreToCount(save);
    }

    // ── Feature 22: Specular Sweep ───────────────────────────────────────────

    private void DrawSpecularSweep(SKCanvas canvas, Style s, SKRect rect, float r, float time)
    {
        float t = (MathF.Sin(time * s.EffectSpeed * 0.4f) + 1f) * 0.5f;
        float cx = rect.Left + rect.Width * t;
        float cy = rect.Top  + rect.Height * 0.3f;
        float ellipseW = rect.Width * 0.35f;
        float ellipseH = rect.Height * 0.45f;

        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy), ellipseW,
            new[] {
                new SKColor(255,255,255,(byte)(s.EffectIntensity*100f)),
                new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, (byte)(s.EffectIntensity*40f)),
                new SKColor(255,255,255,0)
            },
            new[] { 0f, 0.4f, 1f },
            SKShaderTileMode.Clamp);

        var scaleMatrix = SKMatrix.CreateScale(1f, ellipseH / MathF.Max(0.001f, ellipseW));
        using var scaledShader = shader.WithLocalMatrix(scaleMatrix);
        using var paint = new SKPaint { IsAntialias = true, Shader = scaledShader, BlendMode = SKBlendMode.Screen };

        int save = canvas.Save();
        if (r > 0) canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
        else canvas.ClipRect(rect);
        canvas.DrawRect(rect, paint);
        canvas.RestoreToCount(save);
    }

    // ── Feature 23: Rim Light ────────────────────────────────────────────────

    private void DrawRimLight(SKCanvas canvas, Style s, SKRect rect, float r)
    {
        float rimW = rect.Width * 0.25f;
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.MidY), new SKPoint(rect.Left + rimW, rect.MidY),
            new[] {
                new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, (byte)(s.EffectIntensity * 160f)),
                new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, 0)
            },
            SKShaderTileMode.Clamp);

        using var paint = new SKPaint { IsAntialias = true, Shader = shader, BlendMode = SKBlendMode.Screen };
        int save = canvas.Save();
        if (r > 0) canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
        else canvas.ClipRect(rect);
        canvas.DrawRect(rect, paint);
        canvas.RestoreToCount(save);
    }

    // ── Feature 24: Ambient Occlusion ────────────────────────────────────────

    private void DrawAmbientOcclusion(SKCanvas canvas, Style s, SKRect rect, float r)
    {
        float aoSize = 20f * s.EffectScale;
        byte alpha = (byte)(s.EffectIntensity * 120f);

        var corners = new[] {
            (rect.Left,  rect.Top),
            (rect.Right, rect.Top),
            (rect.Left,  rect.Bottom),
            (rect.Right, rect.Bottom),
        };

        int save = canvas.Save();
        if (r > 0) canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
        else canvas.ClipRect(rect);

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

    // ── Feature 25: Bloom ────────────────────────────────────────────────────

    private void DrawBloom(SKCanvas canvas, Style s, SKRect rect, float r)
    {
        float blurAmount = 8f * s.EffectScale;
        using var blur = SKImageFilter.CreateBlur(blurAmount, blurAmount);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color       = new SKColor(s.EffectColor1.R, s.EffectColor1.G, s.EffectColor1.B, (byte)(s.EffectIntensity*120f)),
            ImageFilter = blur,
            BlendMode   = SKBlendMode.Screen,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 3f,
        };

        if (r > 0) canvas.DrawRoundRect(rect, r, r, paint);
        else       canvas.DrawRect(rect, paint);
    }

    // ── Feature 28: Heat Haze ────────────────────────────────────────────────

    private void DrawHeatHaze(SKCanvas canvas, Style s, SKRect rect, float r, float time)
    {
        float freq = 0.025f / s.EffectScale;
        using var noiseShader = SKShader.CreatePerlinNoiseTurbulence(freq, freq * 0.5f, 2, 42f);

        // Scroll horizontally and upward (heat rises)
        float dx = time * s.EffectSpeed * 15f;
        float dy = -time * s.EffectSpeed * 8f;
        using var scrolled = noiseShader.WithLocalMatrix(SKMatrix.CreateTranslation(dx, dy));

        // Tint shimmer with EffectColor1 (noise.R drives alpha; color drives RGB)
        var c  = (SKColor)s.EffectColor1;
        float rN = c.Red   / 255f;
        float gN = c.Green / 255f;
        float bN = c.Blue  / 255f;
        float alpha = s.EffectIntensity;
        var matrix = new float[]
        {
            0, 0, 0, 0, rN,
            0, 0, 0, 0, gN,
            0, 0, 0, 0, bN,
            alpha, 0, 0, 0, -0.15f,   // noise.R drives alpha threshold
        };
        using var cf = SKColorFilter.CreateColorMatrix(matrix);
        using var paint = new SKPaint
        {
            IsAntialias = true, Shader = scrolled, ColorFilter = cf,
            BlendMode = SKBlendMode.Screen,
        };

        int save = canvas.Save();
        if (r > 0) canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
        else canvas.ClipRect(rect);
        canvas.DrawRect(rect, paint);
        canvas.RestoreToCount(save);
    }

    // ── Feature 30: Card Flip — back-face color overlay ─────────────────────

    private void DrawCardFlipFace(SKCanvas canvas, Style s, SKRect rect, float r, float time)
    {
        float period = 2.4f / MathF.Max(0.1f, s.EffectSpeed);
        float t      = (time / period) % 1f;
        float angle  = t * MathF.PI * 2f;
        bool backFace = MathF.Cos(angle) < 0f;
        if (!backFace) return;

        // Tint the back face with EffectColor2 so it reads as the "other side"
        var c = (SKColor)s.EffectColor2;
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color       = new SKColor(c.Red, c.Green, c.Blue, (byte)(s.EffectIntensity * 180)),
            BlendMode   = SKBlendMode.SrcOver,
        };
        int save = canvas.Save();
        if (r > 0) canvas.ClipRoundRect(new SKRoundRect(rect, r, r), antialias: true);
        else canvas.ClipRect(rect);
        canvas.DrawRect(rect, paint);
        canvas.RestoreToCount(save);
    }

    // ── Shared helpers ───────────────────────────────────────────────────────

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
        return ((byte)((r1 + m) * 255), (byte)((g1 + m) * 255), (byte)((b1 + m) * 255));
    }

    /// <summary>Deterministic pseudo-random float in [0,1) from a seed.</summary>
    private static float Hash(float n)
    {
        // Low-discrepancy hash — no System.Random allocation
        float v = MathF.Sin(n) * 43758.5453f;
        return v - MathF.Floor(v);
    }

    private static float Fract(float v) => v - MathF.Floor(v);

    private static float EaseOutCubic(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

    // ────────────────────────────────────────────────────────────────────────

    private void DrawText(SKCanvas canvas, string text, LayoutBox box, Style s)
    {
        var typeface = s.Bold
            ? SKTypeface.FromFamilyName(null, SKFontStyleWeight.Bold,   SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            : s.Italic
                ? SKTypeface.FromFamilyName(null, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic)
                : SKTypeface.Default;

        using var font = new SKFont(typeface, s.FontSize);
        font.GetFontMetrics(out var metrics);

        float contentTop   = box.Y + s.Padding.Top;
        float contentH     = box.Height - s.Padding.Vertical;
        float textH        = metrics.Descent - metrics.Ascent;
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
                int lo = 0, hi = text.Length;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (font.MeasureText(text.Substring(0, mid)) <= budget)
                        lo = mid;
                    else
                        hi = mid - 1;
                }
                text  = text[..lo] + "...";
                textW = font.MeasureText(text);
            }

            textX = s.TextAlign switch
            {
                TextAlign.Center => contentLeft + (contentWidth - textW) / 2f,
                TextAlign.Right  => box.Right - s.Padding.Right - textW,
                _                => contentLeft,
            };
        }

        if (s.TextOutlineColor.HasValue && s.TextOutlineSize > 0)
        {
            _text.Style       = SKPaintStyle.StrokeAndFill;
            _text.StrokeWidth = s.TextOutlineSize * 2f;
            _text.Color       = s.TextOutlineColor.Value;
            canvas.DrawText(text, textX, baselineY, SKTextAlign.Left, font, _text);
        }

        _text.Style       = SKPaintStyle.Fill;
        _text.StrokeWidth = 0;
        _text.Color       = s.Color.HasValue ? (SKColor)s.Color.Value : SKColors.White;
        canvas.DrawText(text, textX, baselineY, SKTextAlign.Left, font, _text);
    }

    // ── Feature 26: Text Wave ────────────────────────────────────────────────

    private void DrawTextWave(SKCanvas canvas, string text, LayoutBox box, Style s, float time)
    {
        var typeface = s.Bold
            ? SKTypeface.FromFamilyName(null, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            : s.Italic
                ? SKTypeface.FromFamilyName(null, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic)
                : SKTypeface.Default;

        using var font = new SKFont(typeface, s.FontSize);
        font.GetFontMetrics(out var metrics);

        float contentTop   = box.Y + s.Padding.Top;
        float contentH     = box.Height - s.Padding.Vertical;
        float textH        = metrics.Descent - metrics.Ascent;
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

        _text.Style       = SKPaintStyle.Fill;
        _text.StrokeWidth = 0;
        _text.Color       = s.Color.HasValue ? (SKColor)s.Color.Value : SKColors.White;

        float x = startX;
        for (int i = 0; i < text.Length; i++)
        {
            string ch = text[i].ToString();
            float charW = font.MeasureText(ch);
            float offset = MathF.Sin(time * s.EffectSpeed * 3f + i * 0.6f) * amplitude;
            canvas.DrawText(ch, x, baselineY + offset, SKTextAlign.Left, font, _text);
            x += charW;
        }
    }

    public void Dispose()
    {
        _fill.Dispose();
        _stroke.Dispose();
        _text.Dispose();
    }
}

// Extension: gradient direction helper (avoids cluttering Style)
internal static class StyleGradientExt
{
    public static SKPoint Style_Flow_for_gradient_start(this Style s, SKRect rect) =>
        s.Flow == Flow.Horizontal ? new SKPoint(rect.Left, rect.MidY) : new SKPoint(rect.MidX, rect.Top);

    public static SKPoint Style_Flow_for_gradient_end(this Style s, SKRect rect) =>
        s.Flow == Flow.Horizontal ? new SKPoint(rect.Right, rect.MidY) : new SKPoint(rect.MidX, rect.Bottom);
}

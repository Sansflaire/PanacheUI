using System.Collections.Generic;

namespace PanacheUI.Core;

public class NodeAnimState
{
    // ── Hover / Press ────────────────────────────────────────────────────────

    public float HoverT    { get; set; }   // 0→1 lerp on hover
    public float PressT    { get; set; }   // 0→1 on press, springs back
    public bool  IsHovered { get; set; }
    public bool  IsPressed { get; set; }

    // ── Ripple ───────────────────────────────────────────────────────────────

    public float RippleRadius { get; set; }
    public float RippleAlpha  { get; set; } = 0f;
    public float RippleX      { get; set; }
    public float RippleY      { get; set; }

    // ── Entrance animation (StaggeredEntrance) ───────────────────────────────

    public float EntranceT       { get; set; }   // 0→1
    public float EntranceDelay   { get; set; }   // seconds before starting
    public bool  EntranceStarted { get; set; }

    // ── Shake ────────────────────────────────────────────────────────────────

    public float ShakeTimer     { get; set; }
    public float ShakeDuration  { get; set; } = 0.5f;
    public float ShakeIntensity { get; set; } = 6f;
    public float ShakeOffsetX   { get; set; }
    public float ShakeOffsetY   { get; set; }

    // ── Scroll (OverflowY.Scroll nodes) ──────────────────────────────────────

    /// <summary>Current scroll position in pixels. Updated by InteractionManager on scroll-wheel.</summary>
    public float ScrollOffsetY  { get; set; }

    /// <summary>Total natural height of children. Set by LayoutEngine when OverflowY.Scroll.</summary>
    public float ScrollContentH { get; set; }

    // ── One-shot triggered effect ─────────────────────────────────────────────

    /// <summary>Which effect to flash. Set by TriggerEffect().</summary>
    public NodeEffect FlashEffect    { get; set; } = NodeEffect.None;

    /// <summary>Remaining time of the flash, counting down from FlashDuration to 0.</summary>
    public float FlashTimer          { get; set; }

    /// <summary>Total duration of the flash.</summary>
    public float FlashDuration       { get; set; } = 0.3f;

    /// <summary>0→1 intensity of the flash (1 at start, 0 at end). Multiply by EffectIntensity.</summary>
    public float FlashT              { get; set; }

    /// <summary>
    /// Trigger a one-shot effect that plays once and fades out over <paramref name="duration"/> seconds.
    /// The effect renders at EffectIntensity * FlashT strength on top of any permanent effects.
    /// </summary>
    public void TriggerEffect(NodeEffect effect, float duration = 0.3f)
    {
        FlashEffect   = effect;
        FlashTimer    = duration;
        FlashDuration = duration;
        FlashT        = 1f;
    }

    // ── Accordion ────────────────────────────────────────────────────────────

    public bool  IsExpanded { get; set; } = true;
    public float ExpandT    { get; set; } = 1f;   // 0=collapsed, 1=expanded

    // ── Slide Transition ─────────────────────────────────────────────────────

    public float SlideT       { get; set; }
    public bool  SlideForward { get; set; } = true;

    // ── Card Flip ────────────────────────────────────────────────────────────

    public float FlipT { get; set; }

    // ── Programmatic shake trigger ────────────────────────────────────────────

    /// <summary>Trigger a one-shot shake animation.</summary>
    public void Shake(float duration = 0.5f, float intensity = 6f)
    {
        ShakeTimer    = duration;
        ShakeDuration = duration;
        ShakeIntensity = intensity;
    }

    // ── Per-frame update ─────────────────────────────────────────────────────

    /// <summary>Update all time-based state. Call once per frame with delta time in seconds.</summary>
    public void Update(float dt)
    {
        // Hover lerp
        HoverT = IsHovered
            ? System.Math.Min(1f, HoverT + dt * 8f)
            : System.Math.Max(0f, HoverT - dt * 6f);

        // Press spring
        PressT = IsPressed
            ? System.Math.Min(1f, PressT + dt * 16f)
            : System.Math.Max(0f, PressT - dt * 10f);

        // Ripple expand
        if (RippleAlpha > 0f)
        {
            RippleRadius += dt * 180f;
            RippleAlpha  -= dt * 2.5f;
            if (RippleAlpha < 0f) RippleAlpha = 0f;
        }

        // Entrance
        if (!EntranceStarted)
        {
            EntranceDelay -= dt;
            if (EntranceDelay <= 0f) EntranceStarted = true;
        }
        else
        {
            EntranceT = System.Math.Min(1f, EntranceT + dt * 3f);
        }

        // Shake
        if (ShakeTimer > 0f)
        {
            ShakeTimer -= dt;
            float progress = ShakeTimer / ShakeDuration;
            float r = (float)(new System.Random((int)(ShakeTimer * 1000)).NextDouble() * 2 - 1);
            ShakeOffsetX = r * ShakeIntensity * progress;
            r = (float)(new System.Random((int)(ShakeTimer * 997 + 1)).NextDouble() * 2 - 1);
            ShakeOffsetY = r * ShakeIntensity * progress;
        }
        else { ShakeOffsetX = 0; ShakeOffsetY = 0; }

        // Flash / one-shot triggered effect
        if (FlashTimer > 0f)
        {
            FlashTimer -= dt;
            FlashT      = FlashDuration > 0f ? FlashTimer / FlashDuration : 0f;
            if (FlashTimer <= 0f)
            {
                FlashTimer   = 0f;
                FlashT       = 0f;
                FlashEffect  = NodeEffect.None;
            }
        }

        // Accordion
        ExpandT = IsExpanded
            ? System.Math.Min(1f, ExpandT + dt * 6f)
            : System.Math.Max(0f, ExpandT - dt * 6f);

        // Slide
        SlideT = System.Math.Min(1f, SlideT + dt * 4f);

        // Flip
        FlipT = System.Math.Min(1f, FlipT + dt * 3f);
    }
}

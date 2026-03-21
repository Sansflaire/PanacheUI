using System.Collections.Generic;

namespace PanacheUI.Core;

public class NodeAnimState
{
    // Hover/Press
    public float HoverT     { get; set; }  // 0→1 lerp on hover
    public float PressT     { get; set; }  // 0→1 on press, springs back
    public bool  IsHovered  { get; set; }
    public bool  IsPressed  { get; set; }

    // Ripple (Feature 15)
    public float RippleRadius { get; set; }
    public float RippleAlpha  { get; set; } = 0f;
    public float RippleX      { get; set; }
    public float RippleY      { get; set; }

    // Entrance animation (Feature 21 - Staggered)
    public float EntranceT     { get; set; }   // 0→1
    public float EntranceDelay { get; set; }   // seconds before starting
    public bool  EntranceStarted { get; set; }

    // Shake (Feature 27)
    public float ShakeTimer    { get; set; }
    public float ShakeDuration { get; set; } = 0.5f;
    public float ShakeIntensity { get; set; } = 6f;
    public float ShakeOffsetX  { get; set; }
    public float ShakeOffsetY  { get; set; }

    // Particles (deterministic — no state needed, uses time directly)

    // Accordion (Feature 18)
    public bool  IsExpanded  { get; set; } = true;
    public float ExpandT     { get; set; } = 1f; // 0=collapsed, 1=expanded

    // Slide Transition (Feature 29)
    public float SlideT { get; set; }  // 0→1
    public bool  SlideForward { get; set; } = true;

    // Card Flip (Feature 30)
    public float FlipT { get; set; }  // 0→1

    /// <summary>Trigger a shake animation.</summary>
    public void Shake(float duration = 0.5f, float intensity = 6f)
    {
        ShakeTimer    = duration;
        ShakeDuration = duration;
        ShakeIntensity = intensity;
    }

    /// <summary>Update time-based state. Call once per frame with delta time.</summary>
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

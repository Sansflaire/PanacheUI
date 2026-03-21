using System;
using SkiaSharp;

namespace PanacheUI.Core;

/// <summary>
/// RGBA color used throughout the PanacheUI style system.
/// Implicitly converts to/from SKColor.
/// </summary>
public readonly record struct PColor(byte R, byte G, byte B, byte A = 255)
{
    public SKColor ToSkia() => new(R, G, B, A);

    public static implicit operator SKColor(PColor c) => c.ToSkia();

    /// <summary>Parse a CSS hex color: #RGB, #RRGGBB, or #RRGGBBAA.</summary>
    public static PColor FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return hex.Length switch
        {
            3  => new PColor(
                    ParseNibble(hex[0]),
                    ParseNibble(hex[1]),
                    ParseNibble(hex[2])),
            6  => new PColor(
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16)),
            8  => new PColor(
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16),
                    Convert.ToByte(hex[6..8], 16)),
            _  => throw new ArgumentException($"Invalid hex color: #{hex}"),
        };
    }

    public PColor WithAlpha(byte a) => new(R, G, B, a);
    public PColor WithOpacity(float opacity) => WithAlpha((byte)(opacity * 255));

    private static byte ParseNibble(char c)
    {
        byte v = Convert.ToByte(c.ToString(), 16);
        return (byte)(v | (v << 4));
    }

    // Common colors
    public static readonly PColor Transparent = new(0, 0, 0, 0);
    public static readonly PColor White       = new(255, 255, 255);
    public static readonly PColor Black       = new(0,   0,   0);
    public static readonly PColor Red         = new(255, 0,   0);
    public static readonly PColor Green       = new(0,   200, 0);
    public static readonly PColor Blue        = new(0,   120, 255);
}

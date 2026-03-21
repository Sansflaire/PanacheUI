using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PanacheUI.Core;
using PanacheUI.Layout;
using PanacheUI.Rendering;
using SkiaSharp;

namespace PanacheUI;

/// <summary>
/// Local HTTP server on port 17778 for Claude to self-test rendered effects.
/// Renders any NodeEffect to a PNG strip (N frames across time) and returns the file path.
///
/// Endpoints:
///   GET /effects                                           → JSON array of all NodeEffect names
///   GET /render?effect=HeatHaze[&c1=FFB86B][&c2=6B8FFF]  → renders a frame strip, saves PNG
///            [&speed=1.0][&scale=1.0][&intensity=0.35]
///            [&frames=8][&duration=3.0][&w=240][&h=100]
///   GET /state                                            → health + port confirmation
/// </summary>
public sealed class RenderApi : IDisposable
{
    public const int Port = 17779;

    private readonly HttpListener         _listener = new();
    private readonly CancellationTokenSource _cts   = new();

    public RenderApi()
    {
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        Task.Run(ListenLoop, _cts.Token);
        Plugin.Log.Info($"[PanacheUI] RenderApi listening on http://localhost:{Port}/");
    }

    private async Task ListenLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => Handle(ctx));
            }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        try
        {
            var absPath = ctx.Request.Url?.AbsolutePath ?? "/";
            var q       = ctx.Request.QueryString;

            string body = absPath switch
            {
                "/effects" => HandleEffects(),
                "/render"  => HandleRender(q),
                "/state"   => $"{{\"ok\":true,\"port\":{Port},\"plugin\":\"PanacheUI\"}}",
                _          => "{\"error\":\"unknown endpoint\"}"
            };

            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.ContentType     = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[PanacheUI] RenderApi error");
        }
        finally
        {
            ctx.Response.Close();
        }
    }

    // ── /effects ────────────────────────────────────────────────────────────

    private static string HandleEffects()
    {
        var names = Enum.GetNames<NodeEffect>();
        return "[" + string.Join(",", Array.ConvertAll(names, n => $"\"{n}\"")) + "]";
    }

    // ── /render ─────────────────────────────────────────────────────────────

    private static string HandleRender(System.Collections.Specialized.NameValueCollection q)
    {
        if (!Enum.TryParse<NodeEffect>(q["effect"] ?? "PerlinNoise", true, out var effect))
            effect = NodeEffect.PerlinNoise;

        float scale     = TryFloat(q["scale"],     1.0f);
        float speed     = TryFloat(q["speed"],     1.0f);
        float intensity = TryFloat(q["intensity"], 0.35f);
        float duration  = TryFloat(q["duration"],  3.0f);
        int   frames    = Math.Clamp(TryInt(q["frames"], 8), 1, 16);
        int   frameW    = Math.Clamp(TryInt(q["w"],    240), 80, 480);
        int   frameH    = Math.Clamp(TryInt(q["h"],    100), 60, 320);
        string c1hex    = "#" + (q["c1"] ?? "42D4FF").TrimStart('#');
        string c2hex    = "#" + (q["c2"] ?? "8050FF").TrimStart('#');

        // Render a horizontal strip of N frames sampled across duration
        int totalW = frameW * frames;
        var info   = new SKImageInfo(totalW, frameH, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var stripSurface = SKSurface.Create(info);
        var stripCanvas = stripSurface.Canvas;
        stripCanvas.Clear(new SKColor(15, 15, 34));

        var layout   = new LayoutEngine();
        var renderer = new SkiaRenderer();
        using var surf       = new RenderSurface(frameW, frameH);
        using var labelFont  = new SKFont { Size = 9f };
        using var labelPaint = new SKPaint { Color = new SKColor(255, 255, 255, 180), IsAntialias = true };
        using var sepPaint   = new SKPaint { Color = new SKColor(60, 60, 90), StrokeWidth = 1 };

        for (int i = 0; i < frames; i++)
        {
            float t = frames == 1 ? 0f : duration * i / (frames - 1);

            var node = BuildPreviewNode(effect, c1hex, c2hex, scale, speed, intensity, frameW, frameH);
            var map  = layout.Compute(node, frameW, frameH);
            surf.Canvas.Clear(new SKColor(15, 15, 34));
            renderer.Render(surf.Canvas, node, map, t);

            // Blit frame into strip
            var frameBytes = surf.EncodePng();
            using var frameImg = SKImage.FromEncodedData(frameBytes);
            stripCanvas.DrawImage(frameImg, SKRect.Create(i * frameW, 0, frameW, frameH));

            // Time label
            stripCanvas.DrawText($"t={t:F1}s", i * frameW + 3, frameH - 5, SKTextAlign.Left, labelFont, labelPaint);

            // Frame separator
            if (i > 0)
                stripCanvas.DrawLine(i * frameW, 0, i * frameW, frameH, sepPaint);
        }

        layout.Dispose();
        renderer.Dispose();

        // Save strip PNG
        string outPath = Path.Combine(Path.GetTempPath(), $"panache_{effect}_{frames}f.png");
        using var stripImg  = stripSurface.Snapshot();
        using var stripData = stripImg.Encode(SKEncodedImageFormat.Png, 100);
        using var stream    = File.Create(outPath);
        stripData.SaveTo(stream);

        return $"{{" +
               $"\"ok\":true," +
               $"\"file\":\"{outPath.Replace("\\", "\\\\")}\"," +
               $"\"effect\":\"{effect}\"," +
               $"\"frames\":{frames}," +
               $"\"duration\":{duration}," +
               $"\"frameSize\":\"{frameW}x{frameH}\"" +
               $"}}";
    }

    private static Node BuildPreviewNode(
        NodeEffect effect, string c1hex, string c2hex,
        float scale, float speed, float intensity,
        int w, int h)
    {
        return new Node().WithId("api-preview").WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fixed; s.Width  = w;
            s.HeightMode      = SizeMode.Fixed; s.Height = h;
            s.Flow            = Flow.Vertical;
            s.BackgroundColor = PColor.FromHex("#0F0F22");
            s.BorderRadius    = 6;
            s.Effect          = effect;
            s.EffectColor1    = PColor.FromHex(c1hex);
            s.EffectColor2    = PColor.FromHex(c2hex);
            s.EffectScale     = scale;
            s.EffectSpeed     = speed;
            s.EffectIntensity = intensity;
            s.Padding         = new EdgeSize(6, 10);
        }).WithChildren(
            new Node().WithText(effect.ToString()).WithStyle(s =>
            {
                s.WidthMode  = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                s.FontSize   = 13f; s.Bold = true;
                s.Color      = PColor.FromHex(c1hex).WithOpacity(0.9f);
            })
        );
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static float TryFloat(string? s, float def)
        => float.TryParse(s, System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;

    private static int TryInt(string? s, int def)
        => int.TryParse(s, out var v) ? v : def;

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        _listener.Close();
        _cts.Dispose();
    }
}

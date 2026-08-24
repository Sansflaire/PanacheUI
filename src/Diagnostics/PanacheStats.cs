using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PanacheUI.Diagnostics;

/// <summary>
/// Live per-surface cost accounting for every PanacheUI surface in the process.
/// </summary>
/// <remarks>
/// <para>Answers the only question that matters about a UI framework running inside a
/// game: <i>how much of the frame am I eating?</i> Every
/// <see cref="Rendering.PanacheSurface"/> registers itself here and reports the time
/// spent in each pipeline phase, plus how often it actually had to repaint. Read the
/// merged, process-wide result over HTTP at <c>GET http://localhost:17779/stats</c>.</para>
///
/// <para><b>Why there is a cross-context registry.</b> Dalamud gives every plugin its own
/// <c>AssemblyLoadContext</c>, and each consumer copies <c>PanacheUI.dll</c> into its own
/// folder — so there is not one PanacheUI in the process, there is one <i>per plugin</i>,
/// each with its own statics. A plain static list would therefore only ever see the
/// surfaces of whichever plugin was asking, and the HTTP endpoint (which lives in the
/// PanacheUI plugin) would report nothing but its own demo windows.</para>
///
/// <para>The fix is a rendezvous through <see cref="AppDomain"/> data, which is genuinely
/// process-wide: there is exactly one AppDomain, and the dictionary/delegate types come
/// from <c>System.Private.CoreLib</c>, which every load context shares. So the type
/// identity matches across contexts even though <c>PanacheUI.SurfaceStats</c> would not.
/// Each copy publishes a <see cref="Func{T}"/> returning its own surfaces as JSON text;
/// the aggregator invokes them all. Nothing crosses the boundary but strings.</para>
///
/// <para>Overhead is five <see cref="Stopwatch.GetTimestamp"/> calls per surface per
/// frame (tens of nanoseconds each) and no allocation — cheap enough to leave on
/// permanently, which matters because a profiler you have to switch on is a profiler
/// that is off when the problem happens.</para>
/// </remarks>
public static class PanacheStats
{
    private static readonly object Gate = new();
    private static readonly List<SurfaceStats> Surfaces = new();

    /// <summary>Identifies this load context's copy of PanacheUI in the shared registry.</summary>
    private static readonly string ContextId = Guid.NewGuid().ToString("N")[..8];

    private static bool _published;

    /// <summary>Frames per second as ImGui last reported it. Fed from the draw thread.</summary>
    public static float FrameRate { get; private set; }

    /// <summary>Frame time in milliseconds as ImGui last reported it.</summary>
    public static float FrameMs { get; private set; }

    /// <summary>Record the host's frame timing. Call once per frame from the ImGui draw thread.</summary>
    /// <remarks>
    /// <para><b>FrameMs is derived from FrameRate, not from the raw delta.</b> ImGui's
    /// <c>Framerate</c> is a smoothed multi-frame average while <c>DeltaTime</c> is the
    /// single most recent frame, so the two routinely disagree by a millisecond or more.
    /// Mixing them made the "fps without Panache" arithmetic nonsense — subtracting a
    /// smoothed per-frame cost from an instantaneous frame time produced answers like
    /// "removing 1.2 ms would cost you 5 fps". Both figures now come from the same
    /// smoothed source, so they are consistent by construction.</para>
    /// </remarks>
    public static void ReportFrame(float frameRate, float deltaSeconds)
    {
        FrameRate = frameRate;
        FrameMs   = frameRate > 0.01f ? 1000f / frameRate : deltaSeconds * 1000f;
    }

    // ── Cross-load-context registry ───────────────────────────────────────────

    private const string RegistryKey = "PanacheUI.Diagnostics.StatsProviders.v1";

    /// <summary>
    /// Process-wide map of contextId → "give me your surfaces as a JSON array".
    /// Created by whichever copy of PanacheUI gets there first; shared by all of them.
    /// </summary>
    private static ConcurrentDictionary<string, Func<string>> Registry()
    {
        // AppDomain data has no atomic get-or-add, so the double-check is guarded on a
        // type the CLR shares across contexts rather than on our own static lock.
        if (AppDomain.CurrentDomain.GetData(RegistryKey) is ConcurrentDictionary<string, Func<string>> existing)
            return existing;

        lock (typeof(string))
        {
            if (AppDomain.CurrentDomain.GetData(RegistryKey) is ConcurrentDictionary<string, Func<string>> raced)
                return raced;

            var created = new ConcurrentDictionary<string, Func<string>>(StringComparer.Ordinal);
            AppDomain.CurrentDomain.SetData(RegistryKey, created);
            return created;
        }
    }

    private static void PublishSelf()
    {
        if (_published) return;
        _published = true;
        try { Registry()[ContextId] = LocalSurfaceRecords; }
        catch { /* diagnostics must never break a surface */ }
    }

    private static void UnpublishSelf()
    {
        if (!_published) return;
        _published = false;
        try { Registry().TryRemove(ContextId, out _); }
        catch { /* ignored */ }
    }

    // ── Registration ──────────────────────────────────────────────────────────

    internal static SurfaceStats Register(string owner, int width, int height)
    {
        var stats = new SurfaceStats(owner, width, height);
        lock (Gate) Surfaces.Add(stats);
        PublishSelf();
        return stats;
    }

    internal static void Unregister(SurfaceStats stats)
    {
        bool empty;
        lock (Gate)
        {
            Surfaces.Remove(stats);
            empty = Surfaces.Count == 0;
        }
        if (empty) UnpublishSelf();
    }

    /// <summary>Zero every counter in this load context — useful to re-baseline.</summary>
    public static void ResetAll()
    {
        lock (Gate)
            foreach (var s in Surfaces) s.Reset();
    }

    // ── Reporting ─────────────────────────────────────────────────────────────

    /// <summary>
    /// This load context's surfaces, one record per line, fields pipe-delimited.
    /// </summary>
    /// <remarks>
    /// A flat text record rather than JSON because this string is the wire format between
    /// load contexts, and both consumers of it — the HTTP endpoint and the in-game chat
    /// command — need the individual numbers back. Emitting JSON here would force one of
    /// them to re-parse JSON by hand; this way each output formats from real fields.
    /// Order: owner|label|w|h|frames|repaints|layout|interaction|fingerprint|render|upload|max
    /// </remarks>
    private static string LocalSurfaceRecords()
    {
        var sb = new StringBuilder(512);
        lock (Gate)
        {
            foreach (var s in Surfaces)
            {
                if (sb.Length > 0) sb.Append('\n');
                s.AppendRecord(sb);
            }
        }
        return sb.ToString();
    }

    /// <summary>One surface's numbers, decoded from the cross-context wire format.</summary>
    public readonly record struct Row(
        string Owner, string Label, int Width, int Height,
        long Frames, long Repaints,
        double LayoutMs, double InteractionMs, double FingerprintMs,
        double RenderMs, double ReadbackMs, double UploadMs, double MaxMs,
        long IdleMs)
    {
        public double TotalMs =>
            LayoutMs + InteractionMs + FingerprintMs + RenderMs + ReadbackMs + UploadMs;

        public double RepaintPct => Frames > 0 ? Repaints * 100.0 / Frames : 0;
        public string Display    => string.IsNullOrEmpty(Label) ? Owner : $"{Owner}/{Label}";

        /// <summary>
        /// True when the surface has not rendered recently — its window is closed or
        /// hidden. Its averages are frozen history, not present cost, so it must not be
        /// counted toward the frame budget.
        /// </summary>
        public bool IsIdle => IdleMs > 500;
    }

    /// <summary>
    /// Every PanacheUI surface in the process, across every plugin's own copy of the
    /// framework. Providers whose plugin has unloaded are dropped as they are discovered.
    /// </summary>
    public static List<Row> Collect()
    {
        var rows = new List<Row>();
        var registry = Registry();

        foreach (var kv in registry)
        {
            string blob;
            try { blob = kv.Value(); }
            catch { registry.TryRemove(kv.Key, out _); continue; }

            if (string.IsNullOrEmpty(blob)) continue;
            foreach (var line in blob.Split('\n'))
            {
                var f = line.Split('|');
                if (f.Length < 14) continue;
                rows.Add(new Row(
                    f[0], f[1], I(f[2]), I(f[3]), L(f[4]), L(f[5]),
                    D(f[6]), D(f[7]), D(f[8]), D(f[9]), D(f[10]), D(f[11]), D(f[12]), L(f[13])));
            }
        }

        rows.Sort((a, b) => b.TotalMs.CompareTo(a.TotalMs));   // most expensive first
        return rows;
    }

    private static int    I(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    private static long   L(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    private static double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    /// <summary>
    /// Human-readable summary for the in-game <c>/panacheui stats</c> command.
    /// </summary>
    public static List<string> ToChatLines()
    {
        var rows  = Collect();
        var lines = new List<string>();

        // Only surfaces that actually rendered recently are charged to the frame budget.
        double total = 0;
        int live = 0;
        foreach (var r in rows)
            if (!r.IsIdle) { total += r.TotalMs; live++; }

        lines.Add($"[Panache] {FrameRate:F0} fps ({FrameMs:F2} ms/frame) — {live} active surface(s), " +
                  $"{total:F2} ms/frame total");

        if (rows.Count == 0)
        {
            lines.Add("  No PanacheUI surfaces exist right now.");
            return lines;
        }

        foreach (var r in rows)
        {
            if (r.IsIdle)
            {
                lines.Add($"  {r.Display} {r.Width}x{r.Height} — idle (window closed), not counted");
                continue;
            }
            lines.Add($"  {r.Display} {r.Width}x{r.Height} — {r.TotalMs:F2} ms " +
                      $"(layout {r.LayoutMs:F2}, paint {r.RenderMs:F2}, " +
                      $"readback {r.ReadbackMs:F2}, texture {r.UploadMs:F2}) " +
                      $"repaint {r.RepaintPct:F0}%");
        }

        if (FrameMs > total && total > 0)
        {
            double without = 1000.0 / (FrameMs - total);
            lines.Add($"  Without Panache you would be at ~{without:F0} fps " +
                      $"(costing you ~{without - FrameRate:F0} fps).");
        }

        return lines;
    }

    /// <summary>
    /// Merged snapshot of every PanacheUI surface in the process, across all plugins,
    /// plus host frame timing and the implied frame-rate cost.
    /// </summary>
    public static string ToJson()
    {
        var rows = Collect();
        double total = 0;
        int live = 0;
        foreach (var r in rows) if (!r.IsIdle) { total += r.TotalMs; live++; }

        var sb = new StringBuilder(1024);
        sb.Append("{\"fps\":").Append(F(FrameRate))
          .Append(",\"frameMs\":").Append(F(FrameMs))
          .Append(",\"surfaces\":[");
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"owner\":\"").Append(r.Owner).Append('"');
            if (!string.IsNullOrEmpty(r.Label)) sb.Append(",\"label\":\"").Append(r.Label).Append('"');
            sb.Append(",\"size\":\"").Append(r.Width).Append('x').Append(r.Height).Append('"')
              .Append(",\"frames\":").Append(r.Frames)
              .Append(",\"repaints\":").Append(r.Repaints)
              .Append(",\"repaintPct\":").Append(F(r.RepaintPct))
              .Append(",\"idle\":").Append(r.IsIdle ? "true" : "false")
              .Append(",\"idleMs\":").Append(r.IdleMs)
              .Append(",\"avgMs\":{")
                  .Append("\"layout\":").Append(F(r.LayoutMs))
                  .Append(",\"interaction\":").Append(F(r.InteractionMs))
                  .Append(",\"fingerprint\":").Append(F(r.FingerprintMs))
                  .Append(",\"render\":").Append(F(r.RenderMs))
                  .Append(",\"readback\":").Append(F(r.ReadbackMs))
                  .Append(",\"upload\":").Append(F(r.UploadMs))
                  .Append(",\"total\":").Append(F(r.TotalMs))
              .Append("},\"maxTotalMs\":").Append(F(r.MaxMs))
              .Append('}');
        }
        sb.Append(']')
          .Append(",\"surfaceCount\":").Append(rows.Count)
          .Append(",\"activeSurfaceCount\":").Append(live)
          .Append(",\"totalAvgMs\":").Append(F(total));

        // What the frame rate would be if Panache cost nothing. Only meaningful while
        // the reported frame time actually exceeds Panache's own share.
        if (FrameMs > total && total > 0 && FrameMs > 0)
        {
            double without = 1000.0 / (FrameMs - total);
            sb.Append(",\"fpsWithoutPanache\":").Append(F(without));
            sb.Append(",\"fpsCost\":").Append(F(without - FrameRate));
        }

        sb.Append(",\"note\":\"avgMs are exponential moving averages over recent frames; ")
          .Append("render+upload are charged only on frames that actually repainted. ")
          .Append("Surfaces are merged across every plugin's own copy of PanacheUI.\"}");
        return sb.ToString();
    }

    internal static string F(double v) =>
        double.IsFinite(v) ? Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture) : "0";
}

/// <summary>Rolling cost record for one surface. Created and owned by <see cref="PanacheStats"/>.</summary>
public sealed class SurfaceStats
{
    /// <summary>Assembly that constructed the surface, e.g. "GlamourDresserHelper".</summary>
    public string Owner { get; }

    /// <summary>Optional caller-supplied label, shown alongside <see cref="Owner"/>.</summary>
    public string? Label { get; set; }

    public int Width  { get; internal set; }
    public int Height { get; internal set; }

    public long Frames   { get; private set; }
    public long Repaints { get; private set; }

    // Exponential moving averages. Alpha 1/60 ≈ a one-second window at 60 fps: long
    // enough to be readable while typing a query, short enough to track a mode change.
    private const double Alpha = 1.0 / 60.0;

    public double AvgLayoutMs      { get; private set; }
    public double AvgInteractionMs { get; private set; }
    public double AvgFingerprintMs { get; private set; }
    public double AvgRenderMs      { get; private set; }
    public double AvgUploadMs      { get; private set; }

    public double AvgReadbackMs { get; private set; }

    public double MaxTotalMs { get; private set; }

    /// <summary>
    /// When this surface last actually rendered, as <see cref="Environment.TickCount64"/>.
    /// </summary>
    /// <remarks>
    /// A surface object outlives its window: hiding a window just stops calling Render,
    /// it does not dispose the surface. Without this, a closed window kept reporting its
    /// final moving average forever, so the readout showed phantom cost for UI that was
    /// no longer on screen — and that phantom got added into the frame-cost total.
    /// </remarks>
    public long LastRenderTick { get; private set; }

    /// <summary>Average total cost per frame, repaint-weighted exactly as it is actually paid.</summary>
    public double AvgTotalMs =>
        AvgLayoutMs + AvgInteractionMs + AvgFingerprintMs + AvgRenderMs + AvgReadbackMs + AvgUploadMs;

    internal SurfaceStats(string owner, int width, int height)
    {
        Owner  = owner;
        Width  = width;
        Height = height;
    }

    internal void Reset()
    {
        Frames = Repaints = 0;
        AvgLayoutMs = AvgInteractionMs = AvgFingerprintMs = AvgRenderMs = AvgReadbackMs = AvgUploadMs = 0;
        MaxTotalMs = 0;
    }

    internal void Record(double layoutMs, double interactionMs, double fingerprintMs,
                         double renderMs, double readbackMs, double uploadMs, bool repainted)
    {
        Frames++;
        if (repainted) Repaints++;
        LastRenderTick = Environment.TickCount64;

        AvgReadbackMs += (readbackMs - AvgReadbackMs) * Alpha;

        AvgLayoutMs      += (layoutMs      - AvgLayoutMs)      * Alpha;
        AvgInteractionMs += (interactionMs - AvgInteractionMs) * Alpha;
        AvgFingerprintMs += (fingerprintMs - AvgFingerprintMs) * Alpha;
        // Render/upload are 0 on skipped frames, which is the honest per-frame average.
        AvgRenderMs      += (renderMs      - AvgRenderMs)      * Alpha;
        AvgUploadMs      += (uploadMs      - AvgUploadMs)      * Alpha;

        double total = layoutMs + interactionMs + fingerprintMs + renderMs + readbackMs + uploadMs;
        if (total > MaxTotalMs) MaxTotalMs = total;
    }

    /// <summary>
    /// Serialise to the pipe-delimited cross-context wire format. Field order must match
    /// <c>PanacheStats.Collect</c>. Owner and label are sanitised so a stray separator
    /// cannot shift every subsequent field.
    /// </summary>
    internal void AppendRecord(StringBuilder sb)
    {
        string f(double v) => PanacheStats.F(v);
        sb.Append(Clean(Owner)).Append('|')
          .Append(Clean(Label)).Append('|')
          .Append(Width).Append('|').Append(Height).Append('|')
          .Append(Frames).Append('|').Append(Repaints).Append('|')
          .Append(f(AvgLayoutMs)).Append('|')
          .Append(f(AvgInteractionMs)).Append('|')
          .Append(f(AvgFingerprintMs)).Append('|')
          .Append(f(AvgRenderMs)).Append('|')
          .Append(f(AvgReadbackMs)).Append('|')
          .Append(f(AvgUploadMs)).Append('|')
          .Append(f(MaxTotalMs)).Append('|')
          .Append(Math.Max(0, Environment.TickCount64 - LastRenderTick));
    }

    private static string Clean(string? s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace('|', '/').Replace('\n', ' ').Replace('"', '\'');
}

# Using PanacheUI from another plugin

**PanacheUI is vendored, not shared.** Your plugin ships its own copy. A user never
installs PanacheUI to run your plugin, and you must never ask them to.

---

## Why it can't be a shared install

Dalamud gives every plugin its own `PluginLoadContext`, and that context resolves managed
assemblies **only from that plugin's own folder**. PanacheUI installed from its pluginmaster
lands in *its* folder; your plugin's loader cannot see it. Without a bundled copy your
plugin throws `FileNotFoundException` on the first PanacheUI type it touches — for every
user, while working perfectly on a dev machine that happens to have `devPlugins\PanacheUI`.

There is no load-order trick that fixes this, and no instruction you can give a
non-technical user that should be necessary. They install your plugin from your
pluginmaster URL and it works. That's the whole contract.

The cost is ~10 MB per consuming plugin, almost all of it the native Skia binary. Pay it.

---

## What your zip must contain

Flat, beside your own DLL:

```
YourPlugin.dll
YourPlugin.json
PanacheUI.dll
SkiaSharp.dll
libSkiaSharp.dll        <-- native, ~9.6 MB
Icons/0001.png … 0167.png
Themes/…                <-- only if you load PanacheUI themes
```

`Icons/` is not optional if you call `PUI.Icon`. Without it every icon silently renders as
a grey placeholder swatch — a broken-looking UI that reports no error. It looks fine on
your machine because `PanacheIcons` searches `devPlugins\PanacheUI\Icons` first, which only
exists during development.

---

## 1. Build side — one line

```xml
<Import Project="$(APPDATA)\XIVLauncher\devPlugins\PanacheUI\PanacheUI.Consumer.props" />
```

That adds the `PanacheUI` + `SkiaSharp` references, copies the payload into your build
output and your dev-plugin folder, and **fails the build** if any of it is missing.

Remove any hand-rolled `<Reference Include="PanacheUI">` and copy targets when you adopt it
— the props file owns that list now.

### Knobs

Set these in a `<PropertyGroup>` **before** the import.

| Property | Default | Use it when |
|---|---|---|
| `PanacheUIPath` | `devPlugins\PanacheUI\` | Vendoring from somewhere else (CI extracts the zip here by default, so usually leave it). |
| `PanacheDeployPath` | `devPlugins\$(AssemblyName)\` | Your dev-plugin folder differs from your assembly name. |
| `PanacheIncludeIcons` | `true` | Set `false` **only** if you call neither `PUI.Icon` nor `PUI.CloseButton`. Saves 4.3 MB. |
| `PanacheIncludeThemes` | `false` | Set `true` if you load PanacheUI themes. Adds ~770 KB. |
| `PanacheUIMinimumVersion` | *(unset)* | Pin a version floor — see below. |

> **The icons trap.** `PUI.CloseButton` renders icon `#0005` internally. "I never call
> `PUI.Icon`" is *not* sufficient reason to set `PanacheIncludeIcons=false` — grep for
> `CloseButton` too. Get it wrong and the close button silently becomes a grey box.

### Version pinning

```xml
<PanacheUIMinimumVersion>0.1.6</PanacheUIMinimumVersion>
```

The build reads the **real assembly version out of the vendored `PanacheUI.dll`** and fails
if it's older:

```
error : PanacheUI 0.1.3.0 at '...\devPlugins\PanacheUI\' is older than the required 0.1.6.
```

Set it to the version whose features you actually use. This is what catches the common
local failure — a stale `devPlugins\PanacheUI` that hasn't been rebuilt since you pulled —
before it becomes "why does this API not exist".

To see what you're building against without pinning, the build prints it whenever the
property is set. At runtime, read it off the assembly:

```csharp
var panacheVersion = typeof(PanacheUI.Components.PUI).Assembly.GetName().Version;
Log.Information($"PanacheUI {panacheVersion}");
```

### Checking against the latest published build

The floor above is a *local* check. To confirm your vendored copy is the newest release,
compare against the GitHub API — do this **in CI or a maintenance script, never at plugin
runtime**:

```powershell
$latest  = (Invoke-RestMethod https://api.github.com/repos/Sansflaire/PanacheUI/releases/latest).tag_name.TrimStart('v')
$vendored = [Reflection.AssemblyName]::GetAssemblyName("$env:APPDATA\XIVLauncher\devPlugins\PanacheUI\PanacheUI.dll").Version
if ([version]$latest -gt $vendored) { Write-Warning "PanacheUI $vendored is behind latest $latest" }
```

CI already gets this for free: the fetch step below pulls `releases/latest`, so every CI
build vendors the newest PanacheUI by construction. The check is only useful for spotting a
stale *local* dev folder.

## 2. Runtime side — point the icon loader at your copy

```csharp
var dir = PluginInterface.AssemblyLocation.Directory?.FullName;
if (!string.IsNullOrEmpty(dir))
{
    var icons = Path.Combine(dir, "Icons");
    if (Directory.Exists(icons)) PanacheIcons.FolderOverride = icons;
}
```

Call this once during construction. `FolderOverride` ignores a path that doesn't exist, so
this is safe unconditionally and still falls back to the dev layout.

## 3. CI side — fetch PanacheUI, then package the payload

```yaml
- name: Fetch PanacheUI (vendored dependency)
  run: |
    $dest = "$env:APPDATA\XIVLauncher\devPlugins\PanacheUI"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Invoke-WebRequest -Uri "https://github.com/Sansflaire/PanacheUI/releases/latest/download/PanacheUI.zip" -OutFile panache.zip
    Expand-Archive panache.zip -DestinationPath $dest -Force
    foreach ($f in 'PanacheUI.dll','SkiaSharp.dll','libSkiaSharp.dll') {
      if (-not (Test-Path "$dest\$f")) { Write-Error "PanacheUI payload incomplete: $f" }
    }
  shell: pwsh
```

Then in `Package`, copy the vendored payload out of the build output alongside your DLL:

```powershell
foreach ($f in 'PanacheUI.dll','SkiaSharp.dll','libSkiaSharp.dll') {
  Copy-Item "$bin/$f" $dist
}
Copy-Item "$bin/Icons" $dist -Recurse
```

And assert against the finished zip, because a green build has never meant an installable
artifact:

```powershell
$names = [IO.Compression.ZipFile]::OpenRead((Resolve-Path "YourPlugin.zip")).Entries.FullName
foreach ($r in 'PanacheUI.dll','SkiaSharp.dll','libSkiaSharp.dll') {
  if ($names -notcontains $r) { Write-Error "Zip is missing $r - the plugin will not load." }
}
if (-not ($names | Where-Object { $_ -like 'Icons/*.png' })) { Write-Error "Zip has no icons." }
```

---

---

## Rejected designs, and why

These come up every time. Recording the reasoning so it isn't re-litigated.

### A single shared copy in AppData that every plugin loads

**Rejected.** Technically possible — an `AssemblyLoadContext.Resolving` handler in each
plugin could load `PanacheUI.dll` from a common folder, and it would save roughly
`(N-1) × 10 MB`.

What it costs:

- **One version for everyone.** A breaking PanacheUI change breaks all N plugins
  simultaneously. Vendoring means each plugin upgrades when *it* is rebuilt and tested.
- **Nobody owns the shared copy.** Whichever plugin writes it wins; the DLL is file-locked
  once any plugin has loaded it, so the next writer fails. Install/update ordering becomes
  load-bearing.
- **The failure is worse.** A missing vendored DLL fails at that one plugin's load. A
  corrupt or mismatched shared DLL takes down every Panache plugin at once.

The disk saving is real but small against what it buys. Modern installs are ~14 MB per
consumer; the isolation is worth more.

### Downloading PanacheUI at runtime from GitHub

**Rejected, and don't revisit.** Fetching and loading executable code at runtime is a
supply-chain problem, breaks offline play, adds a 9.2 MB network fetch to plugin load, and
lets a plugin built against 0.1.6 silently pull an incompatible 0.2.0 on a user's machine
with no test coverage. Dalamud expects plugins to be self-contained.

Note what *is* already true: PanacheUI's release **is** the public artifact consumers pull
from — at **build** time, pinned into the zip, tested before it ships. That's the safe form
of the same idea, and it's what the CI step below does.

---

## Release ordering

PanacheUI must be released **before** any consumer in the same patch cycle — consumer CI
fetches `releases/latest`, so a consumer built first vendors the previous PanacheUI. See
[`devPlugins/BROKEN.md`](../BROKEN.md).

## Reference implementation

`TieriChallengesFFXIV` — `scripts/build-public.ps1` for the release payload and
`Plugin.TrySetIconFolder` for the runtime override. It hit both the missing-icons and
missing-DLL failures the hard way; copy it rather than re-deriving.

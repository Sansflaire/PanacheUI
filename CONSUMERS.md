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

That adds the `PanacheUI` + `SkiaSharp` references, copies all three DLLs and `Icons/` into
your build output and your dev-plugin folder, and **fails the build** if any of it is
missing. Set `<PanacheIncludeThemes>true</PanacheIncludeThemes>` before the import if you
load themes.

Remove any hand-rolled `<Reference Include="PanacheUI">` and copy targets when you adopt it
— the props file owns that list now.

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

## Release ordering

PanacheUI must be released **before** any consumer in the same patch cycle — consumer CI
fetches `releases/latest`, so a consumer built first vendors the previous PanacheUI. See
[`devPlugins/BROKEN.md`](../BROKEN.md).

## Reference implementation

`TieriChallengesFFXIV` — `scripts/build-public.ps1` for the release payload and
`Plugin.TrySetIconFolder` for the runtime override. It hit both the missing-icons and
missing-DLL failures the hard way; copy it rather than re-deriving.

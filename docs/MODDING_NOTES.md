# How StS2 loads mods

Notes from the decompiled `MegaCrit.Sts2.Core.Modding` namespace, game v0.111.0. Check these again after big patches.

## Discovery

- `ModManager.Initialize` scans `<game dir>/mods` and `<game dir>/mods_STEAMTEST` **recursively**, plus subscribed Steam Workshop items.
- Every `*.json` file it finds is parsed as a manifest. A JSON file with no `id` is skipped, or logged as an error if it looks like a manifest. **Don't drop other JSON files into a mod's folder** (for example `*.deps.json`). Our install step only copies the dll, pdb, manifest and pck.
- The `-nomods` launch argument skips all mod loading.
- If the same mod id is loaded from both local `mods/` and the Workshop, the higher version wins. If the versions are equal, the local copy wins.

## Manifest (`ModManifest`)

```json
{
  "id": "SpireQoL",              // required; the game loads <id>.dll and <id>.pck from this folder
  "name": "...",
  "author": "...",
  "description": "...",
  "version": "0.1.0",
  "has_dll": true,
  "has_pck": false,
  "affects_gameplay": false,     // defaults to TRUE; display-only mods should set false
  "dependencies": [ { "id": "BaseLib", "min_version": "..." } ],
  "min_game_version": "v0.111.0"
}
```

Writing `dependencies` as a list of plain strings is the old style. It still loads, but logs a migration error.

## Entry point

- The game looks for a type with `[ModInitializer("MethodName")]` and calls that method. **It must be static.** It takes no arguments.
- If no type has the attribute, the game calls `new Harmony(...).PatchAll(assembly)` instead.
- We always use the attribute, so `FeatureHost` can patch each feature separately and keep failures contained.

## Game-provided libraries

These live in `data_sts2_windows_x86_64/`: `sts2.dll`, `GodotSharp.dll`, `0Harmony.dll` (HarmonyX/Harmony 2.4.2). We compile against them with `Private=false`, so they're never copied into a mod.

## Logging

`MegaCrit.Sts2.Core.Logging.Log.Info/Warn/Error(string, int skipFrames)`. Use `ModLog` from core, which adds a `[ModId]` prefix to each line.

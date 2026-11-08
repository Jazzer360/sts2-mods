# sts2-mods

Monorepo of Slay the Spire 2 mods (C# net9.0, Harmony, Godot). Design notes: docs/ROADMAP.md. Loader facts: docs/MODDING_NOTES.md.

- Build: `dotnet build` (also installs to `<game>/mods/<ModId>/`; `-p:InstallToGame=false` to skip).
- Game assemblies are referenced from the install via Directory.Build.props/targets. Never commit game DLLs or decompiled code.
- Decompiled game source (read-only reference): `C:\Users\jazze\git\sts2-decompiled` (see its VERSION.json). Look up real class/method names there before writing a Harmony patch; don't guess targets.
- `core/` is compiled into each mod via a linked `<Compile Include>`; it is not a separate DLL.
- Mod AssemblyName == manifest `id` == `<ModId>.json` filename.
- Every feature implements `IFeature` and is applied through `FeatureHost`, so one broken patch can't crash the game.

# Building and installing

## Requirements

1. **Idle Slayer** installed through Steam.
2. **BepInEx 6.0.0-be.788 IL2CPP win-x64** installed in the game folder, and the game started **at least once** with it: that first launch generates `BepInEx\interop\` (the assemblies this project compiles against).
3. **.NET 8 SDK**:
   ```powershell
   winget install Microsoft.DotNet.SDK.8
   ```
   Check with `dotnet --version`.

## Build

```powershell
cd $env:USERPROFILE\Downloads\SlayerTools-en
.\build.ps1             # build + copy into BepInEx\plugins
.\build.ps1 -NoInstall  # build only (DLL in src\bin\Release\net6.0\)
```

Or by hand:

```powershell
cd src
dotnet build -c Release                  # build
dotnet build -c Release -p:Install=true  # build + copy into BepInEx\plugins
```

The game must be **closed** to replace the DLL (it is locked while the game runs).

### Why `net6.0`?
BepInEx 6 IL2CPP ships the **.NET 6** runtime. The .NET 8 SDK can target `net6.0` (it downloads the targeting pack from NuGet on the first build). The original sandbox had no NuGet access, so releases were built for `net8.0` and then patched with `tools/retarget`. You do not need that.

### Steam installed somewhere else?
The BepInEx folder is set in `Directory.Build.props`. Change it there, or:

```powershell
dotnet build -c Release -p:BepInExDir="D:\SteamLibrary\steamapps\common\Idle Slayer\BepInEx"
.\build.ps1 -BepInExDir "D:\SteamLibrary\steamapps\common\Idle Slayer\BepInEx"
```

## Editors
- **VS Code** + *C# Dev Kit*: open the repository folder.
- **Visual Studio 2022** / **Rider**: open `src\SlayerTools.csproj`.

IntelliSense knows every game class thanks to the interop assemblies.

## Debugging
- Full log: `BepInEx\LogOutput.log`. Every line from the mod starts with `[Slayer Tools]`.
- BepInEx console: in `BepInEx\config\BepInEx.cfg`, section `[Logging.Console]`, set `Enabled = true`.
- The native menu disables itself if it crashes (`Native menu disabled: …` in the log); the game keeps running normally.
- `F11` writes the whole game UI tree to `BepInEx\slayer_ui_dump.txt` (paths, sizes, sprites, texts).

## After a game update
1. Start the game once: BepInEx regenerates `BepInEx\interop\`.
2. Rebuild: compile errors show what was renamed or removed.
3. Update `ExpectedGameVersion` in `src\Features.cs`.

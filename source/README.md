# Interface mod source

`src/VerticalDialogue.cs` implements layout, scrolling, HUD behaviour, and display formatting. `src/HangingIndent.cs` adjusts text meshes while preserving hyperlink geometry. `patcher/Program.cs` applies targeted Mono.Cecil patches to the original game assembly.

## Build

Requires Windows, the .NET 10 SDK, the game's original managed libraries, and `Unity.Cecil.dll` from a Unity Editor installation. These third-party binaries are not included. The supported pristine `Assembly-CSharp.dll` has SHA-256 `677CB9884B3ADC18A65005D37A55784164821F484F970339CD15297105686664`.

Save an original assembly before installing the mod. Do not use the modified DLL distributed at the repository root as build input.

```powershell
.\Build.ps1 -GameManaged 'D:\SteamLibrary\steamapps\common\Torment Tides of Numenera\WIN\TidesOfNumenera_Data\Managed' -OriginalAssembly 'D:\Backups\Torment\Assembly-CSharp.dll' -CecilPath 'D:\tools\Unity\6000.0.85f1\Editor\Data\Managed\Unity.Cecil.dll'
```

Output defaults to `build/TidesOfNumenera_Data/Managed`. Copy both generated DLLs into the game's matching folder with the game closed. The build does not install files automatically. Unity shared asset files remain untouched; interface art changes happen at runtime using the original sprites.

The English text edits are distributed separately under the repository's root `TidesOfNumenera_Data/StreamingAssets` folder. This source folder builds the interface DLLs only.

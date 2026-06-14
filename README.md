# StartWithAspect

A **Risk of Rain 2** mod that lets you pick an **elite aspect** to start the run with, straight from the **Loadout** tab on the character select screen.

![Preview](Thunderstore/icon.png)

## Features

- An **ASPECT** row added to the Loadout tab: click an icon to choose your starting aspect (or "none").
- All aspects are detected automatically, **DLC included** (Blazing, Overloading, Glacial, Malachite, Celestine, Perfected, Void, etc.).
- When the run starts, your character spawns with the aspect equipped (you become that elite: aura + effect).
- **Multiplayer**: each player picks and starts with their own aspect (synchronized via R2API Networking).
- A `Starting aspect` config option as a fallback selector.

## Dependencies

- BepInEx (BepInExPack)
- R2API Networking
- HookGenPatcher

## Building

Open `RoR2Mods.sln` in Visual Studio (with the ".NET desktop development" workload), let NuGet restore the packages, then build the solution (`Ctrl+Shift+B`). The `.dll` is produced in `ExamplePlugin/bin/<Config>/netstandard2.1/`.

Copy that `.dll` into `BepInEx/plugins/StartWithAspect/` of your profile (r2modman recommended) to test it.

## Publishing

The `Thunderstore/` folder contains `manifest.json`, `icon.png` (256×256), `README.md` and `CHANGELOG.md`, ready to be packed into a `.zip` and uploaded to [Thunderstore](https://thunderstore.io/c/riskofrain2/).

## License

MIT — see `LICENSE`.

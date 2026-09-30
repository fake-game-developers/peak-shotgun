# peak-shotgun

![Shotgun preview](https://raw.githubusercontent.com/fake-game-developers/peak-shotgun/master/media/img1.jpg)

![Shotgun preview](https://raw.githubusercontent.com/fake-game-developers/peak-shotgun/master/media/img2.jpg)

![Shotgun preview](https://raw.githubusercontent.com/fake-game-developers/peak-shotgun/master/media/img3.jpg)

Turns PEAK's leftover blowgun into a shotgun.

A blast throws eight pellets in a short cone, about every 0.85 seconds. Scouts who are hit take a heavy Injury and get flipped. Zombies ignore Injury. The first hit flips them and adds Drowsy. The second hit knocks them out, and they stay down until PEAK kills them. See the [zombie page](https://peak.wiki.gg/wiki/Zombie).

## Requirements

- BepInExPack for PEAK
- [PEAKLib Core](https://thunderstore.io/c/peak/p/PEAKModding/PEAKLib_Core/)
- [PEAKLib Items](https://thunderstore.io/c/peak/p/PEAKModding/PEAKLib_Items/)

PEAK does not include a shotgun model. The mod builds the gun from the blowgun already in the game, names it Shotgun, and makes it fire on its own. Installing the mod is the whole setup.

## Where it spawns

- **Roots:** every run, **at least two random luggage** anywhere on the Roots map are forced to contain a shotgun. This always happens; no config turns it off.
- **Other biomes:** by default the shotgun does **not** appear in luggage outside Roots.
- Set `CanSpawnOnAnyBiome = true` under `[Shotgun]` in `BepInEx/config/PeakShotgun.cfg` to also allow random shotgun rolls in luggage on any biome. Roots still forces two suitcases either way.

It looks like the blowgun, because that is the model PEAK ships. It does not replace the blowgun.

## Configuration

After one launch, edit `BepInEx/config/PeakShotgun.cfg`:

| Section | Key | Default | Meaning |
|---|---|---|---|
| `[Shotgun]` | `CanSpawnOnAnyBiome` | `false` | `false` = Roots luggage only (plus the two forced suitcases). `true` = can also roll in luggage in other biomes |
| `[Shotgun]` | `Shots` | `5` | Shots per shotgun |
| `[Shotgun]` | `EnableRecoil` | `true` | Push the shooter back on each shot |
| `[Shotgun]` | `Recoil` | `5` | Recoil strength in m/s (0–10) |
| `[Debug]` | `EnableDebugMode` | `false` | Host spawns test zombies, a shotgun, and luggage at the Airport / Shore |

## Install

1. Install the package with r2modman / Gale, **or** drop `PeakShotgun.dll` into `BepInEx/plugins/PeakShotgun/`.
2. Launch PEAK from r2modman. Starting the game from Steam skips BepInEx.

## How to test

1. Set `EnableDebugMode = true` under `[Debug]` in the PeakShotgun config file (it appears after one launch). Host a run. On the Shore, three zombies appear near you, and a Shotgun is on the ground in front of you. With debug mode off (the default) nothing is force-spawned there; open Roots luggage to find the two guaranteed shotguns.
2. Press primary fire. It shoots a spread of pellets about every 0.85 seconds, plays the shotgun fire sound and pushes you back a little. Each shotgun has 5 shots. Tune `Shots`, `EnableRecoil`, and `Recoil` under `[Shotgun]` as needed.
3. Shoot another scout. They take a heavy Injury and get flipped. You cannot hit yourself.
4. Reach the Roots (Peak ascent or higher for natural zombies). Open luggage until you find a shotgun, then shoot a zombie. The first blast flips it, and it can get back up. The second blast knocks it out, and it stays down. Or use debug Shore zombies from step 1.

## Build

Requires [PEAK](https://store.steampowered.com/app/3527290/PEAK/) installed (auto-detected under common Steam paths), or set `PEAK_GAME_DIR` / `-p:PeakGameRootDir=`.

```bash
dotnet build Peak.Shotgun.slnx -c Release
```

Optional deploy: `-p:DeployToPeak=true -p:PeakPluginsDir="/path/to/BepInEx/plugins/PeakShotgun"`  
Optional overrides: copy `Config.Build.user.props.example` → `Config.Build.user.props` (gitignored).

The DLL is `artifacts/bin/Peak.Shotgun/release/PeakShotgun.dll`.

## Developers

[AGENTS.md](AGENTS.md) is the code map: what each type does, where icons and sounds live, and how zombie knockdown and ammo work.

## Thunderstore packaging (CI)

Every push to `master` runs [.github/workflows/thunderstore.yml](.github/workflows/thunderstore.yml):

1. Builds a Thunderstore ZIP (using stripped [PEAKGameLibs](https://www.nuget.org/packages/PEAKGameLibs) for compile references)
2. Uploads a workflow artifact named **`FakeGameDevelopers-PeakShotgun`**

Every push still builds that zip. Thunderstore publish runs only when `<Version>` in `src/PeakShotgun/Peak.Shotgun.csproj` changes, and the organization secret `TCLI_AUTH_TOKEN` is set. The publish step copies the categories already on the package, so a new version keeps the same tags. A commit that leaves the version unchanged only builds the artifact.

### Local package build

```bash
./build.sh
# or: dotnet build Peak.Shotgun.slnx -c Release -target:PackTS
# zip lands in artifacts/thunderstore/
```

## Credits

- Original author: **Arman Ossi Loko**
- This mod belongs to **Fake Game Developers**
- Based on [Peak_AKGun](https://github.com/TheCodinPro/Peak_AKGun) by TheCodinPro

### Contributors

- [bekto](https://github.com/bekto)

Work based on this mod must credit Arman Ossi Loko and Fake Game Developers. See [LICENSE](LICENSE).

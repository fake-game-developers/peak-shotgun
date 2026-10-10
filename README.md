# peak-shotgun

![Shotgun preview](https://raw.githubusercontent.com/fake-game-developers/peak-shotgun/master/media/img1.jpg)

![Shotgun preview](https://raw.githubusercontent.com/fake-game-developers/peak-shotgun/master/media/img2.jpg)

![Shotgun preview](https://raw.githubusercontent.com/fake-game-developers/peak-shotgun/master/media/img3.jpg)

![Shotgun preview](https://raw.githubusercontent.com/fake-game-developers/peak-shotgun/master/media/img4.jpg)

Turns PEAK's leftover blowgun into a shotgun.

A blast throws eight pellets in a short cone, about every 0.85 seconds. Scouts who are hit take a heavy Injury and get flipped. Zombies ignore Injury. The first hit flips them and adds Drowsy. The second hit knocks them out, and they stay down until PEAK kills them. See the [zombie page](https://peak.wiki.gg/wiki/Zombie).

## Requirements

- BepInExPack for PEAK
- [PEAKLib Core](https://thunderstore.io/c/peak/p/PEAKModding/PEAKLib_Core/)
- [PEAKLib Items](https://thunderstore.io/c/peak/p/PEAKModding/PEAKLib_Items/)

PEAK does not include a shotgun model. The mod builds the gun from the blowgun already in the game, names it Shotgun, and makes it fire on its own. Installing the mod is the whole setup.

## Where it spawns

- **On spawn (optional):** set `GiveShotgunOnSpawn = true` and the **host** puts one shotgun in each scout's hand when they enter the climb or join mid-run (Shore and later — not at the Airport; once per player per run).
- **Roots:** every run, **at least two random luggage** anywhere on the Roots map are forced to contain a shotgun. No mod config turns it off, but a custom run that disables the shotgun in its item settings gets none.
- **Other biomes:** luggage outside Roots is locked out only when **Zombies is the sole enabled** `[Shootables]` target. Any other shootables mix (or `CanSpawnOnAnyBiome = true`) allows **RidiculouslyRare** rolls in every biome. Roots still forces two suitcases either way.
- **Cap:** at most **4** luggage per biome can contain a shotgun in a single run (including Roots).
- **Campfire ammo:** by default the **host** places ammo piles at each segment campfire (`[AmmoPile] SpawnAtCampfire`). Hold a shotgun with fewer than `Shots` left, interact (**REFILL**), and the magazine fills up to `Shots` (never above). The pile stays. Ammo piles never appear in luggage. Set `SpawnAtCampfire = false` to disable.

It looks like the blowgun, because that is the model PEAK ships. It does not replace the blowgun.

## Configuration

After one launch, edit `BepInEx/config/PeakShotgun.cfg`:

| Section | Key | Default | Meaning |
|---|---|---|---|
| `[Shotgun]` | `CanSpawnOnAnyBiome` | `false` | Force rare rolls in every biome. When `false`, Roots-only only if Zombies is the sole `[Shootables]` target |
| `[Shotgun]` | `GiveShotgunOnSpawn` | `false` | Host only: one shotgun in hand per scout when they spawn into the climb / join mid-run (not Airport) |
| `[Shotgun]` | `Shots` | `5` | Shots per shotgun (host's value in multiplayer) |
| `[Shotgun]` | `EnableRecoil` | `true` | Push the shooter back on each shot |
| `[Shotgun]` | `Recoil` | `5` | Recoil strength in m/s (0–10) |
| `[AmmoPile]` | `SpawnAtCampfire` | `true` | Host only: place ammo piles at segment campfires |
| `[AmmoPile]` | `PilesPerCampfire` | `1` | How many piles per campfire (1–8) when spawning is on |
| `[Combat]` | `FriendlyFire` | `true` | `true` = injure other scouts. `false` = knockback only, no Injury |
| `[Shootables]` | `Zombies` | `true` | Hit / knock down zombies |
| `[Shootables]` | `Mandrake` | `false` | Destroy mandrakes |
| `[Shootables]` | `Beetles` | `false` | Kill beetles |
| `[Shootables]` | `Spiders` | `false` | Stun spiders |
| `[Shootables]` | `Spores` | `false` | Break spore bombs (spore, explosive, poison) / clear spore clouds |
| `[Shootables]` | `Scorpions` | `false` | Kill scorpions |
| `[Shootables]` | `Dynamite` | `false` | Light dynamite fuses |
| `[Debug]` | `EnableDebugMode` | `false` | Host spawns test zombies, a shotgun, and luggage at the Airport / Shore |

In multiplayer, `Shots`, `FriendlyFire`, and every `[Shootables]` toggle come from the host, so the whole lobby plays by the same rules. Recoil and the hold pose stay per player.

The host keeps track of which suitcases gave a shotgun in each run, in `BepInEx/config/PeakShotgun.loot.json`, so the per-biome cap and the Roots picks survive loading a quicksave or the host leaving mid-run.

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

1. Builds a Thunderstore ZIP (using [FakeGameDevelopers.PEAKGameLibs](https://github.com/fake-game-developers/PEAKGameLibs) stripped refs for compile references)
2. Uploads a workflow artifact named **`FakeGameDevelopers-PeakShotgun`**

Every push still builds that zip. Thunderstore publish runs when `<Version>` in `src/PeakShotgun/Peak.Shotgun.csproj` is not yet the live Thunderstore release, and the organization secret `TCLI_AUTH_TOKEN` is set. The publish step copies the categories already on the package, so a new version keeps the same tags. A commit that leaves the version matching live only builds the artifact.

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

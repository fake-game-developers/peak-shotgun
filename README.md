# peak-shotgun

![Shotgun preview](https://raw.githubusercontent.com/fake-game-developers/peak-shotgun/master/media/img1.jpg)

![Shotgun preview](https://raw.githubusercontent.com/fake-game-developers/peak-shotgun/master/media/img2.jpg)

![Shotgun preview](https://raw.githubusercontent.com/fake-game-developers/peak-shotgun/master/media/img3.jpg)

![Shotgun preview](https://raw.githubusercontent.com/fake-game-developers/peak-shotgun/master/media/img4.jpg)

Turns PEAK's leftover blowgun into a shotgun.

A blast throws eight pellets in a short cone, about every 0.85 seconds. Scouts who are hit take a heavy Injury and get flipped. Zombies ignore Injury. The first hit flips them and adds Drowsy. The second hit knocks them out, and they stay down until PEAK kills them. See the [zombie page](https://peak.wiki.gg/wiki/Zombie). You can also shoot condors, antlions, Gloom frogs (they drop cooked frog legs), beehives, ticks, jellyfish, and coconuts on palm trees, scatter bee swarms, blow Mesa tumbleweeds away, pop balloons (including the ones keeping a scout afloat), and get a reaction out of Bing Bong.

## Requirements

- BepInExPack for PEAK
- [PEAKLib Core](https://thunderstore.io/c/peak/p/PEAKModding/PEAKLib_Core/)
- [PEAKLib Items](https://thunderstore.io/c/peak/p/PEAKModding/PEAKLib_Items/)

PEAK does not include a shotgun model. The mod builds the gun from the blowgun already in the game, names it Shotgun, and makes it fire on its own. Installing the mod is the whole setup.

## Where it spawns

- **On spawn (optional):** set `GiveShotgunOnSpawn = true` and the **host** puts one shotgun in each scout's hand when they enter the climb or join mid-run (Shore and later — not at the Airport; once per player per run).
- **Roots:** every run, **at least two random luggage** anywhere on the Roots map are forced to contain a shotgun. No mod config turns it off, but a custom run that disables the shotgun in its item settings gets none.
- **Other biomes:** by default every `[Shootables]` target is on, so luggage outside Roots can roll a shotgun (**RidiculouslyRare**). Luggage stays Roots-only only when **Zombies is the sole enabled** shootable, not counting Scoutmaster, tumbleweeds, bee swarms, Bing Bong and balloons (or set `CanSpawnOnAnyBiome = true` to force every biome). Roots still forces two suitcases either way.
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
| `[Shotgun]` | `CarryWeight` | `4` | Stamina-bar weight (1 = 2.5). Default 4 = 10. 0 matches an empty backpack |
| `[AmmoPile]` | `SpawnAtCampfire` | `true` | Host only: place ammo piles at segment campfires |
| `[AmmoPile]` | `PilesPerCampfire` | `1` | How many piles per campfire (1–8) when spawning is on |
| `[Shootables]` | `Scouts` | `true` | Injure / ragdoll other scouts |
| `[Shootables]` | `Zombies` | `true` | Hit / knock down zombies |
| `[Shootables]` | `Mandrake` | `true` | Destroy mandrakes |
| `[Shootables]` | `Beetles` | `true` | Kill beetles |
| `[Shootables]` | `Spiders` | `true` | Stun spiders |
| `[Shootables]` | `Spores` | `true` | Break spore bombs (spore, explosive, poison) / clear spore clouds |
| `[Shootables]` | `Scorpions` | `true` | Kill scorpions |
| `[Shootables]` | `Dynamite` | `true` | Explode dynamite on hit |
| `[Shootables]` | `Scoutmaster` | `true` | Drive off Scoutmaster Myres after enough hits |
| `[Shootables]` | `Condors` | `true` | Kill Caldera condors (drops anyone they are carrying) |
| `[Shootables]` | `Antlions` | `true` | Disable Mesa antlion pits |
| `[Shootables]` | `Frogs` | `true` | Kill Gloom frogs (drops cooked frog legs) |
| `[Shootables]` | `Beehives` | `true` | Smash Tropics beehives (honeycomb drops) |
| `[Shootables]` | `Ticks` | `true` | Knock ticks off scouts |
| `[Shootables]` | `Jellyfish` | `true` | Smash Shore jellyfish |
| `[Shootables]` | `Coconuts` | `true` | Knock whole coconuts off palm trees |
| `[Shootables]` | `Tumbleweeds` | `true` | Blow Mesa tumbleweeds away (every hit pushes) |
| `[Shootables]` | `BeeSwarms` | `true` | Scatter bee swarms, angry or not |
| `[Shootables]` | `BingBong` | `true` | Bing Bong squeaks, says something, and flies (every hit) |
| `[Shootables]` | `Balloons` | `true` | Pop balloons tied to scouts (they lose that lift) and balloon items on the ground or in a hand |
| `[HitsToDefeat]` | `Zombies` | `2` | Shots to knock down a zombie |
| `[HitsToDefeat]` | `Mandrake` | `1` | Shots to destroy a mandrake |
| `[HitsToDefeat]` | `Beetles` | `1` | Shots to kill a beetle |
| `[HitsToDefeat]` | `Spiders` | `1` | Shots to stun a spider |
| `[HitsToDefeat]` | `Spores` | `1` | Shots to break a spore bomb / clear a spore cloud |
| `[HitsToDefeat]` | `Scorpions` | `1` | Shots to kill a scorpion |
| `[HitsToDefeat]` | `Dynamite` | `1` | Shots to explode dynamite |
| `[HitsToDefeat]` | `Scoutmaster` | `4` | Shots to drive off the Scoutmaster |
| `[HitsToDefeat]` | `Condors` | `1` | Shots to kill a condor |
| `[HitsToDefeat]` | `Antlions` | `1` | Shots to disable an antlion |
| `[HitsToDefeat]` | `Frogs` | `1` | Shots to kill a frog (drops cooked frog legs) |
| `[HitsToDefeat]` | `Beehives` | `1` | Shots to smash a beehive |
| `[HitsToDefeat]` | `Ticks` | `1` | Shots to knock a tick off |
| `[HitsToDefeat]` | `Jellyfish` | `1` | Shots to smash a jellyfish |
| `[HitsToDefeat]` | `Coconuts` | `1` | Shots to knock a coconut off a tree |
| `[HitsToDefeat]` | `BeeSwarms` | `1` | Shots to scatter a bee swarm |
| `[Feedback]` | `HitMarkers` | `true` | Flash ticks on the crosshair when your shot hits something; larger red ticks when it downs a living target (not coconuts, dynamite, beehives or spores) |
| `[Feedback]` | `Tracers` | `true` | Short streaks along each pellet's path |
| `[Feedback]` | `ImpactEffects` | `true` | Sparks and dust where pellets land |
| `[Debug]` | `EnableDebugMode` | `false` | Host spawns test zombies, a shotgun, and luggage at the Airport / Shore |

Fellow scouts are not under `[HitsToDefeat]`: each hit still applies Injury and knockback when `[Shootables] Scouts` is on.

In multiplayer, `Shots`, every `[Shootables]` toggle, and every `[HitsToDefeat]` value come from the host, so the whole lobby plays by the same rules. Recoil, carry weight, the hold pose and everything under `[Feedback]` stay per player.

The host keeps track of which suitcases gave a shotgun in each run, in `BepInEx/config/PeakShotgun.loot.json`, so the per-biome cap and the Roots picks survive loading a quicksave or the host leaving mid-run.

## Translations

The shotgun and ammo pile names and the refill prompt follow PEAK's language setting (all 15 languages). To fix or change a translation, create `BepInEx/config/PeakShotgun.localization.csv` with a `Key` column and one column per language, for example:

```csv
Key,English,German
NAME_SHOTGUN,SHOTGUN,SCHROTFLINTE
PEAKSHOTGUN_REFILL,refill,nachfüllen
```

Keys are `NAME_SHOTGUN`, `NAME_AMMO PILE` and `PEAKSHOTGUN_REFILL`. Language columns are `English`, `French`, `Italian`, `German`, `SpanishSpain`, `SpanishLatam`, `BRPortuguese`, `Russian`, `Ukrainian`, `SimplifiedChinese`, `TraditionalChinese`, `Japanese`, `Korean`, `Polish` and `Turkish`. Empty cells keep the built-in text.

## Install

1. Install the package with r2modman / Gale, **or** drop `PeakShotgun.dll` into `BepInEx/plugins/PeakShotgun/`.
2. Launch PEAK from r2modman. Starting the game from Steam skips BepInEx.

## How to test

1. Set `EnableDebugMode = true` under `[Debug]` in the PeakShotgun config file (it appears after one launch). Host a run. On the Shore, three zombies appear near you, and a Shotgun is on the ground in front of you. With debug mode off (the default) nothing is force-spawned there; open Roots luggage to find the two guaranteed shotguns.
2. Press primary fire. It shoots a spread of pellets about every 0.85 seconds, plays the shotgun fire sound, draws pellet tracers with sparks where they land and pushes you back a little. A hit flashes ticks on the crosshair. Each shotgun has 5 shots. Tune `Shots`, `EnableRecoil`, and `Recoil` under `[Shotgun]` as needed.
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

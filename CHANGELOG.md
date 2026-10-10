# Changelog

## 1.0.14

- Enable `[AmmoPile] SpawnAtCampfire` by default
- Remove `[AmmoPile] Scale` (ammo pile size is fixed in the mod)

## 1.0.13

- Add optional campfire **ammo piles** (`[AmmoPile] SpawnAtCampfire`, default off): the host places crate piles at each segment campfire (same moment as marshmallows)
- Hold a shotgun with fewer than `Shots` remaining, look at a pile, and **REFILL** to top up the magazine (never above `Shots`); the pile stays for others
- `[AmmoPile] PilesPerCampfire` (1–8) controls how many piles spawn per fire; piles never roll from luggage
- Debug mode also drops a test ammo pile next to the Shore / Airport test shotgun

## 1.0.12

- Thunderstore package readme is now player-facing only (no build/CI docs)

## 1.0.11

- Add `[Shotgun] GiveShotgunOnSpawn` (default off): host gives each scout one shotgun in hand when they spawn into the climb or join mid-run (not at the Airport)

## 1.0.10

- Luggage stays Roots-only only when Zombies is the sole enabled shootable; any other `[Shootables]` mix unlocks rare rolls in every biome

## 1.0.9

- Fix scouts sometimes taking no Injury or knockback when shot by a non-host player in multiplayer

## 1.0.8

- Fix `[Shootables] Spores`: hit real spore bombs (`Breakable` SporeShroom / ExploShroom / PoisonShroom), not Cloud Fungus

## 1.0.7

- Host authorizes each shot before it fires, so ammo and hits stay consistent in multiplayer
- `Shots`, `FriendlyFire`, and every `[Shootables]` toggle come from the host for the whole lobby
- Roots picks and the per-biome shotgun cap survive quicksave reloads and host migration (`PeakShotgun.loot.json`)
- Custom runs that disable the shotgun no longer get forced Roots luggage shotguns
- Remaining ammo updates the correct inventory slot (by gun instance), not just the selected hotbar slot
- Knocked-down zombies stay silenced for late joiners and after host migration
- Require PEAKLib Core 1.7.2

## 1.0.6

- Add `[Shootables]` toggles for zombies, mandrake, beetles, spiders, spores, scorpions, and dynamite (only zombies on by default)
- Add `[Combat] FriendlyFire` (default on): off keeps scout knockback but removes Injury

## 1.0.5

- Make luggage shotgun spawns much rarer when allowed outside Roots (`RidiculouslyRare` instead of `Common`)
- Cap shotgun luggage at 4 per biome per run

## 1.0.4

- Fix ammo resetting to full when a shotgun is dropped or given to another player
- Remaining shots stay in sync for everyone in the lobby, including late joiners
- Magazine size and remaining shots are host-authoritative (clients use the host's `Shots` value)

## 1.0.3

- Changing `Shots` in the config now applies on a new map without quitting PEAK
- Newly spawned shotguns always use the current `Shots` value

## 1.0.2

- CI publish test bump

## 1.0.1

- Fix README preview images on the Thunderstore package page (absolute GitHub URLs)

## 1.0.0

- Builds a shotgun from the blowgun already in PEAK, so the mod needs no extra files
- Roots always forces two random luggage to have a shotgun; `CanSpawnOnAnyBiome` (default false) allows rolls in other biomes
- With `EnableDebugMode` on, spawns three test zombies and a shotgun near you on the Shore. Off by default
- Each shot pushes the shooter back. `EnableRecoil` turns it on or off, `Recoil` sets the strength (0-10)
- Blasts injure scouts and flip them
- Each shotgun holds 5 shots, set by the `Shots` config entry. The hotbar name and shoot prompt show how many are left
- Knocked-out zombies stop making sounds while the game still has them alive
- Hotbar icon is a shotgun drawing
- Each shot flashes a short light at the barrel
- Shot fire sound is `shotgun_fire_01.wav`
- Blasts flip zombies harder than a coconut and add Drowsy damage

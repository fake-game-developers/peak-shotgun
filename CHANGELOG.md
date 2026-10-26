# Changelog

## 1.0.21

- New shootables, all on by default: blow Mesa tumbleweeds away, scatter bee swarms, pop balloons (tied to a scout, on the ground, or in a hand), and get a reaction out of Bing Bong
- New config keys `[Shootables] Tumbleweeds`, `BeeSwarms`, `BingBong`, `Balloons` and `[HitsToDefeat] BeeSwarms`; these do not unlock all-biome luggage, so a Zombies-only setup keeps Roots-only loot
- Shot bee swarms stop stinging at once and scatter within half a second instead of chasing you for several seconds
- Shotgun and ammo pile names and the refill prompt are translated into all of PEAK's languages; fix or add translations with `BepInEx/config/PeakShotgun.localization.csv`
- Sturdier after PEAK updates: a game change now turns off only the feature it breaks instead of stopping the mod from loading, and the log says which one
- Report bugs on GitHub Issues (link in the readme)

## 1.0.20

- Empty shotgun clicks play a dry-fire sound that everyone in the lobby hears

## 1.0.19

- Add `[Shootables]` / `[HitsToDefeat]` (default on / 1 hit) for Condors, Antlions, Frogs, Beehives, Ticks, Jellyfish, and Coconuts
- Condors drop anyone they are carrying and are removed
- Antlion pits stop attacking and hide; the sand funnel stays
- Frogs let go and drop cooked frog legs
- Beehives smash open (honeycomb drops; bees get angry)
- Ticks can be shot off a scout
- Shore jellyfish smash
- Whole coconuts fall off palm trees when shot (they do not split)
- Shotgun carry weight: `[Shotgun] CarryWeight` default 4 (10 on the stamina bar). 0 matches an empty backpack
- New keys default on in an existing `PeakShotgun.cfg`; that mix unlocks all-biome luggage unless you turn them off (Scoutmaster still does not count)

## 1.0.18

- Campfire ammo piles now has a proper in-game icon

## 1.0.17

- Add `[Shootables] Scoutmaster` (default on) and `[HitsToDefeat] Scoutmaster` (default 4): after enough hits he clears aggro and warps to the hidey spot
- Scoutmaster does not count toward unlocking all-biome luggage (Zombies-only Roots rule still holds with Scoutmaster on)

## 1.0.16

- All `[Shootables]` toggles default on (scouts, zombies, mandrake, beetles, spiders, spores, scorpions, dynamite)
- Replace `[Combat] FriendlyFire` with `[Shootables] Scouts` (off = pellets ignore other scouts; no knockback-only mode)
- Host syncs shootables as one bitmask only (no separate friendly-fire room property)
- With defaults on, luggage rare rolls unlock in every biome (Roots-only luggage only when Zombies is the sole enabled shootable)
- Note: an existing `PeakShotgun.cfg` keeps prior `[Shootables]` values until you change them; new keys (e.g. `Scouts`) use the new defaults
- Add `[HitsToDefeat]` (host-synced): zombies default 2, other defeatable targets default 1; scouts stay Injury every hit
- Shooting dynamite explodes it immediately (no fuse light)

## 1.0.15

- Improve the held shotgun pose: gun sits further forward and lower so the right arm is less cramped
- Right hand tips 45° down onto the pistol grip (`RightWristDownDegrees`); left hand sits on the pump (`LeftHandIn` / `LeftHandUp`, default up `0.04`)
- Hold pose config renames: `HoldForward` (was `Reach`), plus live `GunHeight` / `Side` / `HoldForward` updates while holding
- Seat the held gun a fixed reach from the right shoulder every frame so it no longer glues to the chest

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

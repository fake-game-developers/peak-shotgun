# Agent notes

Map of the shotgun mod for agents and developers. The player-facing setup is in [README.md](README.md).

The disk folder is still `Peak_AKGun`. The solution is `Peak.Shotgun.slnx`. The C# namespace is `Peak.Shotgun`. The assembly, BepInEx plugin, and Thunderstore name are `PeakShotgun`.

The shotgun is automatic: holding fire keeps shooting at the fire rate, and the action cycles itself. It is not a pump-action gun. Do not add a pump sound, pump animation, racking between shots, or the word "pump" in code, config or text. The wooden grip under the barrel, where the left hand sits, is the forend.

The gun is not a new model. `Plugin.CreateFromBlowgun` waits for PEAK's item catalog, clones the blowgun, strips its `ItemAction`s, and registers that clone with PEAKLib. The world mesh stays the blowgun. Do not replace the blowgun item itself.

## Where things live

| Path | What it is |
|---|---|
| `src/PeakShotgun/` | All gameplay code |
| `src/PeakShotgun/Targets/` | Shootable rules and one `ShotTarget` per world target (see [Adding a target](#adding-a-target)) |
| `src-mesh/` | Shotgun FBX + textures; `build_mesh.py` → `src/PeakShotgun/models/` (see `models/README.md`) |
| `src/PeakShotgun/models/` | Only `shotgun.obj` + `shotgun_albedo_1k.png` are embedded in the DLL |
| `src/PeakShotgun/icons/ShotgunIcon.png` | Shotgun hotbar icon, embedded in the DLL |
| `src/PeakShotgun/icons/AmmoIcon.png` | Ammo pile in-game icon, embedded in the DLL |
| `src/PeakShotgun/icons/Melon.png` | Unused earlier test icon. Not embedded |
| `src/PeakShotgun/sounds/shotgun_fire_01.wav` | Shot fire sound. Embedded |
| `src/PeakShotgun/sounds/shotgun_empty_01.wav` | Empty-magazine click. Embedded |
| `src/PeakShotgun/localization/strings.csv` | Item names and prompts in every PEAK language. Embedded (see [Localization](#localization)) |
| `src/PeakShotgun/thunderstore.toml` | Thunderstore listing. `icon.png` at the repo root is the store icon, not the in-game one |
| `artifacts/bin/Peak.Shotgun/release/PeakShotgun.dll` | Release build |

Embedded resource names are set in `Peak.Shotgun.csproj` and must match the `GetManifestResourceStream` strings in `Plugin.cs`:

- `Peak.Shotgun.icons.ShotgunIcon.png`
- `Peak.Shotgun.icons.AmmoIcon.png`
- `Peak.Shotgun.sounds.shotgun_fire_01.wav`
- `Peak.Shotgun.sounds.shotgun_empty_01.wav`
- `Peak.Shotgun.localization.strings.csv` (read in `ShotgunText.cs`)

New art goes in `icons/`. New audio goes in `sounds/`. Resource folders stay lowercase. Add an `EmbeddedResource` with an explicit `LogicalName` when the game should load the file.

## Code

`Plugin.Awake` binds config, applies every `[HarmonyPatch]` class in this assembly through `GameCompat.PatchAll` (one class at a time, see [Game updates](#game-updates)), and starts the blowgun wait plus the shore test spawn.

| Type | Role |
|---|---|
| `Plugin` | Builds the shotgun and ammo-pile prefabs, loads their icons and the shot sound, sets `item.totalUses` and `item.carryWeight` from config |
| `Action_Gun` | Primary fire. Shot request to the host, pellet cone, hit detection, shot sound, empty-click, and the blast RPC |
| `Action_Ammo` | Host-only `TrySpendOne` lowers `ItemUses` and broadcasts it with `ApplyUsesRPC` (also the fuel bar). `TryRefillToCapacity` sets remaining to `ShotCount` (never above). Clients accept `ApplyUsesRPC` only from the host, and write the count into the inventory slot whose instance-data guid matches the gun (never the currently selected slot). At zero the gun stops firing. The empty gun stays in hand |
| `AmmoPileInteract` / `AmmoPilePatches` | Ground ammo pile: interact while holding an underfilled shotgun → host refill (pile stays). No pickup, no luggage loot |
| `CampfireAmmoSpawner` | Host-only: when `[AmmoPile] SpawnAtCampfire` is on, places piles around each segment campfire on first `Campfire.OnEnable` |
| `AmmoPileModelSwap` | Embedded `ammo_pile.obj` + albedo on the ammo-pile prefab |
| `HostConfigSync` | Publishes the host's `Shots`, `[Shootables]`, and `[HitsToDefeat]` as Photon room properties; every client reads those instead of its own config |
| `ShootableRules` | One `ShootableRule` per `[Shootables]` / `[HitsToDefeat]` key: config binding, the room bitmask bit (`ShootTargets`) and the packed hits slot. `Rule.Enabled` / `Rule.HitsToDefeat` already read the host's values |
| `ShotgunCombat` | Shared hit flow: classify a pellet (tick, then character, then world targets), send world hits to the host, count hits toward defeat, broadcast defeats that have no vanilla RPC |
| `ShotTarget` / `ShotTargetRegistry` | One class per world target in `Targets/` (classify, host re-check, view, defeat, client defeat). The registry holds them in classify order and runs each behind a try/catch |
| `GameCompat` | Game-update protection: per-class Harmony patching, a startup check of names used by string, and `IsGameMismatch` |
| `ShotgunText` | Localization: adds `localization/strings.csv` (and an optional player override file) to PEAK's `LocalizedText` table |
| `ShotgunAmmoUI` | Appends the remaining count to the hotbar name (`Item.GetItemName`) and the shoot prompt (`GUIManager.GetMainInteractPrompt`), then refreshes both after a shot |
| `ShotgunVFX` | A short orange point light, plus a small copy of a smoke effect already in PEAK |
| `GunPatch` | Adds `GunCharacterLaunch` to every character in `Character.Awake` |
| `GunCharacterLaunch` | Knockback and zombie knockdown. Owner-only physics. The owner mirrors the zombie's hit count and knockdown to everyone (`RPC_ZombieShotState`, replayed to late joiners), which also sets `Silenced` |
| `ZombieSilencePatch` | Stops a knocked-down zombie from playing its own sounds |
| `ItemDatabasePatch` | Calls `CreateFromBlowgun` when `ItemDatabase.OnLoaded` finishes |
| `RootsLuggagePatch` | Always marks 2 random Roots suitcases per run and forces a shotgun into those (only when `Item.IsValidToSpawn`, so custom-run exclusions apply). Luggage is Roots-only only when Zombies is the sole enabled shootable (unless `CanSpawnOnAnyBiome` forces every biome); any other `[Shootables]` target with `UnlocksAllBiomeLoot` (all but Scoutmaster, Tumbleweeds, BeeSwarms, BingBong and Balloons) unlocks rare rolls in all biomes' luggage. At the per-biome cap the shotgun becomes an invalid roll and extra shotguns are re-rolled as other loot (same pool, `canRepeat` and fallback item as the original roll). Picks and counts live in `ShotgunLootLedger` |
| `ShotgunLootLedger` | Per-run loot bookkeeping keyed by `RunManager.RunId` and a stable suitcase key (scene + scene view id): the marked Roots suitcases, shotguns per suitcase per biome, and each seat pose. The host publishes it as room property `PeakShotgun_Loot` and saves it to `BepInEx/config/PeakShotgun.loot.json` (last 20 runs), so quicksave reloads and host migration keep it. A re-rolled suitcase replaces its own count; a gun restored from save history counts too |
| `ShotgunLuggageRest` | Luggage-only visual pose and freeze, on the item root. The host arms it in `Luggage.OffsetSpawn` (for saved loot, from the pose in `ShotgunLootLedger` in `Spawner.InitializePhysics`), sends it to the others, and replays it to late joiners |
| `ShoreTestSpawns` | Only with `[Debug] EnableDebugMode` (default off): the host spawns a shotgun and luggage at the Airport, and three zombies and one shotgun near the local player once per Shore load |
| `StartingShotgunGiver` | Only with `[Shotgun] GiveShotgunOnSpawn` (default off): host calls `CharacterItems.SpawnItemInHand` once per player per run after the climb starts (never Airport), after beach wake / grounded (skips if they already hold a shotgun or the run disables the item) |

### Shot

`Action_Gun.RunAction` sends `RPC_RequestShot` with a shot id to the host and waits; only one request is outstanding at a time, and an unanswered one is re-sent with the same id every 1.5 s while fire is held. The host checks that the sender holds the gun, the fire-rate cooldown (by send time, with jitter slack) and the magazine, spends one use, and remembers its answer per sender: a retry of the same id gets the same answer without spending again, older ids are ignored. Accepted shots are announced to everyone (`RPC_ShotResult`, `RpcTarget.All`) so every client knows which shooter + shot id the host paid for; rejections go only to the shooter. Only on acceptance does `Action_Gun.Fire` run on the shooter: recoil, pellets, hit RPCs and the blast FX. A rejected shot does nothing.

Every hit RPC carries the shot id, and the receiver checks it with `Action_Gun.TryConsumeShot` / `TryConsumeOrDeferHit` (accepted by the host, from that shooter, at most once per target, valid 10 s): `RPC_GunImpact` on the hit scout, `GunCharacterLaunch.RPC_ShotgunBlast` on the zombie's owner, and `RPC_HostShootTarget` on the host. Scout/zombie hit RPCs come from the shooter while `RPC_ShotResult` comes from the host, so they can arrive out of order; early hits are queued until that shot is authorized, then applied once. Spiders, beetles, scorpions, frogs, ticks, beehives, jellyfish, coconuts, mandrakes, dynamite, spores, condors and antlions are applied only by the host (`ShotgunCombat.HostApply` → that kind's `ShotTarget`), after re-checking the room's `[Shootables]` and that the view id really belongs to that kind of target. Mob state is owner-authoritative, so the host asks a mob's owner to set it to Dead. Condors, antlions, frogs and jellyfish have no mob-state RPC: the host broadcasts the defeat (`RPC_ApplyWorldDefeat` plus room property `PeakShotgun_WorldDown`) so current clients, late joiners, and a new host see the same outcome. Ticks are classified before the scout they sit on, so a pellet on the bug does not Injury the carrier. Jellyfish share one Photon view; the hit carries the child's sibling index so only that jelly is removed. `Fire` raycasts `pelletCount` pellets (default 8) inside `spread`. One character is damaged once per shot. Scouts get the afflictions on `Action_Gun` (Injury). Zombies do not: Injury does not affect them, so zombie hits go to `GunCharacterLaunch` instead.

`RPC_ShotgunBlastFX` runs on every client. It plays `shotSFX` at the muzzle, flashes `ShotgunVFX` and shakes the camera. The shot sound must stay in this RPC: played from `Fire` only the shooter hears it. It does not spawn the blowgun dart puff. `ShotgunVFX.Play` puts the flash and smoke at that client's own `ShotgunMuzzle` and points them along it. `ShotgunMuzzle` sits at the centre of the barrel-end mesh slice (`Plugin.UpdateMeshAnchors`) with its forward along the barrel. Pellets start there but fly along the shooter's camera forward.

The shot sound is a runtime `SFX_Instance` built from `sounds/shotgun_fire_01.wav`. If that wav fails to load, the gun falls back to the blowgun's `shotSFX`. An empty magazine plays `emptyClickSFX` from `sounds/shotgun_empty_01.wav` at the muzzle on the fire-rate cooldown, with no blast FX. The click is sent to everyone (`RPC_EmptyClick`), same as the shot sound. Vanilla `Item.CanUsePrimary` is false at 0 uses, so `ShotgunEmptyUsePatch` keeps the shotgun usable when empty. `ItemUseFeedback.sfxUsed` on the clone is cleared so the blowgun's use sound does not also play.

Ammo count is the BepInEx key `Shots` in section `Shotgun` (`BepInEx/config/PeakShotgun.cfg`). Default 5. Values below 1 become 1. `item.totalUses` is that number, not `-1`.

Recoil is `[Shotgun] EnableRecoil` (default true) and `[Shotgun] Recoil` (strength, default 5 m/s, `AcceptableValueRange` 0-10 so BepInEx clamps larger values to 10), both read live. `Fire` calls `Character.AddForce` on the shooter opposite the camera forward with `Recoil / fixedDeltaTime`: bodypart forces are applied once per physics step, so every bone gains exactly `Recoil` m/s. The shooter owns their character, so the push reaches other clients through normal character sync.

### Adding a target

Every world target is one `ShotTarget` subclass in `Targets/`. Use `ShotTarget<T>` when one component type identifies it: it classifies with `GetComponentInParent<T>`, re-finds it on the host under the claimed view, and casts for you. Override `Classify` / `FindFromView` when the type alone is not enough (coconuts, spore bombs), `ViewOf` when the Photon view is not on a parent, `ExtraOf` when several targets share one view (jellyfish), and `BroadcastsDefeat` + `ClientDefeat` when no vanilla RPC carries the defeat to other clients. A lasting defeat uses `BroadcastsDefeat` (replayed to late joiners); short-lived feedback such as a push or a voice line calls `ShotgunCombat.BroadcastEffect` from `Defeat`, which runs `Effect` on every client through `RPC_ShotEffect` (tumbleweeds, Bing Bong). `ShotHit.DirectionTo` gives the direction from the shooter's muzzle. Physics goes only on the object's owner. If the target's collider is not on Character or Default, set `ExtraLayerMask` (the bee swarm is on GhostCharacter): pellets then also hit that layer, and colliders on it are offered only to targets that asked for it.

A new rule with no hit count (every hit acts, like a push) uses hits slot -1. Rules added after a release should set `unlocksAllBiomeLoot: false` unless they really should open luggage outside Roots: an existing config gets the new key at its default (on), which would otherwise silently change a Zombies-only setup.

1. Append a value to `ShotTargetKind` (wire format: never renumber).
2. Reuse a `ShootableRules` entry, or add one: append a `ShootTargets` bit and use the next free hits slot. `ShootableRules` throws at startup on a duplicate bit or slot.
3. Write the class and add it to `ShotTargetRegistry.Initialize`. That order is the classify order within a `ShotStage`.
4. Add the config keys to the README table.

### Game updates

`GameCompat.PatchAll` applies each `[HarmonyPatch]` class on its own and logs one summary line (`Harmony patches: N applied, …`). A patch whose game method is gone is logged and skipped, and every other patch still applies; plain `Harmony.PatchAll` stops at the first failure. Give a patch that targets something by string a `Prepare` that returns false when the target is missing (`ZombieSilencePatch`), so it is reported as skipped instead of failed.

`GameCompat.CheckNamedMembers` warns at startup when a type or member the mod reaches by string (reflection, RPC names) is gone. Add new string lookups there.

`ShotTargetRegistry` creates each target in its own lambda and calls it through a try/catch. A `MissingMemberException`, `TypeLoadException` or `BadImageFormatException` (`GameCompat.IsGameMismatch`) turns that one target off for the session with one warning; other exceptions are logged and the target stays on. Keep each target's game calls inside its own class so a broken one cannot take the others down.

### Localization

PEAK reads text from `LocalizedText.mainTable`: item names under `NAME_<ITEMNAME>` (`NAME_SHOTGUN`, `NAME_AMMO PILE`), prompts under the prompt string (`SHOOT` is vanilla and already translated; `PEAKSHOTGUN_REFILL` is ours). Names are upper case and prompts lower case in every language, like vanilla. PEAKLib only adds an English row for a missing key, so `ShotgunText.Inject` adds our rows before each `ItemContent.Register`, and `LocalizedTextReloadPatch` adds them again whenever PEAK rebuilds the table.

`localization/strings.csv` has a `Key` column and one column per `LocalizedText.Language` name, in any order. An empty cell falls back to English. Players can put a `PeakShotgun.localization.csv` in the same format in `BepInEx/config/`; its non-empty cells replace the embedded ones. New player-facing text goes in the CSV and is read with `LocalizedText.GetText`, never as a literal.

### Zombies

`GunCharacterLaunch` looks up `MushroomZombie` by name because the mod does not reference that type directly.

A hit calls `Character.Fall` and then `Character.AddForce` once per physics step (`FixedUpdate`) for `ShoveSeconds` (0.3s) at `ShoveAcceleration` (36). Bodypart forces accumulate until the next physics step, so adding it per rendered frame would scale the shove with frame rate. That is one acceleration on the body. Do not switch this to `AddForceAtPosition`. PEAK applies that impulse to every ragdoll bone, which launches the zombie off the map.

Zombie knockdown needs `[HitsToDefeat] Zombies` hits (default 2, host-synced). Hits before that add Drowsy (0.5) and do not knock them out. The defeating hit calls `PassOutInstantly` and `HoldDown`. `LateUpdate` keeps them down (`passedOut`, `fullyPassedOut`, Drowsy at 1, `fallSeconds` at least 5) so lunge recovery cannot stand them back up. The game still does not mark them `Dead`. PEAK's own downed timer kills them later. Other defeatable world targets use the same config section (default 1 each); fellow scouts are not listed and still take Injury every hit. Scoutmaster Myres (`character.isScoutmaster`) needs `[HitsToDefeat] Scoutmaster` hits (default 4): the host clears his target, sets a chill timer, and calls `TeleportFarAway` (warp to `ScoutmasterHideySpot`). He is ignored for the Zombies-only Roots luggage rule. Condors, antlions, frogs, beehives, ticks, jellyfish, and coconuts (default 1 hit each) do count toward unlocking all-biome luggage. A defeated condor drops anyone it is carrying and is destroyed; an antlion pit is disabled (head hidden, AI off) without removing the sand funnel; a frog lets go and is replaced with cooked frog legs (stove `CookingBehavior_ReplaceItem`; Dead mob state does not stop `FrogTongue`). Condors, antlions, frogs and jellyfish have no mob-state RPC: the host broadcasts the defeat (`RPC_ApplyWorldDefeat` plus room property `PeakShotgun_WorldDown`) so current clients, late joiners, and a new host see the same outcome. A beehive uses the vanilla `Breakable` smash (honeycomb + angry swarm). A tick is removed with `Bugfix.RPCA_Remove` (same as picking it off). A jellyfish is destroyed locally on every client so the shared Shore relay stays. A hanging coconut is unfrozen (`SetKinematicNetworked(false)`, same as throwing an item at it) so it falls whole and does not split. A tumbleweed (`TumbleWeed`, synced by `PhotonRigidbodyView`) is pushed away from the shooter by its owner on every hit; it keeps chasing scouts afterwards. A bee swarm is scattered with vanilla `BeeSwarm.DisperseRPC` (`[HitsToDefeat] BeeSwarms`, default 1), which removes the stinger field at once but fades for a second, keeps pulling the swarm toward its target, and is destroyed by the owner about 4 s later. So the hit also broadcasts an effect: every client scatters the bee particles and fades them within 0.5 s and stops the swarm's audio, and the owner makes the swarm kinematic so it stops chasing. Bing Bong reacts on every hit: the host picks one of his `Action_AskBingBong` lines, every client plays the squish, squeak, voice and subtitle (vanilla `Ask` when someone holds him), and on the ground his owner knocks him into the air. A balloon tied to a scout (`TiedBalloon`) is popped by its owner (the scout who tied it) with vanilla `Pop`; its `OnDestroy` plays the pop and removes that lift on every client. A balloon or balloon bunch item plays its `popParticle` everywhere, then its owner removes it: in a hand with vanilla `Item.Consume(-1)` (empties the slot, no consume event), on the ground with `PhotonNetwork.Destroy`. `[Shootables] Balloons` covers both; every hit pops. Tumbleweeds, bee swarms, Bing Bong and balloons do not unlock all-biome luggage.

`KnockDown` silences the zombie and sends the knockdown with `RPC_ZombieShotState`. That sets `Silenced` on every client, stops `AudioSource`s on the zombie, and mutes them. `ZombieSilencePatch` is a Harmony prefix on `MushroomZombie.RPC_PlaySFX`. If `Silenced` is set, the prefix skips the original method, which blocks later grunts, the knockout bark, and bite sounds. `Prepare` skips the patch when that method is missing, so a game update does not fail plugin load.

### Held pose (do not freeze the rigidbody)

PEAK does not parent a held item to the hand. `CharacterItems.HoldItem` adds force and torque to the item's dynamic rigidbody every `FixedUpdate` to pull it to the hold pose and rotate it to the look direction. `AttachItem` joins the scout's `Hand_R` and `Hand_L` bones to that rigidbody. So a held item must stay non-kinematic with `constraints = None` (`ShotgunPhysics.Apply`). A kinematic or `FreezeAll` gun stays fixed in the world and pins the scout in mid-air. Only the luggage, ground and backpack states may freeze it.

A ground gun is settled only by its owner: once it rests on the terrain the owner freezes it (lifting it out of the terrain if needed) and sends `Item.SetKinematicAndResetSyncData` to the others, and sends `Item.SetKinematicRPC(false)` when it starts moving again. Other clients never freeze or unfreeze it themselves, because `ItemPhysicsSyncer` ignores incoming movement while the body is kinematic.

PEAK holds the item root at `Item.defaultPos` in the scout's look space (`CharacterRagdoll.SaveAdditionalTransformPositions`: `animationLookTransform.TransformPoint(defaultPos)`, +X right, +Y up, +Z forward; vanilla items use Z = 1). The cloned blowgun has `(0, 0.33, 1)`, centred at the mouth. Moving the gun on screen goes through `defaultPos` only: offsetting the mesh or hand anchors away from the item root puts the hands out of arm's reach, the hand joints drag the gun back to the centre, and the arms twist.

The item's `Hand_R` and `Hand_L` children set both position and rotation of the scout's hands (`AttachItem`, and the IK targets in `CharacterAnimations.ConfigureIK`). `PlaceHandAnchors` puts `Hand_R` on the rear grip at the item root and `Hand_L` under the wooden forend, turns them with `RightGripRotation` and `LeftForendRotation`, and writes `item.defaultPos`. `Plugin.UpdateMeshAnchors` finds the wrist and forend from mesh cross-sections (fractions from the muzzle), because the mesh is not axis-aligned and whole-mesh bounds put the hands beside and below the gun. Hand bone axes (from vanilla Torch / Honeycomb / RopeShooter anchors): forward is the thumb side of the fist, up is the fingers, and on the right hand right is the palm normal. `[HoldPose]` `Side`, `GunHeight` and `HoldForward` are `defaultPos` x/y/z, in world metres from the head bone (the logged `target` sits about 1.6 m ahead of the head for `HoldForward` 1.6; the head bone's own 0.33 scale does not apply). Vanilla items also target about 1 m out, past arm's reach, so the arms end up stretched toward the target and the gun settles where they reach. `ToeInDegrees` and `BarrelPitchDegrees` turn the mesh and both hands. `LeftHandIn` / `LeftHandUp` pull the left hand onto the forend; `RightWristDownDegrees` tips the right fist 45° down about item +X onto the pistol grip. Do not add left-wrist rolls — they park the left elbow in the chest. `GripReach` keeps each fist on its bar. On every equip the log prints one `Held shotgun` line with the item, its hold `target`, both anchors, both real hand bones, the right shoulder and the head bone: relative to the camera for your own scout (`camera space`), and relative to that scout's head in its look direction for other players (`remote <name>, look space`). A healthy hold has the item and `anchorR` well in front of `shoulderR` (larger z); z near or behind the shoulder means the gun is stuck in the body. The values are read live: `Plugin.Update` re-reads the `.cfg` when it changes on disk, bumps `Plugin.PoseVersion`, and each `ShotgunVisualOrient` re-runs `PlaceHandAnchors`. Sections are renamed when a default changes because BepInEx keeps saved values.

A hand anchor is where the hand BONE goes, and that bone sits at the wrist end of the hand. The fist closes about 0.14 m along the bone's up (fingers) axis: vanilla anchors around a centred shaft measure Torch R 0.134, Torch L 0.160, RopeShooter R 0.141, with almost no offset along the palm normal. So each hand bone is placed `GripReach` (0.14) back along its fingers axis from the bar it holds. Putting the bone on the bar centre pushes the hand through the gun.

The held pose is not networked: `ItemPhysicsSyncer` only syncs `Ground` items, so every client simulates every holder's gun (hold force, hand joints, IK) on its own, and one client can show a good hold while another shows the same scout with the gun in its body. While equipping, `CharacterItems.Equip` places the item at `GetItemHoldPos(item, pushOffTerrain: true)`, then `AttachItem` teleports both hand bones onto the anchors and joins them there. The unpushed shotgun target is 1.3-1.5 m out, far past arm's reach, so the arms snap back and can fling the gun through the torso, which then traps the arms (gun inside the chest, muzzle out the front, arms crossed). Vanilla's push (a hip ray that shortens the offset to `max(hit, 0.2) - 0.4` on any Terrain/Map hit) pulls it toward the hip and traps it too. `ShotgunHoldPosPatch` replaces every `GetItemHoldPos` result for the shotgun with a fixed seat `HoldReach` (0.82 m) from the right shoulder toward the look target so `HoldItem` keeps the grip forward (a short clamp glued it to the chest and sky'd the tip-down right elbow). Held colliders are triggers, so terrain needs no push.

In a backpack, `Item.PutInBackpackRPC` snaps the item root to the slot transform and `SetState(InBackpack)` halves its scale (`forceScale`). `ShotgunVisualOrient` then stands the mesh upright on the slot, muzzle up (`BackpackRotation`), at `BackpackScale` (1.4×).

### Gun dissolving near the camera

W/Character (scout hands), W/Peak_Dither, W/Peak_Glass and W/Peak_Mirage read the screen position and dither away near the camera. Ordinary items (flare, dynamite, guidebook) use W/Peak_Standard, which does not. `ShotgunModelSwap.FindItemMaterial` copies the first catalog material whose shader is exactly `W/Peak_Standard`; if none exists it warns and falls back to the blowgun shader. `PlayerPos` only feeds foliage and water shaders; do not use it for items.

### Effects that render pink

Do not add a `ParticleSystem` with `Shader.Find` or a copied material. PEAK does not draw the built-in particle shaders, so those particles come out magenta. Do not instantiate the blowgun `dartVFX` either. Smoke has to be a copy of a smoke object already in the game (`ShotgunVFX.FindSmoke`), played small at the muzzle and destroyed after about a second. The light in `ShotgunVFX` stays either way.

## Build

```bash
dotnet build Peak.Shotgun.slnx -c Release
```

`Directory.Build.props` finds the PEAK install and references `PEAK_Data/Managed`. `PackTS` (`./build.sh`) writes `artifacts/thunderstore/FakeGameDevelopers-PeakShotgun-<version>.zip`. Publishing is described in the player README. Do not bump `<Version>` unless a release was asked for.
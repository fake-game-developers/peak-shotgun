# Agent notes

Map of the shotgun mod for agents and developers. The player-facing setup is in [README.md](README.md).

The disk folder is still `Peak_AKGun`. The solution is `Peak.Shotgun.slnx`. The C# namespace is `Peak.Shotgun`. The assembly, BepInEx plugin, and Thunderstore name are `PeakShotgun`.

The gun is not a new model. `Plugin.CreateFromBlowgun` waits for PEAK's item catalog, clones the blowgun, strips its `ItemAction`s, and registers that clone with PEAKLib. The world mesh stays the blowgun. Do not replace the blowgun item itself.

## Where things live

| Path | What it is |
|---|---|
| `src/PeakShotgun/` | All gameplay code |
| `src-mesh/` | Shotgun FBX + textures; `build_mesh.py` → `src/PeakShotgun/models/` (see `models/README.md`) |
| `src/PeakShotgun/models/` | Only `shotgun.obj` + `shotgun_albedo_1k.png` are embedded in the DLL |
| `src/PeakShotgun/icons/ShotgunIcon.png` | Hotbar icon, embedded in the DLL |
| `src/PeakShotgun/icons/Melon.png` | Unused earlier test icon. Not embedded |
| `src/PeakShotgun/sounds/shotgun_fire_01.wav` | Shot fire sound. Embedded |
| `src/PeakShotgun/thunderstore.toml` | Thunderstore listing. `icon.png` at the repo root is the store icon, not the in-game one |
| `artifacts/bin/Peak.Shotgun/release/PeakShotgun.dll` | Release build |

Embedded resource names are set in `Peak.Shotgun.csproj` and must match the `GetManifestResourceStream` strings in `Plugin.cs`:

- `Peak.Shotgun.icons.ShotgunIcon.png`
- `Peak.Shotgun.sounds.shotgun_fire_01.wav`

New art goes in `icons/`. New audio goes in `sounds/`. Resource folders stay lowercase. Add an `EmbeddedResource` with an explicit `LogicalName` when the game should load the file.

## Code

`Plugin.Awake` binds config, runs `Harmony.PatchAll`, and starts the blowgun wait plus the shore test spawn. Every `[HarmonyPatch]` class in this assembly is applied from there.

| Type | Role |
|---|---|
| `Plugin` | Builds the shotgun prefab, loads the icon and shot sound, sets `item.totalUses` from config |
| `Action_Gun` | Primary fire. Shot request to the host, pellet cone, hit detection, shot sound, and the blast RPC |
| `Action_Ammo` | Host-only `TrySpendOne` lowers `ItemUses` and broadcasts it with `ApplyUsesRPC` (also the fuel bar). Clients accept `ApplyUsesRPC` only from the host, and write the count into the inventory slot whose instance-data guid matches the gun (never the currently selected slot). At zero the gun stops firing. The empty gun stays in hand |
| `HostConfigSync` | Publishes the host's `Shots`, `FriendlyFire` and `[Shootables]` as Photon room properties; every client reads those instead of its own config |
| `ShotgunAmmoUI` | Appends the remaining count to the hotbar name (`Item.GetItemName`) and the shoot prompt (`GUIManager.GetMainInteractPrompt`), then refreshes both after a shot |
| `ShotgunVFX` | A short orange point light, plus a small copy of a smoke effect already in PEAK |
| `GunPatch` | Adds `GunCharacterLaunch` to every character in `Character.Awake` |
| `GunCharacterLaunch` | Knockback and zombie knockdown. Owner-only physics. The owner mirrors the zombie's hit count and knockdown to everyone (`RPC_ZombieShotState`, replayed to late joiners), which also sets `Silenced` |
| `ZombieSilencePatch` | Stops a knocked-down zombie from playing its own sounds |
| `ItemDatabasePatch` | Calls `CreateFromBlowgun` when `ItemDatabase.OnLoaded` finishes |
| `RootsLuggagePatch` | Always marks 2 random Roots suitcases per run and forces a shotgun into those (only when `Item.IsValidToSpawn`, so custom-run exclusions apply). `CanSpawnOnAnyBiome` (default false) also allows random rolls in other biomes' luggage. At the per-biome cap the shotgun becomes an invalid roll and extra shotguns are re-rolled as other loot (same pool, `canRepeat` and fallback item as the original roll). Picks and counts live in `ShotgunLootLedger` |
| `ShotgunLootLedger` | Per-run loot bookkeeping keyed by `RunManager.RunId` and a stable suitcase key (scene + scene view id): the marked Roots suitcases, shotguns per suitcase per biome, and each seat pose. The host publishes it as room property `PeakShotgun_Loot` and saves it to `BepInEx/config/PeakShotgun.loot.json` (last 20 runs), so quicksave reloads and host migration keep it. A re-rolled suitcase replaces its own count; a gun restored from save history counts too |
| `ShotgunLuggageRest` | Luggage-only visual pose and freeze, on the item root. The host arms it in `Luggage.OffsetSpawn` (for saved loot, from the pose in `ShotgunLootLedger` in `Spawner.InitializePhysics`), sends it to the others, and replays it to late joiners |
| `ShoreTestSpawns` | Only with `[Debug] EnableDebugMode` (default off): the host spawns a shotgun and luggage at the Airport, and three zombies and one shotgun near the local player once per Shore load |

### Shot

`Action_Gun.RunAction` sends `RPC_RequestShot` with a shot id to the host and waits; only one request is outstanding at a time, and an unanswered one is re-sent with the same id every 1.5 s while fire is held. The host checks that the sender holds the gun, the fire-rate cooldown (by send time, with jitter slack) and the magazine, spends one use, and remembers its answer per sender: a retry of the same id gets the same answer without spending again, older ids are ignored. Accepted shots are announced to everyone (`RPC_ShotResult`, `RpcTarget.All`) so every client knows which shooter + shot id the host paid for; rejections go only to the shooter. Only on acceptance does `Action_Gun.Fire` run on the shooter: recoil, pellets, hit RPCs and the blast FX. A rejected shot does nothing.

Every hit RPC carries the shot id, and the receiver checks it with `Action_Gun.TryConsumeShot` / `TryConsumeOrDeferHit` (accepted by the host, from that shooter, at most once per target, valid 10 s): `RPC_GunImpact` on the hit scout, `GunCharacterLaunch.RPC_ShotgunBlast` on the zombie's owner, and `RPC_HostShootTarget` on the host. Scout/zombie hit RPCs come from the shooter while `RPC_ShotResult` comes from the host, so they can arrive out of order; early hits are queued until that shot is authorized, then applied once. Spiders, beetles, scorpions, mandrakes, dynamite and spores are applied only by the host (`ShotgunCombat.HostApply`), after re-checking the room's `[Shootables]` and that the view id really belongs to that kind of target. Mob state is owner-authoritative, so the host asks a mob's owner to set it to Dead. `Fire` raycasts `pelletCount` pellets (default 8) inside `spread`. One character is damaged once per shot. Scouts get the afflictions on `Action_Gun` (Injury). Zombies do not: Injury does not affect them, so zombie hits go to `GunCharacterLaunch` instead.

`RPC_ShotgunBlastFX` runs on every client. It plays `shotSFX` at the muzzle, flashes `ShotgunVFX` and shakes the camera. The shot sound must stay in this RPC: played from `Fire` only the shooter hears it. It does not spawn the blowgun dart puff. `ShotgunVFX.Play` puts the flash and smoke at that client's own `ShotgunMuzzle` and points them along it. `ShotgunMuzzle` sits at the centre of the barrel-end mesh slice (`Plugin.UpdateMeshAnchors`) with its forward along the barrel. Pellets start there but fly along the shooter's camera forward.

The shot sound is a runtime `SFX_Instance` built from `sounds/shotgun_fire_01.wav`. If that wav fails to load, the gun falls back to the blowgun's `shotSFX`. `ItemUseFeedback.sfxUsed` on the clone is cleared so the blowgun's use sound does not also play.

Ammo count is the BepInEx key `Shots` in section `Shotgun` (`BepInEx/config/PeakShotgun.cfg`). Default 5. Values below 1 become 1. `item.totalUses` is that number, not `-1`.

Recoil is `[Shotgun] EnableRecoil` (default true) and `[Shotgun] Recoil` (strength, default 5 m/s, `AcceptableValueRange` 0-10 so BepInEx clamps larger values to 10), both read live. `Fire` calls `Character.AddForce` on the shooter opposite the camera forward with `Recoil / fixedDeltaTime`: bodypart forces are applied once per physics step, so every bone gains exactly `Recoil` m/s. The shooter owns their character, so the push reaches other clients through normal character sync.

### Zombies

`GunCharacterLaunch` looks up `MushroomZombie` by name because the mod does not reference that type directly.

A hit calls `Character.Fall` and then `Character.AddForce` once per physics step (`FixedUpdate`) for `ShoveSeconds` (0.3s) at `ShoveAcceleration` (36). Bodypart forces accumulate until the next physics step, so adding it per rendered frame would scale the shove with frame rate. That is one acceleration on the body. Do not switch this to `AddForceAtPosition`. PEAK applies that impulse to every ragdoll bone, which launches the zombie off the map.

The first zombie hit adds Drowsy (0.5) and does not knock them out. The second hit calls `PassOutInstantly` and `HoldDown`. `LateUpdate` keeps them down (`passedOut`, `fullyPassedOut`, Drowsy at 1, `fallSeconds` at least 5) so lunge recovery cannot stand them back up. The game still does not mark them `Dead`. PEAK's own downed timer kills them later.

`KnockDown` silences the zombie and sends the knockdown with `RPC_ZombieShotState`. That sets `Silenced` on every client, stops `AudioSource`s on the zombie, and mutes them. `ZombieSilencePatch` is a Harmony prefix on `MushroomZombie.RPC_PlaySFX`. If `Silenced` is set, the prefix skips the original method, which blocks later grunts, the knockout bark, and bite sounds. `Prepare` skips the patch when that method is missing, so a game update does not fail plugin load.

### Held pose (do not freeze the rigidbody)

PEAK does not parent a held item to the hand. `CharacterItems.HoldItem` adds force and torque to the item's dynamic rigidbody every `FixedUpdate` to pull it to the hold pose and rotate it to the look direction. `AttachItem` joins the scout's `Hand_R` and `Hand_L` bones to that rigidbody. So a held item must stay non-kinematic with `constraints = None` (`ShotgunPhysics.Apply`). A kinematic or `FreezeAll` gun stays fixed in the world and pins the scout in mid-air. Only the luggage, ground and backpack states may freeze it.

A ground gun is settled only by its owner: once it rests on the terrain the owner freezes it (lifting it out of the terrain if needed) and sends `Item.SetKinematicAndResetSyncData` to the others, and sends `Item.SetKinematicRPC(false)` when it starts moving again. Other clients never freeze or unfreeze it themselves, because `ItemPhysicsSyncer` ignores incoming movement while the body is kinematic.

PEAK holds the item root at `Item.defaultPos` in the scout's look space (`CharacterRagdoll.SaveAdditionalTransformPositions`: `animationLookTransform.TransformPoint(defaultPos)`, +X right, +Y up, +Z forward; vanilla items use Z = 1). The cloned blowgun has `(0, 0.33, 1)`, centred at the mouth. Moving the gun on screen goes through `defaultPos` only: offsetting the mesh or hand anchors away from the item root puts the hands out of arm's reach, the hand joints drag the gun back to the centre, and the arms twist.

The item's `Hand_R` and `Hand_L` children set both position and rotation of the scout's hands (`AttachItem`, and the IK targets in `CharacterAnimations.ConfigureIK`). `PlaceHandAnchors` puts `Hand_R` on the stock wrist at the item root and `Hand_L` under the pump, turns them with `RightGripRotation` and `LeftPumpRotation`, and writes `item.defaultPos`. `Plugin.UpdateMeshAnchors` finds the wrist and pump from mesh cross-sections (fractions from the muzzle), because the mesh is not axis-aligned and whole-mesh bounds put the hands beside and below the gun. Hand bone axes (from vanilla Torch / Honeycomb / RopeShooter anchors): forward is the thumb side of the fist, up is the fingers, and on the right hand right is the palm normal. `[HoldPose]` `Right`, `Up` and `Forward` are `defaultPos` x/y/z, in world metres from the head bone (the logged `target` sits about 1.25 m ahead of the head for `Forward` 1.15; the head bone's own 0.33 scale does not apply). Vanilla items also target about 1 m out, past arm's reach, so the arms end up stretched toward the target and the gun settles where they reach. `ToeInDegrees` and `MuzzleUpDegrees` turn the mesh and both hands. `LeftHandLeft` slides the left hand sideways under the pump; `RightWristTiltDegrees` tips the right hand about item +X. On every equip the log prints one `Held shotgun` line with the item, its hold `target`, both anchors, both real hand bones, the right shoulder and the head bone: relative to the camera for your own scout (`camera space`), and relative to that scout's head in its look direction for other players (`remote <name>, look space`). A healthy hold has the item and `anchorR` well in front of `shoulderR` (larger z); z near or behind the shoulder means the gun is stuck in the body. The values are read live: `Plugin.Update` re-reads the `.cfg` when it changes on disk, bumps `Plugin.PoseVersion`, and each `ShotgunVisualOrient` re-runs `PlaceHandAnchors`. Sections are renamed when a default changes because BepInEx keeps saved values.

A hand anchor is where the hand BONE goes, and that bone sits at the wrist end of the hand. The fist closes about 0.14 m along the bone's up (fingers) axis: vanilla anchors around a centred shaft measure Torch R 0.134, Torch L 0.160, RopeShooter R 0.141, with almost no offset along the palm normal. So `Hand_R` is placed `GripReach` (0.14) back along its fingers axis from the stock wrist centre. Putting the bone on the grip centre pushes the hand through the gun.

The held pose is not networked: `ItemPhysicsSyncer` only syncs `Ground` items, so every client simulates every holder's gun (hold force, hand joints, IK) on its own, and one client can show a good hold while another shows the same scout with the gun in its body. While equipping, `CharacterItems.Equip` places the item at `GetItemHoldPos(item, pushOffTerrain: true)`, then `AttachItem` teleports both hand bones onto the anchors and joins them there. The unpushed shotgun target is 1.3-1.5 m out, far past arm's reach, so the arms snap back and can fling the gun through the torso, which then traps the arms (gun inside the chest, muzzle out the front, arms crossed). Vanilla's push (a hip ray that shortens the offset to `max(hit, 0.2) - 0.4` on any Terrain/Map hit) pulls it toward the hip and traps it too. `ShotgunHoldPosPatch` replaces the pushed point for the shotgun with a seat `EquipReach` (0.35 m) from the right shoulder toward the target, about where the grip settles in a good hold; `HoldItem` then pulls the arms straight. Held colliders are triggers, so terrain needs no push.

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
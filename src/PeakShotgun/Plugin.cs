using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Peak.Afflictions;
using PEAKLib.Core;
using PEAKLib.Items;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace Peak.Shotgun;

[BepInAutoPlugin]
[BepInDependency(ItemsPlugin.Id)]
[BepInDependency(CorePlugin.Id)]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    internal static Plugin Instance { get; private set; } = null!;

    internal static GameObject? ShotgunPrefab { get; private set; }

    private static ConfigEntry<int>? shots;

    /// <summary>Local <c>Shots</c> config (host publishes this; clients ignore it for ammo).</summary>
    internal static int LocalConfiguredShots => Mathf.Max(1, shots?.Value ?? 5);

    /// <summary>Magazine size in use: host-synced in multiplayer, otherwise local config.</summary>
    internal static int ShotCount => HostShotSync.ShotCount;

    internal static float ModelScale { get; private set; } = 0.015f;

    private static ConfigEntry<bool>? recoilEnabled;

    private static ConfigEntry<float>? recoil;

    /// <summary>m/s the shooter is pushed back along the aim per shot, 0-10. 0 when disabled. Live.</summary>
    internal static float Recoil => (recoilEnabled?.Value ?? true) ? recoil?.Value ?? 5f : 0f;

    private static ConfigEntry<bool>? debugMode;

    /// <summary>Host spawns test zombies, shotguns and luggage near the player on Airport / Shore.</summary>
    internal static bool DebugMode => debugMode?.Value ?? false;

    private static ConfigEntry<bool>? canSpawnOnAnyBiome;

    /// <summary>
    /// When false (default), luggage loot is Roots-only.
    /// When true, the shotgun can also roll in luggage on any biome.
    /// Roots always gets <see cref="GuaranteedLuggageShotguns"/> random forced suitcases, regardless of this flag.
    /// </summary>
    internal static bool CanSpawnOnAnyBiome => canSpawnOnAnyBiome?.Value ?? false;

    /// <summary>How many random Roots luggage get a forced shotgun each run (anywhere on the Roots map).</summary>
    internal const int GuaranteedLuggageShotguns = 2;

    /// <summary>
    /// Hard cap: at most this many luggage in a single biome pool may contain a shotgun per run
    /// (random rolls and Roots guarantees both count).
    /// </summary>
    internal const int MaxShotgunsPerBiome = 4;

    /// <summary>All PEAK luggage spawn pools (biome suitcases).</summary>
    internal static readonly SpawnPool AllLuggagePools =
        SpawnPool.LuggageBeach
        | SpawnPool.LuggageJungle
        | SpawnPool.LuggageTundra
        | SpawnPool.LuggageCaldera
        | SpawnPool.LuggageClimber
        | SpawnPool.LuggageAncient
        | SpawnPool.LuggageCursed
        | SpawnPool.LuggageMesa
        | SpawnPool.LuggageRoots
        | SpawnPool.LuggageGloom
        | SpawnPool.LuggageCitadel
        | SpawnPool.LuggageClown;

    internal static SpawnPool ShotgunLuggagePools =>
        CanSpawnOnAnyBiome ? AllLuggagePools : SpawnPool.LuggageRoots;

    // Held pose. These read the config entries live (not cached at startup), and Update() re-reads the
    // .cfg file when it changes, so the pose can be tuned while the game is running.
    private static ConfigEntry<float>? poseRight;
    private static ConfigEntry<float>? poseUp;
    private static ConfigEntry<float>? poseForward;
    private static ConfigEntry<float>? poseLeftHandLeft;

    /// <summary>Metres the left hand sits left of the pump centre (item -X, turned with the aim).</summary>
    internal static float LeftHandLeft => poseLeftHandLeft?.Value ?? 0.09f;

    private static ConfigEntry<float>? poseRightWristTilt;

    /// <summary>Degrees the right hand's fingers tip up (about item +X) so the forearm comes from below and the elbow drops.</summary>
    internal static float RightWristTilt => poseRightWristTilt?.Value ?? 30f;

    private static ConfigEntry<float>? poseToeIn;
    private static ConfigEntry<float>? poseMuzzleUp;

    /// <summary>
    /// PEAK's <c>Item.defaultPos</c>: where the item root (and so the grip and both hands) is held, in the
    /// scout's look space (+X right, +Y up, +Z forward) from the head bone. The camera sits about 1 up from
    /// that bone (<c>Character.GetCameraPos</c>). The blowgun we clone uses (0, 0.33, 1): centred at the mouth.
    /// </summary>
    internal static Vector3 HeldDefaultPos => new(
        poseRight?.Value ?? 0.5f,
        poseUp?.Value ?? -0.4f,
        poseForward?.Value ?? 1.15f);

    /// <summary>Degrees the barrel turns left toward the crosshair. Negative turns it right.</summary>
    internal static float HeldToeIn => poseToeIn?.Value ?? -6f;

    /// <summary>Degrees the gun pitches about the grip: muzzle up, stock down. 0 = level with the aim.</summary>
    internal static float HeldMuzzleUp => poseMuzzleUp?.Value ?? 8f;

    /// <summary>Bumped whenever the .cfg file changes on disk so held guns re-place their hand anchors.</summary>
    internal static int PoseVersion { get; private set; }

    private DateTime lastConfigWriteUtc;
    private float nextConfigCheck;

    internal static float ModelRotX { get; private set; }
    internal static float ModelRotY { get; private set; }
    internal static float ModelRotZ { get; private set; }
    internal static float RestPosX { get; private set; }
    internal static float RestPosY { get; private set; }
    internal static float RestPosZ { get; private set; }
    internal static float RestRotX { get; private set; }
    internal static float RestRotY { get; private set; }
    internal static float RestRotZ { get; private set; }
    internal static float LuggageYaw { get; private set; }

    /// <summary>Extra lift so the centered mesh sits on the floor instead of halfway through it.</summary>
    internal static float RestMeshLift { get; private set; }

    internal static float LuggageLiftExtra { get; private set; }

    /// <summary>Mesh-space grip point (rear/stock). Held pose puts this at the item origin so the camera is not inside the gun.</summary>
    internal static Vector3 GripMeshPoint { get; private set; }

    /// <summary>Mesh-space forend point (under barrel) for the support hand.</summary>
    internal static Vector3 ForendMeshPoint { get; private set; }

    /// <summary>
    /// Mesh barrel runs stock (max.x) → muzzle (min.x) after the OBJ X-flip.
    /// Map that -X aim axis to item forward (+Z), top (+Z mesh) to up (+Y).
    /// </summary>
    internal static readonly Quaternion HeldBaseRotation =
        Quaternion.Inverse(Quaternion.LookRotation(Vector3.left, Vector3.forward));

    /// <summary>Toe-in / muzzle-up (plus Model.Rot*) in item space. The gun and both hand anchors turn by this.</summary>
    internal static Quaternion HeldAim =>
        Quaternion.Euler(ModelRotX - HeldMuzzleUp, ModelRotY - HeldToeIn, ModelRotZ);

    internal static Quaternion HeldLocalRotation => HeldAim * HeldBaseRotation;

    /// <summary>
    /// Grip at the item origin. PEAK holds the item origin at <see cref="HeldDefaultPos"/>, so the
    /// right hand (Hand_R at the origin) stays within arm's reach wherever the gun is placed on screen.
    /// </summary>
    internal static Vector3 HeldLocalPosition => -(HeldLocalRotation * (GripMeshPoint * ModelScale));

    internal static Vector3 RestLocalPosition => new(RestPosX, RestPosY + RestMeshLift, RestPosZ);

    internal static Quaternion RestLocalRotation =>
        Quaternion.Euler(RestRotX, RestRotY, RestRotZ);

    /// <summary>Mesh-space muzzle tip (after OBJ X-flip this is bounds.min.x).</summary>
    internal static Vector3 MuzzleMeshPoint { get; private set; }

    internal static void UpdateMeshAnchors(Mesh mesh)
    {
        Bounds b = mesh.bounds;
        // OBJ loader flips X: source stock (was -X) lands on max.x, muzzle on min.x.
        // The mesh is not axis-aligned (the stock drops and drifts sideways), so whole-mesh bounds put the
        // hands beside and below the gun. Centre each hand on the cross-section of the part it holds instead.
        // Slices are fractions from muzzle (0) to butt (1): the stock wrist sits just behind the trigger guard,
        // the pump is the ribbed block under the barrel ahead of the receiver.
        Vector3[] vertices = mesh.vertices;
        Bounds wrist = SliceBounds(vertices, b, 0.64f, 0.72f);
        Bounds pump = SliceBounds(vertices, b, 0.18f, 0.30f);
        GripMeshPoint = wrist.center;
        // Palm under the pump: hand bone at the pump's underside.
        ForendMeshPoint = new Vector3(pump.center.x, pump.center.y, pump.min.z);
        // Barrel end: centre of the tip slice. Whole-mesh bounds put it 15 cm low and 7 cm to the side.
        Bounds tip = SliceBounds(vertices, b, 0f, 0.02f);
        MuzzleMeshPoint = new Vector3(b.min.x, tip.center.y, tip.center.z);

        RestMeshLift = -MinRestYInItemSpace(mesh.vertices, new Vector3(RestPosX, RestPosY, RestPosZ));
        Log?.LogInfo(
            $"Mesh anchors: grip={GripMeshPoint}, forend={ForendMeshPoint}, muzzle={MuzzleMeshPoint}, restLift={RestMeshLift:0.###}.");
    }

    private static Bounds SliceBounds(Vector3[] vertices, Bounds whole, float t0, float t1)
    {
        float x0 = Mathf.Lerp(whole.min.x, whole.max.x, t0);
        float x1 = Mathf.Lerp(whole.min.x, whole.max.x, t1);
        Vector3 min = Vector3.positiveInfinity;
        Vector3 max = Vector3.negativeInfinity;
        foreach (Vector3 v in vertices)
        {
            if (v.x >= x0 && v.x <= x1)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        }

        if (min.x > max.x)
        {
            return new Bounds(new Vector3((x0 + x1) * 0.5f, whole.center.y, whole.center.z), Vector3.zero);
        }

        var slice = new Bounds();
        slice.SetMinMax(min, max);
        return slice;
    }

    /// <summary>
    /// Luggage centers on mainRenderer.bounds; lift the item root so the rest pose sits on the cavity floor.
    /// Diagonal yaw is applied on the visual in <see cref="LuggageShotgunOffsetPatch"/> (not the item root).
    /// </summary>
    internal static void ConfigureLuggageSpawn(Item item, Mesh mesh)
    {
        Vector3 restPos = RestLocalPosition;
        float minY = MinRestYInItemSpace(mesh.vertices, restPos);
        float maxY = MaxRestYInItemSpace(mesh.vertices, restPos);
        float centerY = (minY + maxY) * 0.5f;
        float liftFromCenterToBottom = centerY - minY;

        item.offsetLuggageSpawn = true;
        // Never rotate the item root here — that tips the gun off the suitcase floor.
        // Position offset is applied by PEAK then undone/replaced by LuggageShotgunOffsetPatch
        // with a vertex snap after the flat-lay pose (renderer.bounds would float the gun).
        item.offsetLuggageRotation = Vector3.zero;
        item.offsetLuggagePosition = new Vector3(0f, liftFromCenterToBottom + LuggageLiftExtra, 0f);

        Log?.LogInfo(
            $"Luggage spawn offset Y={item.offsetLuggagePosition.y:0.###} (rest Y {minY:0.###}..{maxY:0.###}, LuggageYaw={LuggageYaw}).");
    }

    /// <summary>
    /// Force the barrel into the suitcase floor plane (spawn slots tip the item root), then yaw.
    /// Mesh +X = barrel, mesh +Z = top — same axis map as held aim, but forward stays on the floor.
    /// </summary>
    internal static Quaternion LuggageVisualRotation(Transform itemRoot, Vector3 luggageWorldUp)
    {
        Vector3 localUp = itemRoot.InverseTransformDirection(luggageWorldUp);
        if (localUp.sqrMagnitude < 0.0001f)
        {
            localUp = Vector3.up;
        }

        localUp.Normalize();

        Vector3 projected = Vector3.ProjectOnPlane(Vector3.right, localUp);
        if (projected.sqrMagnitude < 0.0001f)
        {
            projected = Vector3.ProjectOnPlane(Vector3.forward, localUp);
        }

        Vector3 floorForward = Quaternion.AngleAxis(LuggageYaw, localUp) * projected.normalized;
        return Quaternion.LookRotation(floorForward, localUp) * HeldBaseRotation;
    }

    private static float MinRestYInItemSpace(Vector3[] vertices, Vector3 visualLocalPos)
    {
        float minY = float.MaxValue;
        Quaternion rest = RestLocalRotation;
        float scale = ModelScale;
        foreach (Vector3 v in vertices)
        {
            float y = (visualLocalPos + rest * (v * scale)).y;
            if (y < minY)
            {
                minY = y;
            }
        }

        return minY;
    }

    private static float MaxRestYInItemSpace(Vector3[] vertices, Vector3 visualLocalPos)
    {
        float maxY = float.MinValue;
        Quaternion rest = RestLocalRotation;
        float scale = ModelScale;
        foreach (Vector3 v in vertices)
        {
            float y = (visualLocalPos + rest * (v * scale)).y;
            if (y > maxY)
            {
                maxY = y;
            }
        }

        return maxY;
    }

    private bool reportedMissingBlowgun;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        shots = Config.Bind("Shotgun", "Shots", 5, new ConfigDescription(
            "Shots in each shotgun. Host's value is used in multiplayer. Save the .cfg and start a new map (no need to quit PEAK).",
            new AcceptableValueRange<int>(1, 999)));
        recoilEnabled = Config.Bind("Shotgun", "EnableRecoil", true, "Push the shooter back on each shot. Strength is Recoil.");
        recoil = Config.Bind("Shotgun", "Recoil", 5f, new ConfigDescription(
            "Metres per second the shooter is pushed back, opposite the aim, on each shot. Used when EnableRecoil is true.",
            new AcceptableValueRange<float>(0f, 10f)));
        canSpawnOnAnyBiome = Config.Bind(
            "Shotgun",
            "CanSpawnOnAnyBiome",
            false,
            "If false (default), shotguns only appear in Roots luggage. If true, they can also roll in luggage in other biomes. Roots always forces two random suitcases to contain a shotgun either way.");
        debugMode = Config.Bind("Debug", "EnableDebugMode", false, "Host spawns test zombies, a shotgun and luggage near the player at the Airport and on the Shore. Off = spawn nothing.");
        ModelScale = Config.Bind("Model", "Scale", 0.5f, "Uniform scale of the custom shotgun mesh (mesh is unit-normalized). Keep ≤0.55 so standing over it does not hit the camera near-clip.").Value;
        // New section on purpose: BepInEx keeps values already saved in the .cfg, so reusing an old key
        // would ignore a changed default. Older [Model] PosX/PosY/PosZ, [Hold], [Pose] and [HeldPose] entries are unused.
        // These are live: edit and save the .cfg while the game runs and the pose updates within a second.
        poseRight = Config.Bind("HoldPose", "Right", 0.5f, "Item.defaultPos.x: how far right of the head the gun and both hands are held. 0 = centred.");
        poseUp = Config.Bind("HoldPose", "Up", -0.4f, "Item.defaultPos.y: height relative to the head bone. The blowgun uses 0.33 (mouth). Bigger = higher on screen.");
        poseForward = Config.Bind("HoldPose", "Forward", 1.15f, "Item.defaultPos.z: metres in front of the head the gun is pulled toward (vanilla items use 1). The arms stop it at their reach, so past that it mostly changes how hard it is pulled.");
        poseLeftHandLeft = Config.Bind("HoldPose", "LeftHandLeft", 0.09f, "Metres the left hand sits left of the pump centre. Negative = right.");
        poseRightWristTilt = Config.Bind("HoldPose", "RightWristTiltDegrees", 30f, "Degrees the right hand's fingers tip up on the grip. Bigger = right elbow lower. 0 = the vanilla RopeShooter grip.");
        poseToeIn = Config.Bind("HoldPose", "ToeInDegrees", -6f, "Degrees the barrel turns left toward the crosshair. Negative = barrel turns right. 0 = parallel to the aim.");
        poseMuzzleUp = Config.Bind("HoldPose", "MuzzleUpDegrees", 8f, "Degrees the gun pitches about the grip: muzzle up and stock down. 0 = level with the aim. Negative = stock up.");
        // Extra euler applied on top of the barrel→forward / top→up base alignment. Leave at 0 unless tuning.
        ModelRotX = Config.Bind("Model", "RotX", 0f, "Extra held local X euler on top of barrel-forward alignment.").Value;
        ModelRotY = Config.Bind("Model", "RotY", 0f, "Extra held local Y euler on top of barrel-forward alignment.").Value;
        ModelRotZ = Config.Bind("Model", "RotZ", 0f, "Extra held local Z euler on top of barrel-forward alignment.").Value;
        // Rest pose: identity keeps the barrel on +X so ground/luggage lays flat (no tip toward sky).
        RestPosX = Config.Bind("Model", "RestPosX", 0f, "Rest (luggage/ground) local X offset of the custom mesh.").Value;
        RestPosY = Config.Bind("Model", "RestPosY", 0f, "Extra padding above the floor after auto-sitting the mesh on its lowest point.").Value;
        RestPosZ = Config.Bind("Model", "RestPosZ", 0f, "Rest (luggage/ground) local Z offset of the custom mesh.").Value;
        // RestRotY≠0 tips the barrel toward item +Z (sky in luggage). Keep 0; use LuggageYaw for diagonal.
        RestRotX = Config.Bind("Model", "RestRotX", 0f, "Rest (luggage/ground) local X euler rotation of the custom mesh.").Value;
        RestRotY = Config.Bind("Model", "RestRotY", 0f, "Rest local Y euler. Leave 0 — non-zero tips the barrel at the sky in luggage.").Value;
        RestRotZ = Config.Bind("Model", "RestRotZ", 0f, "Rest (luggage/ground) local Z euler rotation of the custom mesh.").Value;
        // Luggage-only flat diagonal: yaw around the suitcase floor normal on the visual (not the item root).
        LuggageYaw = Config.Bind("Model", "LuggageYaw", 45f, "Luggage-only yaw around the suitcase floor normal (degrees). Flip sign if diagonal goes the wrong way.").Value;
        LuggageLiftExtra = Config.Bind("Model", "LuggageFloorPad", 0.025f, "Extra padding above the luggage floor after snapping the mesh bottom onto the suitcase lining.").Value;
        new Harmony(Name ?? "PeakShotgun").PatchAll();
        gameObject.AddComponent<ShotgunCameraNearClip>();
        gameObject.AddComponent<HostShotSync>();
        StartCoroutine(WaitForBlowgun());
        shoreSpawnRoutine = StartCoroutine(ShoreTestSpawns.WhenTheShoreIsReady());
        SceneManager.sceneLoaded += OnSceneLoaded;
        lastConfigWriteUtc = SafeConfigWriteTime();
        LogPose("Held pose at startup");
        Log.LogInfo($"Plugin {Name} is loaded.");
    }

    private DateTime SafeConfigWriteTime()
    {
        try
        {
            return File.GetLastWriteTimeUtc(Config.ConfigFilePath);
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }

    private static void LogPose(string prefix) =>
        Log.LogInfo($"{prefix}: defaultPos={HeldDefaultPos}, toeIn={HeldToeIn:0.#}°, muzzleUp={HeldMuzzleUp:0.#}°.");

    /// <summary>
    /// Live tuning: when the .cfg file changes on disk, reload it and tell held guns to re-place their
    /// hand anchors. Polled twice a second; costs one file-timestamp read.
    /// </summary>
    private void Update()
    {
        if (Time.unscaledTime < nextConfigCheck)
        {
            return;
        }

        nextConfigCheck = Time.unscaledTime + 0.5f;
        DateTime write = SafeConfigWriteTime();
        if (write == lastConfigWriteUtc)
        {
            return;
        }

        ReloadConfigFromDisk("cfg changed");
    }

    /// <summary>
    /// Re-reads <c>PeakShotgun.cfg</c> from disk into the live entries, then applies ammo / loot / pose.
    /// Map restart calls this so you do not need to quit PEAK after editing the file.
    /// </summary>
    private void ReloadConfigFromDisk(string reason)
    {
        try
        {
            lastConfigWriteUtc = SafeConfigWriteTime();
            Config.Reload();
            PoseVersion++;
            ApplyLuggageSpawnConfig();
            // Host publishes Shots; clients keep the room value (ignore their own Shots for ammo).
            HostShotSync.SyncFromLocalRole(reason);
            LogPose($"Config reloaded ({reason}); held pose now");
            Log.LogInfo($"Config reloaded ({reason}); shots per shotgun now {ShotCount}.");
        }
        catch (Exception e)
        {
            Log.LogWarning($"Config reload failed ({reason}): {e.Message}");
        }
    }

    /// <summary>
    /// Writes <see cref="ShotCount"/> onto the prefab and live guns' <see cref="Item.totalUses"/> only.
    /// Remaining shots live in networked <c>ItemUses</c> — never rewrite that here (drop/equip
    /// re-spawns the prefab and reapplies instance data; refilling would look like infinite ammo).
    /// </summary>
    internal static void ApplyShotCountConfig()
    {
        int count = ShotCount;
        if (ShotgunPrefab != null)
        {
            Item? prefabItem = ShotgunPrefab.GetComponent<Item>();
            if (prefabItem != null)
            {
                prefabItem.totalUses = count;
            }
        }

        foreach (ShotgunInstanceSetup setup in ShotgunInstanceSetup.Active)
        {
            if (setup?.Item == null)
            {
                continue;
            }

            setup.Item.totalUses = count;
        }

        ShotgunAmmoUI.Refresh();
    }

    private Coroutine? shoreSpawnRoutine;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Always re-read the .cfg here: map restart must see edits made while PEAK stayed open.
        // The Update() poll can lose a race with luggage / debug spawns on the new scene.
        ReloadConfigFromDisk("map load");

        if (shoreSpawnRoutine != null)
        {
            StopCoroutine(shoreSpawnRoutine);
        }

        shoreSpawnRoutine = StartCoroutine(ShoreTestSpawns.WhenTheShoreIsReady());
        RootsLuggagePatch.ResetGuarantees();
    }

    private IEnumerator WaitForBlowgun()
    {
        var wait = new WaitForSecondsRealtime(0.5f);
        while (ShotgunPrefab == null)
        {
            try
            {
                CreateFromBlowgun();
            }
            catch (Exception exception)
            {
                Log.LogWarning($"Could not create the shotgun yet: {exception.Message}");
            }

            if (ShotgunPrefab != null)
            {
                yield break;
            }

            yield return wait;
        }
    }

    private bool buildingShotgun;

    internal void CreateFromBlowgun()
    {
        if (ShotgunPrefab != null || buildingShotgun)
        {
            return;
        }

        ItemDatabase? database = SingletonAsset<ItemDatabase>.Instance;
        if (database?.Objects == null || database.Objects.Count == 0)
        {
            return;
        }

        Item? blowgun = FindBlowgun(database);
        if (blowgun == null)
        {
            if (!reportedMissingBlowgun)
            {
                reportedMissingBlowgun = true;
                Log.LogError($"The item catalog has {database.Objects.Count} items, and none is the blowgun.");
            }

            return;
        }

        buildingShotgun = true;
        try
        {
            BuildShotgunFromBlowgun(blowgun, database);
        }
        finally
        {
            buildingShotgun = false;
        }
    }

    private void BuildShotgunFromBlowgun(Item blowgun, ItemDatabase database)
    {
        Log.LogInfo($"Building the shotgun from {blowgun.name}.");

        bool sourceWasActive = blowgun.gameObject.activeSelf;
        if (sourceWasActive)
        {
            blowgun.gameObject.SetActive(false);
        }

        GameObject gunObject = Instantiate(blowgun.gameObject);
        if (sourceWasActive)
        {
            blowgun.gameObject.SetActive(true);
        }
        // PEAKLib prefixes with the mod id → network id "0_Items/PeakShotgun:Shotgun".
        // Do not pre-prefix or backpack Instantiate looks for a double PeakShotgun: name.
        gunObject.name = "Shotgun";
        gunObject.SetActive(false);
        DontDestroyOnLoad(gunObject);

        Action_RaycastDart? dart = gunObject.GetComponent<Action_RaycastDart>();
        Transform spawn = dart != null ? dart.spawnTransform : gunObject.transform;
        SFX_Instance? shotSfx = dart != null ? dart.shotSFX : null;
        foreach (ItemAction existing in gunObject.GetComponents<ItemAction>())
        {
            DestroyImmediate(existing);
        }

        foreach (ParticleSystem particles in gunObject.GetComponentsInChildren<ParticleSystem>(true))
        {
            DestroyImmediate(particles);
        }

        Transform? customMuzzle = ShotgunModelSwap.Apply(gunObject, database);
        if (customMuzzle != null)
        {
            spawn = customMuzzle;
        }

        Item item = gunObject.GetComponent<Item>();
        item.usingTimePrimary = 0f;
        item.showUseProgress = true;
        item.totalUses = ShotCount;
        item.UIData.itemName = "Shotgun";
        ApplyIcon(item);
        item.UIData.hideFuel = false;
        item.UIData.hasMainInteract = true;
        item.UIData.mainInteractPrompt = "SHOOT";
        if (spawn == null)
        {
            spawn = gunObject.transform;
        }

        Action_Gun gun = gunObject.AddComponent<Action_Gun>();
        gun.maxDistance = 40f;
        gun.pelletRadius = 0.2f;
        gun.fireRate = 0.85f;
        gun.spread = 0.07f;
        gun.pelletCount = 8;
        gun.OnHeld = true;
        gun.spawnTransform = spawn;
        gun.shotSFX = PlaceholderShot() ?? shotSfx;
        if (gunObject.TryGetComponent(out ItemUseFeedback feedback))
        {
            feedback.sfxUsed = null;
        }

        GameObject? smoke = ShotgunVFX.FindSmoke();
        if (smoke == null)
        {
            Log.LogWarning("No PEAK smoke effect was found. The shot will only flash a light.");
        }
        else
        {
            Log.LogInfo($"Muzzle smoke uses {smoke.name}.");
        }

        gunObject.AddComponent<ShotgunVFX>().Build(spawn, smoke);
        gun.afflictionsOnHit =
        [
            new Affliction_AdjustStatus(CharacterAfflictions.STATUSTYPE.Injury, 0.35f, 1f),
        ];
        gunObject.AddComponent<Action_Ammo>();

        // Zombie-maps-only (default): Roots pool + Rare (first 1–2 Roots rolls are forced).
        // Off: every luggage biome, Common roll, no force.
        LootData loot = gunObject.GetComponent<LootData>() ?? gunObject.AddComponent<LootData>();
        loot.banInSolo = false;
        loot.rarityOverrides.Clear();
        ApplyLuggageSpawnConfig(loot);
        LootData.AllSpawnWeightData = null;

        new ItemContent(item).Register(ModDefinition.GetOrCreate(Info));
        ShotgunPrefab = gunObject;
        Log.LogInfo(
            CanSpawnOnAnyBiome
                ? $"Registered the shotgun for all biomes' luggage ({GuaranteedLuggageShotguns} random Roots suitcases always forced)."
                : $"Registered the shotgun for Roots luggage only ({GuaranteedLuggageShotguns} random suitcases always forced).");
    }

    /// <summary>Applies <see cref="CanSpawnOnAnyBiome"/> to the prefab loot table (startup + live config reload).</summary>
    internal static void ApplyLuggageSpawnConfig(LootData? loot = null)
    {
        if (loot == null && ShotgunPrefab != null)
        {
            loot = ShotgunPrefab.GetComponent<LootData>();
        }

        if (loot == null)
        {
            return;
        }

        loot.spawnLocations = ShotgunLuggagePools;
        // Roots-only: Rare so the two forced suitcases dominate. All biomes: very rare rolls,
        // still hard-capped at MaxShotgunsPerBiome (Common used to flood Mesa/canyon luggage).
        loot.Rarity = CanSpawnOnAnyBiome ? Rarity.RidiculouslyRare : Rarity.Rare;
        LootData.AllSpawnWeightData = null;
        RootsLuggagePatch.ResetGuarantees();
    }

    private static void ApplyIcon(Item item)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Peak.Shotgun.icons.ShotgunIcon.png");
        if (stream == null)
        {
            Log.LogWarning("Shotgun icon resource was not found.");
            return;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(memory.ToArray()))
        {
            Log.LogWarning("Shotgun icon could not be decoded.");
            return;
        }

        texture.name = "Shotgun";
        texture.wrapMode = TextureWrapMode.Clamp;
        item.UIData.icon = texture;
    }

    private static SFX_Instance? PlaceholderShot()
    {
        AudioClip? clip = LoadWav("Peak.Shotgun.sounds.shotgun_fire_01.wav");
        if (clip == null)
        {
            Log.LogWarning("Shotgun fire sound was not found. Using the blowgun sound.");
            return null;
        }

        var shot = ScriptableObject.CreateInstance<SFX_Instance>();
        shot.clips = new[] { clip };
        shot.settings = new SFX_Settings
        {
            volume = 0.85f,
            volume_Variation = 0.05f,
            pitch = 1f,
            pitch_Variation = 0.04f,
            spatialBlend = 1f,
            range = 80f,
            cooldown = 0.05f,
        };
        return shot;
    }

    private static AudioClip? LoadWav(string resourceName)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        byte[] wav = memory.ToArray();
        if (wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
        {
            return null;
        }

        int channels = 0;
        int sampleRate = 0;
        int bits = 0;
        int dataStart = 0;
        int dataLength = 0;
        int cursor = 12;
        while (cursor + 8 <= wav.Length)
        {
            string chunk = System.Text.Encoding.ASCII.GetString(wav, cursor, 4);
            int size = BitConverter.ToInt32(wav, cursor + 4);
            int body = cursor + 8;
            if (chunk == "fmt " && size >= 16)
            {
                channels = BitConverter.ToInt16(wav, body + 2);
                sampleRate = BitConverter.ToInt32(wav, body + 4);
                bits = BitConverter.ToInt16(wav, body + 14);
            }
            else if (chunk == "data")
            {
                dataStart = body;
                dataLength = size;
                break;
            }

            cursor = body + size + (size % 2);
        }

        if (channels <= 0 || sampleRate <= 0 || bits != 16 || dataStart == 0 || dataStart + dataLength > wav.Length)
        {
            Log.LogWarning("Shotgun fire sound is not 16-bit PCM.");
            return null;
        }

        int sampleCount = dataLength / 2;
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short value = BitConverter.ToInt16(wav, dataStart + i * 2);
            samples[i] = value / 32768f;
        }

        var clip = AudioClip.Create("shotgun_fire_01", sampleCount / channels, channels, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static Item? FindBlowgun(ItemDatabase database)
    {
        Item? dartItem = null;
        for (int i = 0; i < database.Objects.Count; i++)
        {
            Item candidate = database.Objects[i];
            if (candidate == null)
            {
                continue;
            }

            string uiName = candidate.UIData != null ? candidate.UIData.itemName ?? "" : "";
            bool namedBlowgun = candidate.name.IndexOf("Blowgun", StringComparison.OrdinalIgnoreCase) >= 0
                || candidate.name.IndexOf("HealingDart", StringComparison.OrdinalIgnoreCase) >= 0
                || uiName.IndexOf("Blowgun", StringComparison.OrdinalIgnoreCase) >= 0;
            bool shootsDarts = candidate.GetComponent<Action_RaycastDart>() != null;
            if (shootsDarts && namedBlowgun)
            {
                return candidate;
            }

            if (shootsDarts && dartItem == null)
            {
                dartItem = candidate;
            }
        }

        return dartItem;
    }
}

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

namespace PeakShotgun;

[BepInAutoPlugin]
[BepInDependency(ItemsPlugin.Id)]
[BepInDependency(CorePlugin.Id)]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    internal static Plugin Instance { get; private set; } = null!;

    internal static GameObject? ShotgunPrefab { get; private set; }

    internal static int ShotCount { get; private set; } = 5;

    internal static float ModelScale { get; private set; } = 0.015f;
    internal static float ModelPosX { get; private set; }

    // Held pose. These read the config entries live (not cached at startup), and Update() re-reads the
    // .cfg file when it changes, so the pose can be tuned while the game is running.
    private static ConfigEntry<float>? poseScreenRight;
    private static ConfigEntry<float>? poseDown;
    private static ConfigEntry<float>? poseForward;
    private static ConfigEntry<float>? poseToeIn;
    private static ConfigEntry<float>? poseMuzzleUp;

    /// <summary>Moves the held gun and both hand anchors toward the scout's right (item +X).</summary>
    internal static float HeldOffsetRight => poseScreenRight?.Value ?? 0.79f;

    /// <summary>Moves the held gun AND both hand anchors down (item -Y). Negative = up.</summary>
    internal static float HeldOffsetDown => poseDown?.Value ?? 0.36f;

    /// <summary>Extra push forward (item +Z) on top of Model.PosZ. Negative = toward the body.</summary>
    internal static float HeldOffsetForward => poseForward?.Value ?? 0f;

    /// <summary>Degrees the barrel turns toward the crosshair (left, since the gun sits on the right).</summary>
    internal static float HeldToeIn => poseToeIn?.Value ?? 5f;

    /// <summary>Degrees the muzzle tips up. FPS guns sit below the crosshair and angle slightly up to it.</summary>
    internal static float HeldMuzzleUp => poseMuzzleUp?.Value ?? 2f;

    /// <summary>Bumped whenever the .cfg file changes on disk so held guns re-place their hand anchors.</summary>
    internal static int PoseVersion { get; private set; }

    private DateTime lastConfigWriteUtc;
    private float nextConfigCheck;

    internal static float ModelPosY { get; private set; }
    internal static float ModelPosZ { get; private set; }
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

    internal static Quaternion HeldLocalRotation =>
        Quaternion.Euler(ModelRotX - HeldMuzzleUp, ModelRotY - HeldToeIn, ModelRotZ) * HeldBaseRotation;

    /// <summary>
    /// Grip at the item origin (where PEAK puts the hands), plus a small Pos* fine-tune.
    /// Without the grip pivot, the centered mesh puts the camera inside the receiver in FP.
    /// </summary>
    internal static Vector3 HeldLocalPosition =>
        new Vector3(ModelPosX + HeldOffsetRight, ModelPosY - HeldOffsetDown, ModelPosZ + HeldOffsetForward) - HeldLocalRotation * (GripMeshPoint * ModelScale);

    internal static Vector3 RestLocalPosition => new(RestPosX, RestPosY + RestMeshLift, RestPosZ);

    internal static Quaternion RestLocalRotation =>
        Quaternion.Euler(RestRotX, RestRotY, RestRotZ);

    /// <summary>Mesh-space muzzle tip (after OBJ X-flip this is bounds.min.x).</summary>
    internal static Vector3 MuzzleMeshPoint { get; private set; }

    internal static void UpdateMeshAnchors(Mesh mesh)
    {
        Bounds b = mesh.bounds;
        // OBJ loader flips X: source stock (was -X) lands on max.x, muzzle on min.x.
        // Gripping min.x put the pivot on the muzzle so the barrel ran back through the scout
        // (looked like "points at my head" / turn left → gun swings right).
        GripMeshPoint = new Vector3(
            Mathf.Lerp(b.min.x, b.max.x, 0.82f),
            b.center.y,
            Mathf.Lerp(b.min.z, b.max.z, 0.35f));
        ForendMeshPoint = new Vector3(
            Mathf.Lerp(b.min.x, b.max.x, 0.38f),
            b.center.y - b.extents.y * 0.15f,
            Mathf.Lerp(b.min.z, b.max.z, 0.35f));
        MuzzleMeshPoint = new Vector3(b.min.x, b.center.y, b.center.z);

        RestMeshLift = -MinRestYInItemSpace(mesh.vertices, new Vector3(RestPosX, RestPosY, RestPosZ));
        Log?.LogInfo(
            $"Mesh anchors: grip={GripMeshPoint}, forend={ForendMeshPoint}, muzzle={MuzzleMeshPoint}, restLift={RestMeshLift:0.###}.");
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
        ShotCount = Mathf.Max(1, Config.Bind("Shotgun", "Shots", 5, "Shots in each shotgun.").Value);
        ModelScale = Config.Bind("Model", "Scale", 0.5f, "Uniform scale of the custom shotgun mesh (mesh is unit-normalized). Keep ≤0.55 so standing over it does not hit the camera near-clip.").Value;
        ModelPosX = Config.Bind("Model", "PosX", 0f, "Extra held X offset after grip-pivot (usually 0).").Value;
        // New section on purpose: BepInEx keeps values already saved in the .cfg, so reusing an old key
        // would ignore a changed default. Older [Model] HeldSideOffset / HeldOffsetRight / HeldOffsetDown
        // and [HeldPose] entries in the .cfg are simply unused now.
        // These are live: edit and save the .cfg while the game runs and the pose updates within a second.
        poseScreenRight = Config.Bind("Pose", "ScreenRight", 0.79f, "Metres to move the held shotgun and both hands to the scout's right.");
        poseDown = Config.Bind("Pose", "Down", 0.36f, "Metres to move the gun AND both hands down. Bigger = lower. Negative = higher.");
        poseForward = Config.Bind("Pose", "Forward", 0f, "Metres to push the gun AND both hands forward (on top of Model.PosZ). Negative = closer to the body.");
        poseToeIn = Config.Bind("Pose", "ToeInDegrees", 5f, "Degrees the barrel turns left toward the crosshair. Makes the gun show its side in first person instead of pointing straight away. 0 = parallel to the aim.");
        poseMuzzleUp = Config.Bind("Pose", "MuzzleUpDegrees", 2f, "Degrees the muzzle tips up toward the crosshair. Negative = muzzle down.");
        ModelPosY = Config.Bind("Model", "PosY", -0.04f, "Extra held Y offset after grip-pivot (negative lowers the gun in FP).").Value;
        ModelPosZ = Config.Bind("Model", "PosZ", 0.18f, "Extra held Z offset after grip-pivot (push forward away from camera).").Value;
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
        LuggageLiftExtra = Config.Bind("Model", "LuggageLiftExtra", 0.01f, "Extra padding above the luggage floor after snapping the mesh bottom onto the spawn plane.").Value;
        new Harmony(Name ?? "PeakShotgun").PatchAll();
        gameObject.AddComponent<ShotgunCameraNearClip>();
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
        Log.LogInfo(
            $"{prefix}: screenRight={HeldOffsetRight:0.###}, down={HeldOffsetDown:0.###}, forward={HeldOffsetForward:0.###}, "
            + $"toeIn={HeldToeIn:0.#}°, muzzleUp={HeldMuzzleUp:0.#}° "
            + $"(Model.PosY={ModelPosY:0.###}, PosZ={ModelPosZ:0.###}).");

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

        lastConfigWriteUtc = write;
        try
        {
            Config.Reload();
            PoseVersion++;
            LogPose("Config reloaded; held pose now");
        }
        catch (Exception e)
        {
            Log.LogWarning($"Config reload failed (pose unchanged): {e.Message}");
        }
    }

    private Coroutine? shoreSpawnRoutine;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (shoreSpawnRoutine != null)
        {
            StopCoroutine(shoreSpawnRoutine);
        }

        shoreSpawnRoutine = StartCoroutine(ShoreTestSpawns.WhenTheShoreIsReady());
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

        Transform? customMuzzle = ShotgunModelSwap.Apply(gunObject, database, blowgun);
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

        // Every Shore and Roots luggage includes one shotgun. Shore is for testing.
        LootData loot = gunObject.GetComponent<LootData>() ?? gunObject.AddComponent<LootData>();
        loot.Rarity = Rarity.Common;
        loot.spawnLocations = SpawnPool.LuggageBeach | SpawnPool.LuggageRoots;
        loot.banInSolo = false;
        loot.rarityOverrides.Clear();
        LootData.AllSpawnWeightData = null;

        new ItemContent(item).Register(ModDefinition.GetOrCreate(Info));
        ShotgunPrefab = gunObject;
        Log.LogInfo("Registered the shotgun in every Shore and Roots luggage.");
    }

    private static void ApplyIcon(Item item)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PeakShotgun.icons.ShotgunIcon.png");
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
        AudioClip? clip = LoadWav("PeakShotgun.sounds.ShotgunBlast.wav");
        if (clip == null)
        {
            Log.LogWarning("Placeholder shotgun shot was not found. Using the blowgun sound.");
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
            Log.LogWarning("Placeholder shotgun shot is not 16-bit PCM.");
            return null;
        }

        int sampleCount = dataLength / 2;
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short value = BitConverter.ToInt16(wav, dataStart + i * 2);
            samples[i] = value / 32768f;
        }

        var clip = AudioClip.Create("ShotgunBlast", sampleCount / channels, channels, sampleRate, false);
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

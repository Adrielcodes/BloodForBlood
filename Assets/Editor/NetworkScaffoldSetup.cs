using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class NetworkScaffoldSetup
{
    private const string PrefabDir = "Assets/Prefabs";
    private const string CharacterMaterialsDir = "Assets/Art/Characters/Materials";
    private const string AnimationsDir = "Assets/Art/Animations";
    private const string AnimatorControllerPath = AnimationsDir + "/CharacterAnimator.controller";

    [MenuItem("Blood For Blood/Setup Network Scaffolding")]
    public static void Setup()
    {
        AssignCharacterPlaceholderMaterials();
        CreateOrUpdateCharacterAnimatorController();

        CreateGroundPlane();
        CreateEnvironmentDressing();
        SetupAmbiance();
        SetupPostProcessing();
        GameObject survivorPrefab = CreateSurvivorPrefab();
        GameObject killerPrefab = CreateKillerPrefab();
        CreateOrUpdateNetworkManager(survivorPrefab, killerPrefab);

        GameObject weaponPickupPrefab = CreateWeaponPickupPrefab();
        PlaceWeaponPickupInScene(weaponPickupPrefab);

        GameObject matchManagerPrefab = CreateMatchManagerPrefab();
        PlaceMatchManagerInScene(matchManagerPrefab);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        CreateBootScene();
        SetPlayModeStartScene();

        Debug.Log("Blood For Blood: network scaffolding created (Ground + NetworkManager + Survivor/Killer prefabs + WeaponPickup + MatchManager + Boot scene).");
    }

    // EditorSceneManager.playModeStartScene makes the Editor's Play button always launch from
    // Boot.unity regardless of which scene is currently open — without this, pressing Play while
    // SampleScene is open (the normal dev workflow, e.g. testing gameplay directly) skips the
    // boot/title/menu sequence entirely, since Unity otherwise just plays whatever scene is open.
    // This is a local Editor preference, not a serialized project asset — it doesn't survive a
    // fresh clone or reliably survive an Editor restart, so it's set here every Setup() run
    // instead of being a one-time manual step.
    private static void SetPlayModeStartScene()
    {
        SceneAsset bootAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Boot.unity");
        if (bootAsset == null) return;

        EditorSceneManager.playModeStartScene = bootAsset;
    }

    private static void CreateGroundPlane()
    {
        if (GameObject.Find("Ground") != null)
        {
            Debug.Log("Blood For Blood: Ground already present in scene, skipping.");
            return;
        }

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(5f, 1f, 5f);
    }

    // Ground is a 10x10-unit Plane primitive scaled (5,1,5) -> 50x50 world units centered on the
    // origin. Player spawns run along X at z=0 (see RoleAssignmentManager.SpawnPositionFor) and
    // WeaponPickup sits at (3,1,5) — prop placements below stay clear of both.
    private static void CreateEnvironmentDressing()
    {
        if (GameObject.Find("EnvironmentDressing") != null)
        {
            Debug.Log("Blood For Blood: EnvironmentDressing already present in scene, skipping.");
            return;
        }

        var root = new GameObject("EnvironmentDressing");

        BuildPerimeterWalls(root.transform);
        BuildObstacleProps(root.transform);
    }

    private static void BuildPerimeterWalls(Transform parent)
    {
        Material wallMat = CreateColorMaterial("PerimeterWallMat", new Color(0.14f, 0.14f, 0.16f));
        const float half = 24f;
        const float thickness = 1f;
        const float height = 4f;

        CreateSolidPart(PrimitiveType.Cube, parent, wallMat, "PerimeterWallNorth",
            new Vector3(0f, height / 2f, half), new Vector3(half * 2f, height, thickness));
        CreateSolidPart(PrimitiveType.Cube, parent, wallMat, "PerimeterWallSouth",
            new Vector3(0f, height / 2f, -half), new Vector3(half * 2f, height, thickness));
        CreateSolidPart(PrimitiveType.Cube, parent, wallMat, "PerimeterWallEast",
            new Vector3(half, height / 2f, 0f), new Vector3(thickness, height, half * 2f));
        CreateSolidPart(PrimitiveType.Cube, parent, wallMat, "PerimeterWallWest",
            new Vector3(-half, height / 2f, 0f), new Vector3(thickness, height, half * 2f));
    }

    // Chase-friendly loop cover (DBD-style: rock clusters, dead trees, crate stacks scattered
    // around the map edges, clear of the spawn line and the weapon pickup) rather than open ground.
    private static void BuildObstacleProps(Transform parent)
    {
        Material rockMat = CreateColorMaterial("RockMat", new Color(0.35f, 0.34f, 0.32f));
        Material treeMat = CreateColorMaterial("DeadTreeMat", new Color(0.22f, 0.18f, 0.14f));
        Material crateMat = CreateColorMaterial("CrateMat", new Color(0.42f, 0.29f, 0.16f));

        BuildRockCluster(parent, rockMat, new Vector3(0f, 0f, -10f));
        BuildRockCluster(parent, rockMat, new Vector3(-14f, 0f, 6f));
        BuildRockCluster(parent, rockMat, new Vector3(12f, 0f, -16f));

        BuildDeadTree(parent, treeMat, new Vector3(-10f, 0f, -12f));
        BuildDeadTree(parent, treeMat, new Vector3(16f, 0f, 8f));
        BuildDeadTree(parent, treeMat, new Vector3(-18f, 0f, -4f));
        BuildDeadTree(parent, treeMat, new Vector3(6f, 0f, 18f));
        BuildDeadTree(parent, treeMat, new Vector3(-4f, 0f, 16f));

        BuildCrateStack(parent, crateMat, new Vector3(11f, 0f, 4f));
        BuildCrateStack(parent, crateMat, new Vector3(-8f, 0f, -18f));
    }

    private static void BuildRockCluster(Transform parent, Material mat, Vector3 center)
    {
        var cluster = new GameObject("RockCluster");
        cluster.transform.SetParent(parent, false);
        cluster.transform.position = center;

        CreateSolidPart(PrimitiveType.Sphere, cluster.transform, mat, "Rock1", new Vector3(0f, 0.5f, 0f), new Vector3(1.6f, 1f, 1.4f));
        CreateSolidPart(PrimitiveType.Sphere, cluster.transform, mat, "Rock2", new Vector3(1.2f, 0.35f, 0.6f), new Vector3(1f, 0.7f, 0.9f));
        CreateSolidPart(PrimitiveType.Sphere, cluster.transform, mat, "Rock3", new Vector3(-1f, 0.3f, -0.8f), new Vector3(0.9f, 0.6f, 1f));
    }

    private static void BuildDeadTree(Transform parent, Material mat, Vector3 position)
    {
        var tree = new GameObject("DeadTree");
        tree.transform.SetParent(parent, false);
        tree.transform.position = position;

        CreateSolidPart(PrimitiveType.Cylinder, tree.transform, mat, "Trunk", new Vector3(0f, 2.5f, 0f), new Vector3(0.35f, 2.5f, 0.35f));
        CreateSolidPart(PrimitiveType.Cylinder, tree.transform, mat, "BranchA", new Vector3(0.4f, 4.3f, 0f), new Vector3(0.12f, 0.9f, 0.12f), new Vector3(0f, 0f, 55f));
        CreateSolidPart(PrimitiveType.Cylinder, tree.transform, mat, "BranchB", new Vector3(-0.35f, 4.6f, 0.2f), new Vector3(0.1f, 0.7f, 0.1f), new Vector3(20f, 0f, -50f));
    }

    private static void BuildCrateStack(Transform parent, Material mat, Vector3 position)
    {
        var stack = new GameObject("CrateStack");
        stack.transform.SetParent(parent, false);
        stack.transform.position = position;

        CreateSolidPart(PrimitiveType.Cube, stack.transform, mat, "CrateA", new Vector3(0f, 0.5f, 0f), Vector3.one);
        CreateSolidPart(PrimitiveType.Cube, stack.transform, mat, "CrateB", new Vector3(1.1f, 0.5f, 0.3f), Vector3.one);
        CreateSolidPart(PrimitiveType.Cube, stack.transform, mat, "CrateC", new Vector3(0.5f, 1.5f, 0.1f), Vector3.one);
    }

    // Unlike CreatePart (used for humanoid/sword visuals, where a Collider would fight the
    // character's own CharacterController), obstacle geometry keeps its default primitive
    // Collider so it actually blocks movement.
    private static GameObject CreateSolidPart(
        PrimitiveType type, Transform parent, Material material, string name,
        Vector3 localPosition, Vector3 localScale, Vector3? localEuler = null)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.transform.localRotation = localEuler.HasValue ? Quaternion.Euler(localEuler.Value) : Quaternion.identity;

        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = material;

        return part;
    }

    // Dim, cool-toned lighting + distance fog for a horror mood — replaces Unity's bright default
    // scene lighting. Idempotent by nature (just assigns values), so no "already present" guard
    // needed like the object-creation methods above.
    private static void SetupAmbiance()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = new Color(0.09f, 0.1f, 0.13f);
        RenderSettings.fogDensity = 0.008f;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.16f, 0.17f, 0.21f);

        // Moonlight, not sunlight — low intensity, pale cool tint.
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        foreach (Light light in lights)
        {
            if (light.type != LightType.Directional) continue;

            light.color = new Color(0.65f, 0.7f, 0.85f);
            light.intensity = 0.45f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(25f, -130f, 0f);
            break;
        }

        SetupNightSkybox();
    }

    // Dark procedural skybox (low exposure, near-black ground/sky tint, tiny sun disc) instead of
    // Unity's bright default daytime skybox. Must be a real persisted .mat asset before assigning
    // to RenderSettings.skybox — same reasoning as PersistRuntimeMaterials below: a Material that
    // was never saved to disk can't survive being referenced from serialized scene/asset data.
    private static void SetupNightSkybox()
    {
        const string path = "Assets/Art/Environment/NightSkybox.mat";

        Material sky = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (sky == null)
        {
            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader == null) return;

            sky = new Material(skyShader) { name = "NightSkybox" };
            sky.SetFloat("_SunSize", 0.01f);
            sky.SetColor("_SkyTint", new Color(0.04f, 0.05f, 0.09f));
            sky.SetColor("_GroundColor", new Color(0.02f, 0.02f, 0.03f));
            sky.SetFloat("_Exposure", 0.35f);
            sky.SetFloat("_AtmosphereThickness", 0.7f);

            if (!AssetDatabase.IsValidFolder("Assets/Art/Environment"))
                AssetDatabase.CreateFolder("Assets/Art", "Environment");

            AssetDatabase.CreateAsset(sky, path);
        }

        RenderSettings.skybox = sky;
    }

    // Global URP Volume with a subtle vignette + desaturation/underexposure for horror mood.
    // Guarded defensively (missing Camera.main, etc. just skip rather than throw) since this
    // runs as part of the one-shot scaffold tool and shouldn't be able to abort the rest of it.
    private static void SetupPostProcessing()
    {
        const string profileDir = "Assets/Art/Environment";
        const string profilePath = profileDir + "/HorrorPostProcess.asset";

        if (GameObject.Find("HorrorPostProcessVolume") == null)
        {
            var volumeGO = new GameObject("HorrorPostProcessVolume");
            Volume volume = volumeGO.AddComponent<Volume>();
            volume.isGlobal = true;

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();

                var vignette = profile.Add<Vignette>(true);
                vignette.intensity.overrideState = true;
                vignette.intensity.value = 0.35f;
                vignette.smoothness.overrideState = true;
                vignette.smoothness.value = 0.6f;
                vignette.color.overrideState = true;
                vignette.color.value = Color.black;

                var colorAdjustments = profile.Add<ColorAdjustments>(true);
                colorAdjustments.saturation.overrideState = true;
                colorAdjustments.saturation.value = -15f;
                colorAdjustments.postExposure.overrideState = true;
                colorAdjustments.postExposure.value = -0.15f;
                colorAdjustments.contrast.overrideState = true;
                colorAdjustments.contrast.value = 6f;

                if (!AssetDatabase.IsValidFolder(profileDir))
                    AssetDatabase.CreateFolder("Assets/Art", "Environment");

                AssetDatabase.CreateAsset(profile, profilePath);
            }

            volume.sharedProfile = profile;
        }

        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        UniversalAdditionalCameraData camData = mainCam.GetComponent<UniversalAdditionalCameraData>();
        if (camData == null)
            camData = mainCam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;
    }

    private const string CharactersDir = "Assets/Art/Characters";

    // Mixamo's "Without Skin" export leaves every material a plain white RGBA(1,1,1,1) with no
    // diffuse texture. These downloaded FBX files have no skin, so we assign flat placeholder
    // colors per material slot instead — combined with the models' own sculpted mesh geometry
    // (face, hair strands, Zombie Girl's decay patches) this reads far better than plain white.
    private static void AssignCharacterPlaceholderMaterials()
    {
        var survivorPalette = new Dictionary<string, Color>
        {
            { "Bodymat", new Color(0.92f, 0.75f, 0.64f) },
            { "Hairmat", new Color(0.25f, 0.15f, 0.1f) },
            { "Eyelashmat", new Color(0.05f, 0.05f, 0.05f) },
            { "Topmat", new Color(0.55f, 0.16f, 0.2f) },
            { "Bottommat", new Color(0.22f, 0.24f, 0.3f) },
            { "Shoesmat", new Color(0.12f, 0.09f, 0.07f) },
        };

        var killerPalette = new Dictionary<string, Color>
        {
            { "ZombieGirl_body_Material", new Color(0.6f, 0.66f, 0.56f) },
            { "ZombieGirl_Material", new Color(0.12f, 0.1f, 0.12f) },
        };

        ApplyMaterialPalette($"{CharactersDir}/SurvivorMale.fbx", survivorPalette);
        ApplyMaterialPalette($"{CharactersDir}/ZombieGirl.fbx", killerPalette);
    }

    private static void ApplyMaterialPalette(string fbxPath, Dictionary<string, Color> palette)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        if (importer == null)
        {
            Debug.LogWarning($"Blood For Blood: model not found at {fbxPath}, skipping material palette.");
            return;
        }

        if (!Directory.Exists(CharacterMaterialsDir))
            Directory.CreateDirectory(CharacterMaterialsDir);

        var seen = new HashSet<string>();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (asset is not Material mat || !seen.Add(mat.name))
                continue;

            if (!palette.TryGetValue(mat.name, out Color color))
                continue;

            string matPath = $"{CharacterMaterialsDir}/{Path.GetFileNameWithoutExtension(fbxPath)}_{mat.name}.mat";
            Material placeholder = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (placeholder == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                placeholder = new Material(shader) { color = color };
                AssetDatabase.CreateAsset(placeholder, matPath);
            }
            else
            {
                placeholder.color = color;
            }

            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), mat.name), placeholder);
        }

        importer.SaveAndReimport();
    }

    private static GameObject CreateSurvivorPrefab()
    {
        // SurvivorMale.fbx imports ~4.15 units tall natively (measured via Unity_RunCommand bounds
        // check) — 0.446 brings it to ~1.85 units, matching the CharacterController's ~2-unit height.
        return CreateRolePrefab<SurvivorController>(
            "Survivor",
            $"{CharactersDir}/SurvivorMale.fbx",
            Vector3.one * 0.446f,
            skinColor: new Color(0.92f, 0.75f, 0.64f),
            outfitColor: new Color(0.55f, 0.16f, 0.2f),
            hairColor: new Color(0.25f, 0.15f, 0.1f),
            isKiller: false);
    }

    private static GameObject CreateKillerPrefab()
    {
        // ZombieGirl.fbx imports ~2.14 units tall natively (measured via Unity_RunCommand bounds
        // check) — 0.96 brings it to ~2.05 units, slightly taller than Survivor.
        return CreateRolePrefab<KillerController>(
            "Killer",
            $"{CharactersDir}/ZombieGirl.fbx",
            Vector3.one * 0.96f,
            skinColor: new Color(0.72f, 0.7f, 0.68f),
            outfitColor: new Color(0.12f, 0.1f, 0.12f),
            hairColor: new Color(0.05f, 0.05f, 0.05f),
            isKiller: true);
    }

    private static GameObject CreateRolePrefab<TController>(
        string prefabName, string modelPath, Vector3 visualScale,
        Color skinColor, Color outfitColor, Color hairColor, bool isKiller)
        where TController : NetworkedCharacterMotor
    {
        if (!Directory.Exists(PrefabDir))
            Directory.CreateDirectory(PrefabDir);

        string prefabPath = $"{PrefabDir}/{prefabName}.prefab";

        var root = new GameObject(prefabName);
        root.transform.position = new Vector3(0f, 1f, 0f);
        root.AddComponent<CharacterController>();
        root.AddComponent<NetworkObject>();
        root.AddComponent<OwnerNetworkTransform>();
        root.AddComponent<TController>();

        GameObject visual = BuildModelVisual(root.transform, modelPath, prefabName);
        if (visual == null)
        {
            Debug.LogWarning($"Blood For Blood: model not found at {modelPath}, using primitive placeholder for {prefabName}.");
            visual = BuildHumanoidVisual(root.transform, skinColor, outfitColor, hairColor, isKiller);
        }
        visual.transform.localScale = visualScale;

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    private static GameObject BuildModelVisual(Transform parent, string modelPath, string prefabName)
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (modelAsset == null)
            return null;

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        visual.name = "Visual";
        visual.transform.SetParent(parent, false);
        // Mixamo characters are exported with their root at the feet; CharacterController's
        // collision capsule bottom sits at local y = -1 (default height 2, center 0), so align to that.
        visual.transform.localPosition = new Vector3(0f, -1f, 0f);

        Animator animator = visual.GetComponent<Animator>();
        if (animator == null)
            animator = visual.AddComponent<Animator>();

        // The Mixamo Walking/Running clips carry root motion (the hip bone's own forward
        // translation). With Apply Root Motion on (Animator's default), that fights
        // CharacterController.Move() in NetworkedCharacterMotor — both drive the transform at
        // once, causing erratic jumps. Movement must come from CharacterController alone; the
        // Animator should only ever pose bones, never move the root.
        animator.applyRootMotion = false;

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorControllerPath);
        if (controller != null)
            animator.runtimeAnimatorController = controller;

        return visual;
    }

    private static void CreateOrUpdateCharacterAnimatorController()
    {
        // Mixamo FBX imports don't enable Loop Time by default — without it, each clip plays
        // through once and freezes on its last frame instead of cycling (very noticeable on the
        // short Running clip, less obviously wrong but still broken on the long Idle clip).
        SetClipLooping($"{AnimationsDir}/Idle.fbx");
        SetClipLooping($"{AnimationsDir}/Walking.fbx");
        SetClipLooping($"{AnimationsDir}/Running.fbx");

        AnimationClip idleClip = LoadNamedClip($"{AnimationsDir}/Idle.fbx");
        AnimationClip walkClip = LoadNamedClip($"{AnimationsDir}/Walking.fbx");
        AnimationClip runClip = LoadNamedClip($"{AnimationsDir}/Running.fbx");
        if (idleClip == null || walkClip == null || runClip == null)
        {
            Debug.LogWarning("Blood For Blood: Idle/Walking/Running animation clips not found under " +
                $"{AnimationsDir} — skipping Animator Controller setup.");
            return;
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorControllerPath);

        if (System.Array.FindIndex(controller.parameters, p => p.name == "Speed") < 0)
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState existing in stateMachine.states)
            stateMachine.RemoveState(existing.state);

        var blendTree = new BlendTree { name = "Locomotion", blendType = BlendTreeType.Simple1D, blendParameter = "Speed" };
        AssetDatabase.AddObjectToAsset(blendTree, controller);
        // useAutomaticThresholds defaults to true and silently normalizes children to evenly
        // spaced [0,1] thresholds, discarding the explicit ones passed to AddChild below — must
        // be disabled for the real speed values (walkSpeed/sprintSpeed) to matter.
        blendTree.useAutomaticThresholds = false;
        blendTree.AddChild(idleClip, 0f);
        blendTree.AddChild(walkClip, 3.5f);
        blendTree.AddChild(runClip, 5.5f);

        AnimatorState locomotionState = stateMachine.AddState("Locomotion");
        locomotionState.motion = blendTree;
        stateMachine.defaultState = locomotionState;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    private static void SetClipLooping(string fbxPath)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        if (importer == null) return;

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].loopTime = true;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    private static AnimationClip LoadNamedClip(string fbxPath)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                return clip;
        }
        return null;
    }

    private static GameObject BuildHumanoidVisual(Transform parent, Color skinColor, Color outfitColor, Color hairColor, bool isKiller)
    {
        var visual = new GameObject("Visual");
        visual.transform.SetParent(parent, false);

        Material skinMat = CreateColorMaterial("SkinMat", skinColor);
        Material outfitMat = CreateColorMaterial("OutfitMat", outfitColor);
        Material hairMat = CreateColorMaterial("HairMat", hairColor);

        CreatePart(PrimitiveType.Sphere, visual.transform, skinMat, "Head",
            new Vector3(0f, 0.78f, 0f), new Vector3(0.36f, 0.36f, 0.36f));

        CreatePart(PrimitiveType.Capsule, visual.transform, hairMat, "Hair",
            new Vector3(0f, 0.6f, -0.08f), new Vector3(0.18f, 0.22f, 0.18f), new Vector3(15f, 0f, 0f));

        CreatePart(PrimitiveType.Capsule, visual.transform, outfitMat, "Torso",
            new Vector3(0f, 0.45f, 0f), new Vector3(0.44f, 0.3f, 0.3f));

        CreatePart(PrimitiveType.Cylinder, visual.transform, outfitMat, "Skirt",
            new Vector3(0f, 0.16f, 0f), new Vector3(0.56f, 0.12f, 0.56f));

        CreatePart(PrimitiveType.Capsule, visual.transform, skinMat, "ArmLeft",
            new Vector3(-0.3f, 0.35f, 0f), new Vector3(0.14f, 0.26f, 0.14f));
        CreatePart(PrimitiveType.Capsule, visual.transform, skinMat, "ArmRight",
            new Vector3(0.3f, 0.35f, 0f), new Vector3(0.14f, 0.26f, 0.14f));

        CreatePart(PrimitiveType.Capsule, visual.transform, skinMat, "LegLeft",
            new Vector3(-0.12f, -0.5f, 0f), new Vector3(0.17f, 0.45f, 0.17f));
        CreatePart(PrimitiveType.Capsule, visual.transform, skinMat, "LegRight",
            new Vector3(0.12f, -0.5f, 0f), new Vector3(0.17f, 0.45f, 0.17f));

        if (isKiller)
        {
            Material maskMat = CreateColorMaterial("MaskMat", new Color(0.9f, 0.88f, 0.85f));
            CreatePart(PrimitiveType.Cube, visual.transform, maskMat, "Mask",
                new Vector3(0f, 0.78f, 0.16f), new Vector3(0.3f, 0.32f, 0.08f));
        }

        return visual;
    }

    private static GameObject CreatePart(
        PrimitiveType type, Transform parent, Material material, string name,
        Vector3 localPos, Vector3 localScale, Vector3? localEuler = null)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        part.transform.localScale = localScale;
        part.transform.localRotation = localEuler.HasValue ? Quaternion.Euler(localEuler.Value) : Quaternion.identity;

        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);

        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;

        return part;
    }

    private static Material CreateColorMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { name = name, color = color };
        return mat;
    }

    private static GameObject CreateWeaponPickupPrefab()
    {
        if (!Directory.Exists(PrefabDir))
            Directory.CreateDirectory(PrefabDir);

        string prefabPath = $"{PrefabDir}/WeaponPickup.prefab";

        // NetworkObject is enough — NGO auto-registers it into Assets/DefaultNetworkPrefabs.asset.
        // Do not add an explicit NetworkPrefabsList entry (see CreateOrUpdateNetworkManager below).
        var root = new GameObject("WeaponPickup");
        root.AddComponent<NetworkObject>();

        SphereCollider trigger = root.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 1.5f;

        // Kinematic Rigidbody ensures OnTrigger callbacks fire reliably against the Survivor's CharacterController.
        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        root.AddComponent<WeaponPickup>();

        BuildSwordVisual(root.transform);

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    private static void BuildSwordVisual(Transform parent)
    {
        GameObject visual = SwordVisualBuilder.Build(parent, "Visual");
        visual.AddComponent<PickupVisualSpin>();
        PersistRuntimeMaterials(visual);
    }

    // SwordVisualBuilder normally creates cheap in-memory Materials (new Material(shader)), which
    // is fine when it's called at runtime (NetworkedCharacterMotor.AttachSwordToRightHand) — the
    // object and its materials just live for as long as the GameObject does. But WeaponPickup.prefab
    // is a real saved asset: PrefabUtility.SaveAsPrefabAsset can't persist a reference to a Material
    // that was never itself saved to disk, so the reference silently comes back null after the
    // save — which Unity renders as its default magenta "missing material" fallback. Swap each
    // renderer's material for a real persisted .mat asset (reused by name across re-runs) before
    // the prefab gets saved.
    private static void PersistRuntimeMaterials(GameObject root)
    {
        const string materialsDir = "Assets/Art/Weapons/Materials";
        if (!AssetDatabase.IsValidFolder("Assets/Art/Weapons"))
            AssetDatabase.CreateFolder("Assets/Art", "Weapons");
        if (!AssetDatabase.IsValidFolder(materialsDir))
            AssetDatabase.CreateFolder("Assets/Art/Weapons", "Materials");

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material runtimeMat = renderer.sharedMaterial;
            if (runtimeMat == null) continue;

            string assetPath = $"{materialsDir}/{runtimeMat.name}.mat";
            Material persisted = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (persisted == null)
            {
                persisted = new Material(runtimeMat);
                AssetDatabase.CreateAsset(persisted, assetPath);
            }

            renderer.sharedMaterial = persisted;
        }
    }

    private static void PlaceWeaponPickupInScene(GameObject prefab)
    {
        if (GameObject.Find("WeaponPickup") != null)
        {
            Debug.Log("Blood For Blood: WeaponPickup already present in scene, skipping placement.");
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = new Vector3(3f, 1f, 5f);
    }

    // Second in-scene-placed NetworkObject (after WeaponPickup) — pure logic, no collider/visual
    // needed. See MatchManager.cs for why it must live in the scene rather than spawn dynamically:
    // it needs to exist and start listening for connections before any player object does.
    private static GameObject CreateMatchManagerPrefab()
    {
        if (!Directory.Exists(PrefabDir))
            Directory.CreateDirectory(PrefabDir);

        string prefabPath = $"{PrefabDir}/MatchManager.prefab";

        var root = new GameObject("MatchManager");
        root.AddComponent<NetworkObject>();
        root.AddComponent<MatchManager>();

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    private static void PlaceMatchManagerInScene(GameObject prefab)
    {
        if (GameObject.Find("MatchManager") != null)
        {
            Debug.Log("Blood For Blood: MatchManager already present in scene, skipping placement.");
            return;
        }

        PrefabUtility.InstantiatePrefab(prefab);
    }

    // Boot/loading flow: Assets/Scenes/Boot.unity becomes Build Settings scene 0 (SampleScene
    // becomes scene 1) and shows Agape Forge -> "Blood For Blood" -> a menu with a Play button
    // that loads SampleScene. Built additively alongside whatever scene is currently open (rather
    // than via EditorSceneManager.NewScene(..., Single), which would replace it) so this never
    // disrupts an in-progress editing session on SampleScene — the additive scene is closed again
    // once saved, leaving the editor's open scenes exactly as they were before this ran.
    private static void CreateBootScene()
    {
        const string bootScenePath = "Assets/Scenes/Boot.unity";

        if (File.Exists(bootScenePath))
        {
            Debug.Log("Blood For Blood: Boot scene already present, skipping creation.");
            ConfigureBuildScenes(bootScenePath);
            return;
        }

        Scene originalActiveScene = EditorSceneManager.GetActiveScene();

        Scene bootScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(bootScene);

        // NewSceneSetup.EmptyScene means truly empty — no Camera, no Light. A ScreenSpaceOverlay
        // Canvas doesn't strictly need a Camera to render its own UI, but with zero cameras in the
        // scene the Game view has nothing to render to at all ("Display 1 No cameras rendering").
        var cameraGO = new GameObject("Main Camera");
        cameraGO.tag = "MainCamera";
        Camera bootCamera = cameraGO.AddComponent<Camera>();
        bootCamera.clearFlags = CameraClearFlags.SolidColor;
        bootCamera.backgroundColor = Color.black;

        // GraphicRaycaster (below, on the Canvas) needs an EventSystem in the scene to route
        // clicks to UI at all — without one, the Play button would render but never respond to
        // input. InputSystemUIInputModule (not the legacy StandaloneInputModule) since this
        // project runs exclusively on the new Input System (see CLAUDE.md Gotchas).
        var eventSystemGO = new GameObject("EventSystem");
        eventSystemGO.AddComponent<EventSystem>();
        eventSystemGO.AddComponent<InputSystemUIInputModule>();

        var canvasGO = new GameObject("BootCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGO.AddComponent<GraphicRaycaster>();

        var background = new GameObject("Background", typeof(Image));
        background.transform.SetParent(canvasGO.transform, false);
        RectTransform bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        background.GetComponent<Image>().color = Color.black;

        GameObject studioScreen = BuildTextScreen(canvasGO.transform, "StudioScreen", "AGAPE FORGE", 64, Color.white);
        GameObject titleScreen = BuildTextScreen(canvasGO.transform, "TitleScreen", "BLOOD FOR BLOOD", 96, new Color(0.8f, 0.1f, 0.1f));
        GameObject menuScreen = BuildMenuScreen(canvasGO.transform);

        var controllerGO = new GameObject("BootSequenceController");
        BootSequenceController controller = controllerGO.AddComponent<BootSequenceController>();
        controller.Configure(studioScreen, titleScreen, menuScreen);

        Button playButton = menuScreen.GetComponentInChildren<Button>(true);
        playButton.onClick.AddListener(controller.OnPlayPressed);

        if (!Directory.Exists("Assets/Scenes"))
            Directory.CreateDirectory("Assets/Scenes");

        EditorSceneManager.SaveScene(bootScene, bootScenePath);

        EditorSceneManager.SetActiveScene(originalActiveScene);
        EditorSceneManager.CloseScene(bootScene, true);

        ConfigureBuildScenes(bootScenePath);
    }

    private static GameObject BuildTextScreen(Transform parent, string name, string message, int fontSize, Color color)
    {
        var screen = new GameObject(name);
        screen.transform.SetParent(parent, false);
        RectTransform screenRect = screen.AddComponent<RectTransform>();
        screenRect.anchorMin = Vector2.zero;
        screenRect.anchorMax = Vector2.one;
        screenRect.offsetMin = Vector2.zero;
        screenRect.offsetMax = Vector2.zero;

        var textGO = new GameObject("Text", typeof(Text));
        textGO.transform.SetParent(screen.transform, false);
        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(1700f, 300f);

        Text text = textGO.GetComponent<Text>();
        text.text = message;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;

        screen.SetActive(false);
        return screen;
    }

    private static GameObject BuildMenuScreen(Transform parent)
    {
        var screen = new GameObject("MenuScreen");
        screen.transform.SetParent(parent, false);
        RectTransform screenRect = screen.AddComponent<RectTransform>();
        screenRect.anchorMin = Vector2.zero;
        screenRect.anchorMax = Vector2.one;
        screenRect.offsetMin = Vector2.zero;
        screenRect.offsetMax = Vector2.zero;

        var titleGO = new GameObject("Title", typeof(Text));
        titleGO.transform.SetParent(screen.transform, false);
        RectTransform titleRect = titleGO.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.68f);
        titleRect.anchorMax = new Vector2(0.5f, 0.68f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.sizeDelta = new Vector2(1500f, 200f);
        Text title = titleGO.GetComponent<Text>();
        title.text = "BLOOD FOR BLOOD";
        title.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        title.fontSize = 60;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = new Color(0.8f, 0.1f, 0.1f);

        var buttonGO = new GameObject("PlayButton", typeof(Image), typeof(Button));
        buttonGO.transform.SetParent(screen.transform, false);
        RectTransform buttonRect = buttonGO.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.4f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.4f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(320f, 84f);
        buttonGO.GetComponent<Image>().color = new Color(0.5f, 0.1f, 0.1f);

        var buttonTextGO = new GameObject("Text", typeof(Text));
        buttonTextGO.transform.SetParent(buttonGO.transform, false);
        RectTransform buttonTextRect = buttonTextGO.GetComponent<RectTransform>();
        buttonTextRect.anchorMin = Vector2.zero;
        buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.offsetMin = Vector2.zero;
        buttonTextRect.offsetMax = Vector2.zero;
        Text buttonText = buttonTextGO.GetComponent<Text>();
        buttonText.text = "PLAY";
        buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        buttonText.fontSize = 36;
        buttonText.fontStyle = FontStyle.Bold;
        buttonText.alignment = TextAnchor.MiddleCenter;
        buttonText.color = Color.white;

        screen.SetActive(false);
        return screen;
    }

    private static void ConfigureBuildScenes(string bootScenePath)
    {
        const string gameplayScenePath = "Assets/Scenes/SampleScene.unity";

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(bootScenePath, true),
            new EditorBuildSettingsScene(gameplayScenePath, true)
        };
    }

    private static void CreateOrUpdateNetworkManager(GameObject survivorPrefab, GameObject killerPrefab)
    {
        NetworkManager existing = Object.FindAnyObjectByType<NetworkManager>();
        GameObject nmGO = existing != null ? existing.gameObject : null;

        if (nmGO == null)
        {
            nmGO = new GameObject("NetworkManager");
            NetworkManager newManager = nmGO.AddComponent<NetworkManager>();
            UnityTransport transport = nmGO.AddComponent<UnityTransport>();

            newManager.NetworkConfig.NetworkTransport = transport;
            transport.ConnectionData.Address = "127.0.0.1";
            transport.ConnectionData.Port = 7777;
        }

        NetworkManager manager = nmGO.GetComponent<NetworkManager>();
        manager.NetworkConfig.PlayerPrefab = survivorPrefab;
        manager.NetworkConfig.ConnectionApproval = true;

        // Any NetworkObject-bearing prefab (Survivor, Killer) is auto-registered by NGO into
        // Assets/DefaultNetworkPrefabs.asset, which is auto-wired into NetworkPrefabsLists already —
        // do not add a second explicit list here, it duplicates the same hash and NGO errors on Awake.
        // Clean up any stale/missing list reference left over from a prior misconfiguration.
        manager.NetworkConfig.Prefabs.NetworkPrefabsLists.RemoveAll(l => l == null);

        if (nmGO.GetComponent<NetworkBootstrapUI>() == null)
            nmGO.AddComponent<NetworkBootstrapUI>();

        RoleAssignmentManager roleAssigner = nmGO.GetComponent<RoleAssignmentManager>();
        if (roleAssigner == null)
            roleAssigner = nmGO.AddComponent<RoleAssignmentManager>();
        roleAssigner.Configure(survivorPrefab, killerPrefab);

        EditorUtility.SetDirty(nmGO);
    }
}

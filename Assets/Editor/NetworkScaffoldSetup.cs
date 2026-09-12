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
        HospitalMapBuilder.Build();
        SetupAmbiance();
        SetupPostProcessing();
        GameObject survivorPrefab = CreateSurvivorPrefab();
        GameObject killerPrefab = CreateKillerPrefab();
        CreateOrUpdateNetworkManager(survivorPrefab, killerPrefab);

        GameObject weaponPickupPrefab = CreateWeaponPickupPrefab();
        GameObject matchManagerPrefab = CreateMatchManagerPrefab();
        PlaceMatchManagerInScene(matchManagerPrefab);
        GameObject restoreBeaconPrefab = CreateRestoreBeaconPrefab();

        // Beacon/pickup candidate spawn points live in the map builder (8 + 6; MatchManager keeps
        // 4 + 3 per match).
        HospitalMapBuilder.PlaceObjectives(restoreBeaconPrefab, weaponPickupPrefab);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        CreateBootScene();
        SetPlayModeStartScene();

        Debug.Log("Blood For Blood: network scaffolding created (Ground + NetworkManager + Survivor/Killer prefabs + WeaponPickup + MatchManager + RestoreBeacon x4 + Boot scene).");
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

    // Unlike CreatePart (used for humanoid/sword visuals, where a Collider would fight the
    // character's own CharacterController), obstacle geometry keeps its default primitive
    // Collider so it actually blocks movement.
    internal static GameObject CreateSolidPart(
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
        // Indoors now (roofed hospital): denser, darker fog so corridors fall off into black, and a
        // low ambient so the flickering fixtures are what actually lights the place.
        RenderSettings.fogColor = new Color(0.03f, 0.035f, 0.05f);
        RenderSettings.fogDensity = 0.018f;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.11f, 0.12f, 0.16f);

        // The hospital is lit by ~40 small fixtures; URP's default of 4 additional lights per
        // object leaves large surfaces black wherever the 5th+ nearby light gets culled.
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
        {
            urp.maxAdditionalLightsCount = 8;
            urp.supportsHDR = true;
            EditorUtility.SetDirty(urp);
        }

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
    // The full "DBD grade": bloom on the fluorescent tubes/beacon beam, film grain, a touch of
    // chromatic aberration, crushed cool blacks and a heavy vignette. Adds/updates each override
    // in place on the existing profile asset, so re-running Setup() re-applies the tuning.
    private static void EnsureHorrorGrade(VolumeProfile profile)
    {
        if (profile == null) return;

        T Ensure<T>() where T : VolumeComponent => profile.TryGet(out T c) ? c : profile.Add<T>(true);

        Vignette vignette = Ensure<Vignette>();
        vignette.intensity.Override(0.48f);
        vignette.smoothness.Override(0.55f);
        vignette.color.Override(Color.black);

        ColorAdjustments color = Ensure<ColorAdjustments>();
        color.saturation.Override(-28f);
        color.contrast.Override(18f);
        color.postExposure.Override(0.3f);
        color.colorFilter.Override(new Color(0.86f, 0.94f, 1f));

        LiftGammaGain lgg = Ensure<LiftGammaGain>();
        lgg.lift.Override(new Vector4(0.94f, 0.97f, 1.02f, -0.04f));
        lgg.gamma.Override(new Vector4(0.95f, 0.98f, 1.03f, -0.02f));
        lgg.gain.Override(new Vector4(1f, 1f, 1.02f, 0f));

        Bloom bloom = Ensure<Bloom>();
        bloom.intensity.Override(0.9f);
        bloom.threshold.Override(0.95f);
        bloom.scatter.Override(0.75f);

        FilmGrain grain = Ensure<FilmGrain>();
        grain.type.Override(FilmGrainLookup.Medium2);
        grain.intensity.Override(0.4f);
        grain.response.Override(0.7f);

        ChromaticAberration ca = Ensure<ChromaticAberration>();
        ca.intensity.Override(0.18f);

        EditorUtility.SetDirty(profile);
    }

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

        EnsureHorrorGrade(AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath));

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

    internal static Material CreateColorMaterial(string name, Color color)
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
    internal static void PersistRuntimeMaterials(GameObject root, string materialsDir = "Assets/Art/Weapons/Materials")
    {
        if (!AssetDatabase.IsValidFolder(materialsDir))
        {
            string parent = Path.GetDirectoryName(materialsDir)?.Replace('\\', '/');
            string leaf = Path.GetFileName(materialsDir);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(parent)?.Replace('\\', '/'), Path.GetFileName(parent));
            AssetDatabase.CreateFolder(parent, leaf);
        }

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material runtimeMat = renderer.sharedMaterial;
            if (runtimeMat == null) continue;

            // Already a real asset (e.g. a character's remapped skin material, picked up because
            // it happens to sit under this root too) — leave it alone, don't clone a duplicate.
            if (AssetDatabase.Contains(runtimeMat)) continue;

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

    // The Restore Beacon objective: 4 in-scene NetworkObjects (same in-scene-placed pattern as
    // WeaponPickup/MatchManager — no NetworkPrefabsList entry needed) spread across the map
    // quadrants, clear of the spawn line, WeaponPickup, and the obstacle props. RestoreBeacon.cs
    // owns all the interaction/skill-check/beam logic; this just builds the placeholder pillar
    // visual (kept blocking via CreateSolidPart, matching the environment props) and the trigger.
    private static GameObject CreateRestoreBeaconPrefab()
    {
        if (!Directory.Exists(PrefabDir))
            Directory.CreateDirectory(PrefabDir);

        string prefabPath = $"{PrefabDir}/RestoreBeacon.prefab";

        var root = new GameObject("RestoreBeacon");
        root.AddComponent<NetworkObject>();

        SphereCollider trigger = root.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 2.5f;

        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        BuildBeaconVisual(root.transform);
        root.AddComponent<RestoreBeacon>();

        PersistRuntimeMaterials(root, "Assets/Art/Environment/Materials");

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    private static void BuildBeaconVisual(Transform parent)
    {
        Material stoneMat = CreateColorMaterial("BeaconStoneMat", new Color(0.3f, 0.28f, 0.26f));
        Material bowlMat = CreateColorMaterial("BeaconBowlMat", new Color(0.45f, 0.42f, 0.3f));

        CreateSolidPart(PrimitiveType.Cylinder, parent, stoneMat, "Post", new Vector3(0f, 1f, 0f), new Vector3(0.3f, 1f, 0.3f));
        CreateSolidPart(PrimitiveType.Sphere, parent, bowlMat, "Bowl", new Vector3(0f, 2f, 0f), new Vector3(0.6f, 0.35f, 0.6f));
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

        var environmentRoot = new GameObject("MenuEnvironment");
        BuildMenuEnvironment(environmentRoot.transform);

        // NewSceneSetup.EmptyScene means truly empty — no Camera, no Light. A ScreenSpaceOverlay
        // Canvas doesn't strictly need a Camera to render its own UI, but with zero cameras in the
        // scene the Game view has nothing to render to at all ("Display 1 No cameras rendering").
        // Framed on the showcase character built by BuildMenuEnvironment above.
        var cameraGO = new GameObject("Main Camera");
        cameraGO.tag = "MainCamera";
        Camera bootCamera = cameraGO.AddComponent<Camera>();
        bootCamera.clearFlags = CameraClearFlags.Skybox;
        cameraGO.transform.position = new Vector3(-1.6f, 1.75f, -1.6f);
        cameraGO.transform.rotation = Quaternion.Euler(6f, 32f, 0f);

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

        // Solid black full-screen cover for the studio/title cards only — BootSequenceController
        // hides this once the menu appears, revealing the 3D backdrop built above.
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
        controller.Configure(studioScreen, titleScreen, menuScreen, background);

        // Button.onClick.AddListener() from editor script code only registers a non-persistent
        // (runtime-only) delegate — UnityEvent's serialized "persistent calls" list is a separate
        // thing, and that's what actually survives a scene save. Without this, the click handler
        // silently vanishes on save/reload: the button still renders and is clickable, it just
        // does nothing when clicked. UnityEventTools.AddPersistentListener is the editor-time API
        // for actually baking a serialized call into the scene, same as wiring it up by hand in
        // the Inspector would.
        Button playButton = menuScreen.transform.Find("LeftPanel/PlayButton").GetComponent<Button>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(playButton.onClick, controller.OnPlayPressed);

        Button quitButton = menuScreen.transform.Find("LeftPanel/QuitButton").GetComponent<Button>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(quitButton.onClick, controller.OnQuitPressed);

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

    // DBD/Home Sweet Home-style layout: a semi-transparent left-side panel over a 3D backdrop
    // (see BuildMenuEnvironment) rather than a full-screen overlay — the showcase character stays
    // visible on the right of frame.
    private static GameObject BuildMenuScreen(Transform parent)
    {
        var screen = new GameObject("MenuScreen");
        screen.transform.SetParent(parent, false);
        RectTransform screenRect = screen.AddComponent<RectTransform>();
        screenRect.anchorMin = Vector2.zero;
        screenRect.anchorMax = Vector2.one;
        screenRect.offsetMin = Vector2.zero;
        screenRect.offsetMax = Vector2.zero;

        var panel = new GameObject("LeftPanel", typeof(Image));
        panel.transform.SetParent(screen.transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.sizeDelta = new Vector2(520f, 0f);
        panelRect.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        var titleGO = new GameObject("Title", typeof(Text));
        titleGO.transform.SetParent(panel.transform, false);
        RectTransform titleRect = titleGO.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -70f);
        titleRect.sizeDelta = new Vector2(480f, 160f);
        Text title = titleGO.GetComponent<Text>();
        title.text = "BLOOD\nFOR BLOOD";
        title.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        title.fontSize = 44;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.UpperCenter;
        title.color = new Color(0.82f, 0.12f, 0.12f);

        // SETTINGS is deliberately non-interactive (dimmed, Button.interactable = false) rather
        // than wired to a fake handler — a placeholder slot that reads as "not built yet" instead
        // of implying a feature that doesn't exist.
        BuildMenuButton(panel.transform, "PlayButton", "PLAY", new Vector2(0f, -280f), new Color(0.55f, 0.1f, 0.1f), true);
        BuildMenuButton(panel.transform, "SettingsButton", "SETTINGS", new Vector2(0f, -360f), new Color(0.2f, 0.2f, 0.22f), false);
        BuildMenuButton(panel.transform, "QuitButton", "QUIT", new Vector2(0f, -440f), new Color(0.3f, 0.12f, 0.12f), true);

        screen.SetActive(false);
        return screen;
    }

    private static void BuildMenuButton(Transform parent, string name, string label, Vector2 anchoredPosition, Color color, bool interactable)
    {
        var buttonGO = new GameObject(name, typeof(Image), typeof(Button));
        buttonGO.transform.SetParent(parent, false);
        RectTransform buttonRect = buttonGO.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 1f);
        buttonRect.anchorMax = new Vector2(0.5f, 1f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = anchoredPosition;
        buttonRect.sizeDelta = new Vector2(360f, 64f);
        buttonGO.GetComponent<Image>().color = color;
        buttonGO.GetComponent<Button>().interactable = interactable;

        var textGO = new GameObject("Text", typeof(Text));
        textGO.transform.SetParent(buttonGO.transform, false);
        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        Text text = textGO.GetComponent<Text>();
        text.text = label;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 28;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = interactable ? Color.white : new Color(1f, 1f, 1f, 0.4f);
    }

    // 3D backdrop for the menu screen: moonlit ground/fog/skybox (matching the gameplay scene's
    // ambiance), a couple of dead trees + a rock cluster for atmosphere, a warm point light for
    // contrast against the cool moonlight (a cheap stand-in for a campfire glow, no particle
    // system), and a static showcase character standing in view — same idea as Dead by Daylight's
    // and Home Sweet Home's character-in-the-lobby main menus.
    private static void BuildMenuEnvironment(Transform root)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = new Color(0.05f, 0.06f, 0.09f);
        RenderSettings.fogDensity = 0.03f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.13f, 0.17f);

        Material sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Environment/NightSkybox.mat");
        if (sky != null) RenderSettings.skybox = sky;

        var moonGO = new GameObject("MenuMoonlight");
        moonGO.transform.SetParent(root, false);
        Light moon = moonGO.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.6f, 0.65f, 0.85f);
        moon.intensity = 0.5f;
        moon.shadows = LightShadows.Soft;
        moonGO.transform.rotation = Quaternion.Euler(35f, -120f, 0f);

        var glowGO = new GameObject("MenuWarmGlow");
        glowGO.transform.SetParent(root, false);
        glowGO.transform.localPosition = new Vector3(0.6f, 1.3f, 2.6f);
        Light glow = glowGO.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.55f, 0.25f);
        glow.intensity = 3f;
        glow.range = 6f;

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "MenuGround";
        ground.transform.SetParent(root, false);
        ground.transform.localScale = new Vector3(3f, 1f, 3f);
        Material groundMat = CreateColorMaterial("MenuGroundMat", new Color(0.09f, 0.09f, 0.11f));
        ground.GetComponent<Renderer>().sharedMaterial = groundMat;

        Material rockMat = CreateColorMaterial("MenuRockMat", new Color(0.32f, 0.31f, 0.29f));
        Material treeMat = CreateColorMaterial("MenuTreeMat", new Color(0.13f, 0.11f, 0.09f));
        BuildDeadTree(root, treeMat, new Vector3(-3.2f, 0f, 2.2f));
        BuildDeadTree(root, treeMat, new Vector3(3.6f, 0f, 3.4f));
        BuildDeadTree(root, treeMat, new Vector3(-4.8f, 0f, -0.6f));
        BuildRockCluster(root, rockMat, new Vector3(2.2f, 0f, -1.6f));

        var showcaseRoot = new GameObject("MenuShowcase");
        showcaseRoot.transform.SetParent(root, false);
        showcaseRoot.transform.localPosition = new Vector3(1.2f, 1f, 3f);
        showcaseRoot.transform.localRotation = Quaternion.Euler(0f, 160f, 0f);

        GameObject showcaseVisual = BuildModelVisual(showcaseRoot.transform, $"{CharactersDir}/ZombieGirl.fbx", "MenuShowcase");
        if (showcaseVisual != null)
        {
            showcaseVisual.transform.localScale = Vector3.one * 0.96f;
        }

        // Everything above went through CreateColorMaterial, which returns a plain in-memory
        // Material — fine at pure runtime, but this scene gets saved to disk (SaveScene below in
        // CreateBootScene), and a Material with no asset on disk can't be serialized into a saved
        // scene either (same failure mode as WeaponPickup's prefab — see PersistRuntimeMaterials).
        // The showcase character's own materials are already real remapped assets and get skipped.
        PersistRuntimeMaterials(root.gameObject, "Assets/Art/Environment/Materials");
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

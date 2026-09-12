using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds Assets/Scenes/Boot.unity (Build Settings scene 0): studio splash -> title card -> lobby
// menu. Laid out after Home Sweet Home's lobby (the reference the user gave): a full-screen 3D
// backdrop with the Killer standing by a campfire in mist, a translucent bottom bar of menu items,
// a big QUICK PLAY call-to-action on the right, EXIT bottom-left and a player card top-right. The
// title and studio marks are rendered logo sprites (LogoRenderer), not plain Text. Built
// additively so it never replaces the scene the user has open (see CLAUDE.md); regenerate by
// deleting Boot.unity and re-running Setup.
internal static class BootSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Boot.unity";
    private const string GameplayScenePath = "Assets/Scenes/SampleScene.unity";
    private const string MaterialsDir = "Assets/Art/Environment/Materials";
    private static readonly Font UiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    private static readonly Vector3 Offset = new Vector3(0f, 300f, 0f);

    public static void Build()
    {
        if (File.Exists(ScenePath))
        {
            Debug.Log("Blood For Blood: Boot scene already present, skipping creation.");
            ConfigureBuildScenes();
            return;
        }

        Scene originalActiveScene = EditorSceneManager.GetActiveScene();
        Scene bootScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(bootScene);

        // Lifted 300 units so an additively-opened preview (or any future scene overlap) never
        // shows the hospital through the lobby — everything in here is positioned relative to it.
        var environmentRoot = new GameObject("MenuEnvironment");
        environmentRoot.transform.position = Offset;
        BuildEnvironment(environmentRoot.transform);

        var cameraGO = new GameObject("Main Camera");
        cameraGO.tag = "MainCamera";
        Camera cam = cameraGO.AddComponent<Camera>();
        // A camera built from an empty GameObject has no AudioListener (Unity's menu-created one
        // does) — and without any listener in the scene AudioSource.Play() silently never plays.
        cameraGO.AddComponent<AudioListener>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 50f;
        cameraGO.transform.position = Offset + new Vector3(-1.9f, 1.55f, -2.4f);
        cameraGO.transform.rotation = Quaternion.Euler(3f, 30f, 0f);

        var eventSystemGO = new GameObject("EventSystem");
        eventSystemGO.AddComponent<EventSystem>();
        eventSystemGO.AddComponent<InputSystemUIInputModule>();

        var musicGO = new GameObject("MenuMusic");
        LoopingAudio music = musicGO.AddComponent<LoopingAudio>();
        var musicSo = new SerializedObject(music);
        musicSo.FindProperty("clipName").stringValue = "MenuMusic";
        musicSo.FindProperty("volume").floatValue = 0.55f;
        musicSo.FindProperty("fadeInSeconds").floatValue = 3f;
        musicSo.ApplyModifiedPropertiesWithoutUndo();

        var canvasGO = new GameObject("BootCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGO.AddComponent<GraphicRaycaster>();

        GameObject background = FullScreenImage(canvasGO.transform, "Background", Color.black);

        Sprite studioLogo = LogoRenderer.StudioLogo();
        Sprite titleLogo = LogoRenderer.TitleLogo();
        GameObject studioScreen = LogoScreen(canvasGO.transform, "StudioScreen", studioLogo, "AGAPE FORGE", 64, Color.white, new Vector2(900f, 190f));
        GameObject titleScreen = LogoScreen(canvasGO.transform, "TitleScreen", titleLogo, "BLOOD FOR BLOOD", 96, new Color(0.8f, 0.1f, 0.1f), new Vector2(1250f, 390f));
        GameObject menuScreen = BuildMenuScreen(canvasGO.transform, titleLogo);

        var controllerGO = new GameObject("BootSequenceController");
        BootSequenceController controller = controllerGO.AddComponent<BootSequenceController>();
        controller.Configure(studioScreen, titleScreen, menuScreen, background);

        // Persistent listeners — a plain AddListener() from editor code doesn't survive the save.
        foreach (string playButton in new[] { "BottomBar/PlayButton", "QuickPlayButton" })
            UnityEditor.Events.UnityEventTools.AddPersistentListener(
                menuScreen.transform.Find(playButton).GetComponent<Button>().onClick, controller.OnPlayPressed);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            menuScreen.transform.Find("ExitButton").GetComponent<Button>().onClick, controller.OnQuitPressed);

        if (!Directory.Exists("Assets/Scenes")) Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(bootScene, ScenePath);
        EditorSceneManager.SetActiveScene(originalActiveScene);
        EditorSceneManager.CloseScene(bootScene, true);

        ConfigureBuildScenes();
    }

    private static void ConfigureBuildScenes()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(GameplayScenePath, true),
        };
    }

    // ---------------------------------------------------------------- 3D backdrop

    private static void BuildEnvironment(Transform root)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = new Color(0.05f, 0.07f, 0.09f);
        RenderSettings.fogDensity = 0.045f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.14f, 0.17f, 0.2f);
        Material sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Environment/NightSkybox.mat");
        if (sky != null) RenderSettings.skybox = sky;

        Material groundMat = HospitalMapBuilder.Textured("MenuGroundMat", ProceduralTextures.Concrete(), new Color(0.35f, 0.36f, 0.34f), 0.1f, 0f);
        Material stoneMat = HospitalMapBuilder.Textured("MenuStoneMat", ProceduralTextures.Concrete(), new Color(0.5f, 0.5f, 0.48f), 0.15f, 0f);
        Material barkMat = HospitalMapBuilder.Textured("MenuBarkMat", ProceduralTextures.Wood(), new Color(0.35f, 0.3f, 0.26f), 0.1f, 0f);
        Material fireMat = HospitalMapBuilder.Particle("MenuFireMat", ProceduralTextures.SoftParticle(), true);
        Material mistMat = HospitalMapBuilder.Particle("HospitalFogMat", ProceduralTextures.SoftParticle());
        Material dustMat = HospitalMapBuilder.Particle("HospitalDustMat", ProceduralTextures.SoftParticle());

        GameObject ground = NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, root, groundMat, "MenuGround", new Vector3(0f, -0.1f, 0f), new Vector3(60f, 0.2f, 60f));
        ground.AddComponent<AutoTile>();

        // Moonlight, a cool rim behind the character, and the warm campfire that carries the scene.
        var moon = new GameObject("MenuMoonlight");
        moon.transform.SetParent(root, false);
        Light moonLight = moon.AddComponent<Light>();
        moonLight.type = LightType.Directional;
        moonLight.color = new Color(0.55f, 0.65f, 0.85f);
        moonLight.intensity = 0.35f;
        moonLight.shadows = LightShadows.Soft;
        moon.transform.rotation = Quaternion.Euler(40f, -140f, 0f);

        PointLight(root, "MenuRimLight", new Vector3(3.5f, 2.6f, 6.5f), new Color(0.45f, 0.65f, 1f), 3.5f, 9f, false);
        Light fire = PointLight(root, "MenuCampfireLight", new Vector3(-0.8f, 0.9f, 3.9f), new Color(1f, 0.55f, 0.22f), 7f, 12f, true);

        // Campfire: stone ring, embers, flames and smoke.
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 2f / 8f;
            NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, root, stoneMat, "FireStone",
                fire.transform.localPosition + new Vector3(Mathf.Cos(a) * 0.55f, -0.8f, Mathf.Sin(a) * 0.55f), new Vector3(0.28f, 0.2f, 0.22f), new Vector3(0f, a * Mathf.Rad2Deg, 0f));
        }
        Vector3 fireBase = fire.transform.localPosition + new Vector3(0f, -0.85f, 0f);
        Emitter(root, "Flames", fireBase, new Vector3(0.35f, 0.05f, 0.35f), fireMat, 60, 0.25f, 0.55f, new Color(1f, 0.45f, 0.1f, 0.9f), 0.9f, 0.7f, 0.15f, true);
        Emitter(root, "Embers", fireBase, new Vector3(0.3f, 0.05f, 0.3f), fireMat, 25, 0.03f, 0.06f, new Color(1f, 0.6f, 0.2f, 1f), 1.4f, 1.8f, 0.5f, true);
        Emitter(root, "Smoke", fireBase + Vector3.up * 0.6f, new Vector3(0.4f, 0.1f, 0.4f), mistMat, 20, 0.8f, 1.6f, new Color(0.25f, 0.25f, 0.27f, 0.25f), 0.5f, 3.5f, 0.2f, false);

        // Mist and dust across the whole shot.
        Emitter(root, "Mist", new Vector3(0f, 0.6f, 4f), new Vector3(24f, 1f, 24f), mistMat, 50, 5f, 9f, new Color(0.6f, 0.7f, 0.8f, 0.07f), 0.15f, 12f, 0.05f, false);
        Emitter(root, "Dust", new Vector3(0f, 2f, 3f), new Vector3(14f, 4f, 14f), dustMat, 150, 0.02f, 0.05f, new Color(0.9f, 0.85f, 0.7f, 0.35f), 0.05f, 10f, 0.25f, false);

        foreach (var (x, z, s) in new[] { (-3.4f, 3.2f, 1f), (3.8f, 4.6f, 1.2f), (-5.2f, 0.4f, 0.9f), (6.5f, 1.5f, 1.1f), (-1.5f, 8.5f, 1.3f), (2.5f, 9f, 1f), (-7f, 6f, 1.2f), (8f, 7.5f, 0.9f) })
            DeadTree(root, barkMat, new Vector3(x, 0f, z), s);

        // Ruined stone pillars either side, like the reference's shrine columns.
        foreach (var (x, z, h) in new[] { (-4.6f, 6.2f, 3.2f), (4.9f, 7.4f, 2.6f), (-8f, 2.5f, 2.2f) })
        {
            GameObject pillar = NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, root, stoneMat, "Pillar", new Vector3(x, h * 0.5f, z), new Vector3(0.7f, h, 0.7f));
            pillar.AddComponent<AutoTile>();
            NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, root, stoneMat, "PillarCap", new Vector3(x, h + 0.12f, z), new Vector3(0.95f, 0.24f, 0.95f));
        }
        foreach (var (x, z) in new[] { (2.2f, 2.2f), (-2.8f, 5.5f), (5.5f, 3.2f), (-6f, 4.2f) })
            NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Sphere, root, stoneMat, "Rock", new Vector3(x, 0.22f, z), new Vector3(1.1f, 0.55f, 0.9f));

        // The Killer, lit by the fire, half-turned toward the camera.
        var showcaseRoot = new GameObject("MenuShowcase");
        showcaseRoot.transform.SetParent(root, false);
        showcaseRoot.transform.localPosition = new Vector3(0.9f, 1f, 2.9f);
        showcaseRoot.transform.localRotation = Quaternion.Euler(0f, 205f, 0f);
        GameObject showcase = NetworkScaffoldSetup.BuildModelVisual(showcaseRoot.transform, "Assets/Art/Characters/ZombieGirl.fbx", "MenuShowcase");
        if (showcase != null) showcase.transform.localScale = Vector3.one * 0.96f;

        NetworkScaffoldSetup.PersistRuntimeMaterials(root.gameObject, MaterialsDir);
    }

    private static void DeadTree(Transform root, Material mat, Vector3 pos, float scale)
    {
        var tree = new GameObject("DeadTree");
        tree.transform.SetParent(root, false);
        tree.transform.localPosition = pos;
        tree.transform.localScale = Vector3.one * scale;
        GameObject trunk = NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cylinder, tree.transform, mat, "Trunk", new Vector3(0f, 2.6f, 0f), new Vector3(0.3f, 2.6f, 0.3f));
        trunk.AddComponent<AutoTile>();
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cylinder, tree.transform, mat, "BranchA", new Vector3(0.45f, 4.4f, 0f), new Vector3(0.1f, 0.9f, 0.1f), new Vector3(0f, 0f, 55f));
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cylinder, tree.transform, mat, "BranchB", new Vector3(-0.35f, 4.7f, 0.2f), new Vector3(0.08f, 0.7f, 0.08f), new Vector3(20f, 0f, -50f));
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cylinder, tree.transform, mat, "BranchC", new Vector3(0.1f, 3.6f, -0.35f), new Vector3(0.07f, 0.6f, 0.07f), new Vector3(-55f, 0f, 10f));
    }

    private static Light PointLight(Transform root, string name, Vector3 pos, Color color, float intensity, float range, bool flicker)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        if (flicker) go.AddComponent<FlickerLight>();
        return l;
    }

    private static void Emitter(Transform parent, string name, Vector3 center, Vector3 size, Material mat, int max, float sizeMin, float sizeMax, Color color, float speed, float life, float noise, bool rising)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = life;
        main.startSpeed = speed;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = color;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = max / life;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = size;
        if (rising)
        {
            // All three axes must share a curve mode ("Particle Velocity curves must all be in the
            // same mode" spams every frame otherwise), so X/Z get an explicit two-constant zero.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(speed, speed * 1.6f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));
        }
        var n = ps.noise;
        n.enabled = true;
        n.strength = noise;
        n.frequency = rising ? 0.6f : 0.15f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(rising ? new Color(0.9f, 0.25f, 0.05f) : Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = gradient;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
    }

    // ---------------------------------------------------------------- UI

    private static GameObject FullScreenImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>());
        go.GetComponent<Image>().color = color;
        return go;
    }

    private static GameObject LogoScreen(Transform parent, string name, Sprite logo, string fallbackText, int fallbackSize, Color fallbackColor, Vector2 size)
    {
        var screen = new GameObject(name);
        screen.transform.SetParent(parent, false);
        Stretch(screen.AddComponent<RectTransform>());

        if (logo != null)
        {
            var img = new GameObject("Logo", typeof(Image));
            img.transform.SetParent(screen.transform, false);
            RectTransform r = img.GetComponent<RectTransform>();
            Center(r, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            Image image = img.GetComponent<Image>();
            image.sprite = logo;
            image.preserveAspect = true;
        }
        else
        {
            Label(screen.transform, "Text", fallbackText, fallbackSize, fallbackColor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1700f, 300f), TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        screen.SetActive(false);
        return screen;
    }

    private static GameObject BuildMenuScreen(Transform parent, Sprite titleLogo)
    {
        var screen = new GameObject("MenuScreen");
        screen.transform.SetParent(parent, false);
        Stretch(screen.AddComponent<RectTransform>());

        // Small title mark, top-left.
        if (titleLogo != null)
        {
            var mark = new GameObject("TitleMark", typeof(Image));
            mark.transform.SetParent(screen.transform, false);
            Center(mark.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(230f, -70f), new Vector2(380f, 118f));
            mark.GetComponent<Image>().sprite = titleLogo;
            mark.GetComponent<Image>().preserveAspect = true;
        }

        // Player card, top-right.
        var card = Panel(screen.transform, "PlayerCard", new Vector2(1f, 1f), new Vector2(-190f, -62f), new Vector2(330f, 84f), new Color(0f, 0f, 0f, 0.55f));
        var avatar = new GameObject("Avatar", typeof(Image));
        avatar.transform.SetParent(card.transform, false);
        Center(avatar.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(60f, 60f));
        avatar.GetComponent<Image>().color = new Color(0.55f, 0.08f, 0.08f);
        Label(card.transform, "Name", "SURVIVOR", 24, Color.white, new Vector2(0f, 1f), new Vector2(200f, -24f), new Vector2(240f, 30f), TextAnchor.MiddleLeft, FontStyle.Bold);
        Label(card.transform, "Level", "LEVEL 1   //   PROTOTYPE", 15, new Color(0.7f, 0.7f, 0.72f), new Vector2(0f, 0f), new Vector2(200f, 22f), new Vector2(240f, 24f), TextAnchor.MiddleLeft, FontStyle.Normal);

        // Big call-to-action, right side.
        GameObject quick = TextButton(screen.transform, "QuickPlayButton", "QUICK PLAY", 58, new Vector2(1f, 0.5f), new Vector2(-330f, -40f), new Vector2(620f, 90f), TextAnchor.MiddleRight, true);
        Label(screen.transform, "PlayMode", "PLAY MODE   HOST / JOIN (LAN)", 20, new Color(0.75f, 0.75f, 0.78f), new Vector2(1f, 0.5f), new Vector2(-330f, -100f), new Vector2(620f, 30f), TextAnchor.MiddleRight, FontStyle.Normal);
        Accent(quick.transform, new Vector2(-4f, -34f), new Vector2(200f, 3f), new Vector2(1f, 0.5f));

        // Bottom bar of menu items.
        var bar = Panel(screen.transform, "BottomBar", new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(1240f, 84f), new Color(0f, 0f, 0f, 0.62f));
        Accent(bar.transform, new Vector2(0f, 41f), new Vector2(1240f, 2f), new Vector2(0.5f, 0.5f));
        string[] items = { "PLAY", "CUSTOMIZE", "STORE", "PROFILE" };
        for (int i = 0; i < items.Length; i++)
        {
            float x = -465f + i * 310f;
            bool enabled = i == 0;
            TextButton(bar.transform, items[i] == "PLAY" ? "PlayButton" : items[i] + "Button", items[i], 30, new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(280f, 60f), TextAnchor.MiddleCenter, enabled);
            if (i < items.Length - 1)
            {
                var sep = new GameObject("Separator", typeof(Image));
                sep.transform.SetParent(bar.transform, false);
                Center(sep.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(x + 155f, 0f), new Vector2(2f, 40f));
                sep.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
            }
        }

        // Exit, bottom-left, with an "ESC" key cap like the reference.
        GameObject exit = TextButton(screen.transform, "ExitButton", "EXIT", 26, new Vector2(0f, 0f), new Vector2(230f, 40f), new Vector2(200f, 50f), TextAnchor.MiddleLeft, true);
        var key = Panel(exit.transform, "KeyCap", new Vector2(0f, 0.5f), new Vector2(-60f, 0f), new Vector2(54f, 30f), new Color(0.55f, 0.08f, 0.08f));
        Label(key.transform, "KeyText", "ESC", 15, Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(54f, 30f), TextAnchor.MiddleCenter, FontStyle.Bold);

        Label(screen.transform, "Version", "EARLY ACCESS VERSION 0.1.0", 14, new Color(0.6f, 0.6f, 0.62f), new Vector2(1f, 0f), new Vector2(-170f, 24f), new Vector2(300f, 20f), TextAnchor.MiddleRight, FontStyle.Normal);

        screen.SetActive(false);
        return screen;
    }

    private static GameObject Panel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        Center(go.GetComponent<RectTransform>(), anchor, pos, size);
        go.GetComponent<Image>().color = color;
        return go;
    }

    private static void Accent(Transform parent, Vector2 pos, Vector2 size, Vector2 anchor)
    {
        var go = new GameObject("Accent", typeof(Image));
        go.transform.SetParent(parent, false);
        Center(go.GetComponent<RectTransform>(), anchor, pos, size);
        go.GetComponent<Image>().color = new Color(0.75f, 0.1f, 0.1f, 0.95f);
    }

    private static GameObject TextButton(Transform parent, string name, string label, int size, Vector2 anchor, Vector2 pos, Vector2 rectSize, TextAnchor align, bool interactable)
    {
        var go = new GameObject(name, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Center(go.GetComponent<RectTransform>(), anchor, pos, rectSize);
        Image bg = go.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.001f);
        Button button = go.GetComponent<Button>();
        button.interactable = interactable;
        button.targetGraphic = bg;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.75f, 0.1f, 0.1f, 0.35f);
        colors.pressedColor = new Color(0.75f, 0.1f, 0.1f, 0.6f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0.001f);
        button.colors = colors;
        Label(go.transform, "Text", label, size, interactable ? Color.white : new Color(1f, 1f, 1f, 0.35f), new Vector2(0.5f, 0.5f), Vector2.zero, rectSize, align, FontStyle.Bold);
        return go;
    }

    private static Text Label(Transform parent, string name, string text, int size, Color color, Vector2 anchor, Vector2 pos, Vector2 rectSize, TextAnchor align, FontStyle style)
    {
        var go = new GameObject(name, typeof(Text));
        go.transform.SetParent(parent, false);
        Center(go.GetComponent<RectTransform>(), anchor, pos, rectSize);
        Text t = go.GetComponent<Text>();
        t.text = text;
        t.font = UiFont;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        // Faux letterspacing: rich text can't space letters, so pad with thin spaces for the big labels.
        if (size >= 26) t.text = string.Join(" ", text.ToCharArray());
        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return t;
    }

    private static void Center(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        r.anchorMin = anchor;
        r.anchorMax = anchor;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos;
        r.sizeDelta = size;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }
}

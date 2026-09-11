using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Boot flow for Assets/Scenes/Boot.unity (Build Settings scene 0, gameplay SampleScene is scene
// 1): studio splash ("Agape Forge") -> game title card ("Blood For Blood") -> menu screen with a
// Play button that loads the gameplay scene. Built entirely at runtime by
// NetworkScaffoldSetup.CreateBootScene(), same pattern as the runtime-built HUDs. The menu screen
// itself has a 3D backdrop (moonlit environment + a showcase character) built directly into the
// scene behind the UI Canvas — backgroundOverlay is a solid-black full-screen Image shown only
// during the studio/title cards so that backdrop stays hidden until the menu appears.
//
// Every transition (studio->title, title->menu, menu->gameplay on Play) crossfades via CanvasGroup
// alpha rather than an instant SetActive cut. CanvasGroups are fetched/added in Awake() rather than
// baked in at editor-build time — cheap, and avoids yet another field that would need
// [SerializeField] to survive a scene save (see the studioScreen/titleScreen/menuScreen note below).
public class BootSequenceController : MonoBehaviour
{
    [SerializeField] private float studioDuration = 2f;
    [SerializeField] private float titleDuration = 2f;
    [SerializeField] private float fadeDuration = 0.6f;
    [SerializeField] private string gameplaySceneName = "SampleScene";

    // Must be [SerializeField]: Configure() below is only ever called once, at editor-build time
    // in NetworkScaffoldSetup.CreateBootScene(), before the scene is saved to disk. A plain
    // private field without this attribute isn't serialized, so the assignment would be silently
    // lost on save — these would all read back null when the scene loads at Play mode start,
    // which is exactly what happened (NullReferenceException in RunSequence()).
    [SerializeField] private GameObject studioScreen;
    [SerializeField] private GameObject titleScreen;
    [SerializeField] private GameObject menuScreen;
    [SerializeField] private GameObject backgroundOverlay;

    private CanvasGroup studioGroup;
    private CanvasGroup titleGroup;
    private CanvasGroup menuGroup;
    private CanvasGroup backgroundGroup;

    public void Configure(GameObject studio, GameObject title, GameObject menu, GameObject overlay)
    {
        studioScreen = studio;
        titleScreen = title;
        menuScreen = menu;
        backgroundOverlay = overlay;
    }

    private void Awake()
    {
        studioGroup = RequireCanvasGroup(studioScreen);
        titleGroup = RequireCanvasGroup(titleScreen);
        menuGroup = RequireCanvasGroup(menuScreen);
        backgroundGroup = RequireCanvasGroup(backgroundOverlay);
    }

    private static CanvasGroup RequireCanvasGroup(GameObject go)
    {
        CanvasGroup group = go.GetComponent<CanvasGroup>();
        return group != null ? group : go.AddComponent<CanvasGroup>();
    }

    private void Start()
    {
        StartCoroutine(RunSequence());
    }

    private IEnumerator RunSequence()
    {
        backgroundGroup.alpha = 1f;
        studioGroup.alpha = 0f;
        titleGroup.alpha = 0f;
        menuGroup.alpha = 0f;
        titleScreen.SetActive(false);
        menuScreen.SetActive(false);

        studioScreen.SetActive(true);
        yield return Fade(studioGroup, 0f, 1f);
        yield return new WaitForSeconds(studioDuration);
        yield return Fade(studioGroup, 1f, 0f);
        studioScreen.SetActive(false);

        titleScreen.SetActive(true);
        yield return Fade(titleGroup, 0f, 1f);
        yield return new WaitForSeconds(titleDuration);
        yield return Fade(titleGroup, 1f, 0f);
        titleScreen.SetActive(false);

        // Crossfade the black cover away while the menu fades in, revealing the 3D backdrop.
        menuScreen.SetActive(true);
        yield return CrossFade(backgroundGroup, menuGroup);
        backgroundOverlay.SetActive(false);
    }

    public void OnPlayPressed()
    {
        StartCoroutine(TransitionToGameplay());
    }

    private IEnumerator TransitionToGameplay()
    {
        backgroundOverlay.SetActive(true);
        backgroundGroup.alpha = 0f;
        yield return CrossFade(menuGroup, backgroundGroup);
        SceneManager.LoadScene(gameplaySceneName);
    }

    public void OnQuitPressed()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private IEnumerator Fade(CanvasGroup group, float from, float to)
    {
        float t = 0f;
        group.alpha = from;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / fadeDuration));
            yield return null;
        }
        group.alpha = to;
    }

    private IEnumerator CrossFade(CanvasGroup outGroup, CanvasGroup inGroup)
    {
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float ratio = Mathf.Clamp01(t / fadeDuration);
            outGroup.alpha = 1f - ratio;
            inGroup.alpha = ratio;
            yield return null;
        }
        outGroup.alpha = 0f;
        inGroup.alpha = 1f;
    }
}

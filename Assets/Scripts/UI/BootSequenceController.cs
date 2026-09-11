using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Boot flow for Assets/Scenes/Boot.unity (Build Settings scene 0, gameplay SampleScene is scene
// 1): studio splash ("Agape Forge") -> game title card ("Blood For Blood") -> menu screen with a
// Play button that loads the gameplay scene. Built entirely at runtime by
// NetworkScaffoldSetup.CreateBootScene(), same pattern as the runtime-built HUDs.
public class BootSequenceController : MonoBehaviour
{
    [SerializeField] private float studioDuration = 2f;
    [SerializeField] private float titleDuration = 2f;
    [SerializeField] private string gameplaySceneName = "SampleScene";

    private GameObject studioScreen;
    private GameObject titleScreen;
    private GameObject menuScreen;

    public void Configure(GameObject studio, GameObject title, GameObject menu)
    {
        studioScreen = studio;
        titleScreen = title;
        menuScreen = menu;
    }

    private void Start()
    {
        StartCoroutine(RunSequence());
    }

    private IEnumerator RunSequence()
    {
        studioScreen.SetActive(true);
        titleScreen.SetActive(false);
        menuScreen.SetActive(false);

        yield return new WaitForSeconds(studioDuration);

        studioScreen.SetActive(false);
        titleScreen.SetActive(true);

        yield return new WaitForSeconds(titleDuration);

        titleScreen.SetActive(false);
        menuScreen.SetActive(true);
    }

    public void OnPlayPressed()
    {
        SceneManager.LoadScene(gameplaySceneName);
    }
}

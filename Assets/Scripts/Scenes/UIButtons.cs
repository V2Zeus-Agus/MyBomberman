using UnityEngine;
using UnityEngine.SceneManagement;

public class UIButtons : MonoBehaviour
{
    [Header("Escenas")]
    [SerializeField] private string mainMenuScene = "Menu";
    [SerializeField] private string selectLevelScene = "Levels";
    [SerializeField] private string modevsIAScene = "vsIA";
    [SerializeField] private string mode2vs2Scene = "Bomberman2vs";
    [SerializeField] private string mode3vs3Scene = "3vs";
    [SerializeField] private string mode4vs4Scene = "4vs";
    [SerializeField] private string victoryScene = "Victory";

    public void Play()
    {
        SceneManager.LoadScene(selectLevelScene);

    }

    public void Quit()
    {
        Debug.Log("Saliendo del juego...");
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }


    public void PlayvsIA()
    {
        SceneManager.LoadScene(modevsIAScene);
    }

    public void Play2vs2()
    {
        SceneManager.LoadScene(mode2vs2Scene);
    }

    public void Play3vs3()
    {
        SceneManager.LoadScene(mode3vs3Scene);
    }

    public void Play4vs4()
    {
        SceneManager.LoadScene(mode4vs4Scene);
    }

    public void Repeat()
    {
        string lastLevel = PlayerPrefs.GetString("LastLevel", mode2vs2Scene);
        SceneManager.LoadScene(lastLevel);
    }

    public void GoToLevels()
    {
        SceneManager.LoadScene(selectLevelScene);
    }

    public void GoToMainMenu()
    {
        SceneManager.LoadScene(mainMenuScene);
    }
}

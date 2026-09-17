using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

#if UNITY_EDITOR
using UnityEditor; 
#endif

public class VictoryUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI winnerText;

#if UNITY_EDITOR
    [Header("Escena de juego (arrastrá el .unity)")]
    [SerializeField] private SceneAsset gameplaySceneAsset;
#endif

    [SerializeField, HideInInspector] private string gameplaySceneName = "Game";

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (gameplaySceneAsset != null)
            gameplaySceneName = gameplaySceneAsset.name;
    }
#endif

    private void Start()
    {
        string name = PlayerPrefs.GetString("WinnerName", "Jugador");
        string number = name.Replace("Player ", "");
        if (winnerText) winnerText.text = number;
    }

    public void OnPlayAgain()
    {
        SceneManager.LoadScene(gameplaySceneName); 
    }

    public void OnMainMenu(string menuScene = "MainMenu")
    {
        SceneManager.LoadScene(menuScene);
    }
}

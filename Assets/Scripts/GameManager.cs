using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    [Header("Jugadores")]
    public GameObject[] players;

    [Header("Escenas (runtime)")]
    [SerializeField] private string gameplaySceneName = "2vs3Game";
    [SerializeField] private string victorySceneName = "Victory";

#if UNITY_EDITOR
    [Header("Escenas (editor) - arrastrá .unity")]
    [SerializeField] private UnityEditor.SceneAsset gameplaySceneAsset;
    [SerializeField] private UnityEditor.SceneAsset victorySceneAsset;

    private void OnValidate()
    {
        if (gameplaySceneAsset != null) gameplaySceneName = gameplaySceneAsset.name;
        if (victorySceneAsset != null) victorySceneName = victorySceneAsset.name;
    }
#endif

    private bool roundEnded = false;

    void Awake()
    {
        if (players == null || players.Length == 0)
            players = GameObject.FindGameObjectsWithTag("Player");

        if (players.Length < 3)
            Debug.LogWarning($"[GameManager] Jugadores en array: {players.Length}. ¿Falta asignar P3?");

        // ▶ Música de gameplay
        if (AudioManager.Instance != null) AudioManager.Instance.PlayGameplayMusic();
    }

    public void CheckWinState()
    {
        if (roundEnded) return;

        List<GameObject> alive = new List<GameObject>(players.Length);
        foreach (var p in players)
            if (p != null && p.activeInHierarchy) alive.Add(p);

        int count = alive.Count;

        if (count > 1) return;

        roundEnded = true;

        if (count == 1)
        {
            var winner = alive[0];
            PlayerPrefs.SetString("WinnerName", winner.name);
            PlayerPrefs.SetString("LastLevel", SceneManager.GetActiveScene().name);
            PlayerPrefs.Save();

            SceneManager.LoadScene(victorySceneName);
        }
        else
        {
            Invoke(nameof(NewRound), 2f);
        }
    }

    private void NewRound()
    {
        SceneManager.LoadScene(gameplaySceneName);
    }
}

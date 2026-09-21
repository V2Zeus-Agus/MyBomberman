using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;


public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    [Header("Audio")]
    [SerializeField] private AudioMixer audioMixer;
    // Nombre del parámetro expuesto en el AudioMixer (click derecho > Expose)
    private const string MIXER_PARAM = "MasterVolume";

    // --- Estado actual ---
    public float CurrentVolume { get; private set; }
    public bool IsFullscreen { get; private set; }
    public int CurrentResolutionIndex { get; private set; }

    // Resoluciones disponibles (las más comunes 16:9)
    private Resolution[] availableResolutions;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        CacheResolutions();
        LoadSettings();
        ApplySettings();
    }

    /// <summary>
    /// Filtra resoluciones únicas del sistema (evita duplicados por refresh rate).
    /// </summary>
    private void CacheResolutions()
    {
        var systemRes = Screen.resolutions;
        var unique = new System.Collections.Generic.List<Resolution>();

        int lastW = 0, lastH = 0;
        foreach (var r in systemRes)
        {
            if (r.width != lastW || r.height != lastH)
            {
                unique.Add(r);
                lastW = r.width;
                lastH = r.height;
            }
        }

        availableResolutions = unique.ToArray();
    }

    public Resolution[] GetResolutions()
    {
        return availableResolutions;
    }

    // ===================== VOLUMEN =====================

    /// <summary>
    /// Setea el volumen (0.0001 a 1). Convierte a dB para el AudioMixer.
    /// </summary>
    public void SetVolume(float volume)
    {
        volume = Mathf.Clamp(volume, 0.0001f, 1f);
        CurrentVolume = volume;

        // Convertir a escala logarítmica de dB (-80 a 0)
        if (audioMixer != null)
        {
            audioMixer.SetFloat(MIXER_PARAM, Mathf.Log10(volume) * 20f);
        }

        PlayerPrefs.SetFloat("Settings_Volume", volume);
    }

    // =================== FULLSCREEN ====================

    public void SetFullscreen(bool fullscreen)
    {
        IsFullscreen = fullscreen;
        Screen.fullScreen = fullscreen;

        PlayerPrefs.SetInt("Settings_Fullscreen", fullscreen ? 1 : 0);
    }

    // =================== RESOLUCIÓN ====================

    public void SetResolution(int index)
    {
        if (index < 0 || index >= availableResolutions.Length) return;

        CurrentResolutionIndex = index;
        Resolution res = availableResolutions[index];
        Screen.SetResolution(res.width, res.height, IsFullscreen);

        PlayerPrefs.SetInt("Settings_Resolution", index);
    }

    // ================ LOAD / APPLY =====================

    private void LoadSettings()
    {
        // Volumen: default 0.75
        CurrentVolume = PlayerPrefs.GetFloat("Settings_Volume", 0.75f);

        // Fullscreen: default true
        IsFullscreen = PlayerPrefs.GetInt("Settings_Fullscreen", 1) == 1;

        // Resolución: default la más alta disponible
        int defaultRes = availableResolutions.Length - 1;
        CurrentResolutionIndex = PlayerPrefs.GetInt("Settings_Resolution", defaultRes);

        // Validar que el índice guardado siga siendo válido
        if (CurrentResolutionIndex >= availableResolutions.Length)
        {
            CurrentResolutionIndex = defaultRes;
        }
    }

    private void ApplySettings()
    {
        SetVolume(CurrentVolume);
        SetFullscreen(IsFullscreen);
        SetResolution(CurrentResolutionIndex);
    }

    /// <summary>
    /// Guarda todo de golpe (útil al cerrar un menú de opciones).
    /// </summary>
    public void SaveAll()
    {
        PlayerPrefs.Save();
    }
}
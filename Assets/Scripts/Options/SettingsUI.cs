using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Conecta los elementos de UI (Dropdown, Toggle, Slider) con el SettingsManager.
/// Ponelo en el panel Options.
/// </summary>
public class SettingsUI : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Slider volumeSlider;

    private void OnEnable()
    {
        SetupResolutionDropdown();
        SetupFullscreenToggle();
        SetupVolumeSlider();
    }

    // ========== RESOLUCIÓN ==========

    private void SetupResolutionDropdown()
    {
        if (resolutionDropdown == null || SettingsManager.Instance == null) return;

        Resolution[] resolutions = SettingsManager.Instance.GetResolutions();

        resolutionDropdown.ClearOptions();

        var options = new System.Collections.Generic.List<string>();
        foreach (var res in resolutions)
        {
            options.Add(res.width + " x " + res.height);
        }

        resolutionDropdown.AddOptions(options);
        resolutionDropdown.value = SettingsManager.Instance.CurrentResolutionIndex;
        resolutionDropdown.RefreshShownValue();

        resolutionDropdown.onValueChanged.RemoveAllListeners();
        resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
    }

    private void OnResolutionChanged(int index)
    {
        SettingsManager.Instance.SetResolution(index);
    }

    // ========== FULLSCREEN ==========

    private void SetupFullscreenToggle()
    {
        if (fullscreenToggle == null || SettingsManager.Instance == null) return;

        fullscreenToggle.isOn = SettingsManager.Instance.IsFullscreen;

        fullscreenToggle.onValueChanged.RemoveAllListeners();
        fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
    }

    private void OnFullscreenChanged(bool isFullscreen)
    {
        SettingsManager.Instance.SetFullscreen(isFullscreen);
    }

    // ========== VOLUMEN ==========

    private void SetupVolumeSlider()
    {
        if (volumeSlider == null || SettingsManager.Instance == null) return;

        volumeSlider.minValue = 0.0001f;
        volumeSlider.maxValue = 1f;
        volumeSlider.value = SettingsManager.Instance.CurrentVolume;

        volumeSlider.onValueChanged.RemoveAllListeners();
        volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
    }

    private void OnVolumeChanged(float value)
    {
        SettingsManager.Instance.SetVolume(value);
    }

    // ========== GUARDAR ==========

    private void OnDisable()
    {
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.SaveAll();
        }
    }
}
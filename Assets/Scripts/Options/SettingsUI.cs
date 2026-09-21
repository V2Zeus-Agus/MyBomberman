using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Conecta los elementos de UI (Dropdown, Toggle) con el SettingsManager.
/// Ponelo en el panel Options.
/// </summary>
public class SettingsUI : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;

    private void OnEnable()
    {
        // Cada vez que se abre el panel, sincronizar UI con los valores guardados
        SetupResolutionDropdown();
        SetupFullscreenToggle();
    }

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

        // Limpiar listeners previos para evitar duplicados
        resolutionDropdown.onValueChanged.RemoveAllListeners();
        resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
    }

    private void SetupFullscreenToggle()
    {
        if (fullscreenToggle == null || SettingsManager.Instance == null) return;

        fullscreenToggle.isOn = SettingsManager.Instance.IsFullscreen;

        fullscreenToggle.onValueChanged.RemoveAllListeners();
        fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
    }

    private void OnResolutionChanged(int index)
    {
        SettingsManager.Instance.SetResolution(index);
    }

    private void OnFullscreenChanged(bool isFullscreen)
    {
        SettingsManager.Instance.SetFullscreen(isFullscreen);
    }

    private void OnDisable()
    {
        // Guardar cuando se cierra el panel
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.SaveAll();
        }
    }
}
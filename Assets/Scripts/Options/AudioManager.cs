using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Singleton que persiste entre escenas.
/// Maneja música de fondo y efectos de sonido.
/// Todos los AudioSources rutean al MasterMixer.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Mixer")]
    [SerializeField] private AudioMixerGroup musicGroup;
    [SerializeField] private AudioMixerGroup sfxGroup;

    [Header("Música")]
    [SerializeField] private AudioClip menuMusic;
    [SerializeField] private AudioClip gameplayMusic;
    [Range(0f, 1f)]
    [SerializeField] private float musicVolume = 0.5f;

    [Header("SFX")]
    [SerializeField] private AudioClip sfxBomb;
    [SerializeField] private AudioClip sfxExplosion;
    [SerializeField] private AudioClip sfxDeath;
    [SerializeField] private AudioClip sfxPowerUp;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;

    private AudioSource musicSource;
    private AudioSource sfxSource;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Crear AudioSources por código
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.volume = musicVolume;
        if (musicGroup != null) musicSource.outputAudioMixerGroup = musicGroup;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;
        sfxSource.volume = sfxVolume;
        if (sfxGroup != null) sfxSource.outputAudioMixerGroup = sfxGroup;
    }

    // ========== MÚSICA ==========

    public void PlayMenuMusic()
    {
        PlayMusic(menuMusic);
    }

    public void PlayGameplayMusic()
    {
        PlayMusic(gameplayMusic);
    }

    public void StopMusic()
    {
        musicSource.Stop();
    }

    private void PlayMusic(AudioClip clip)
    {
        if (clip == null) return;

        // Si ya está sonando el mismo clip, no reiniciar
        if (musicSource.clip == clip && musicSource.isPlaying) return;

        musicSource.clip = clip;
        musicSource.Play();
    }

    // ========== SFX ==========

    public void PlayBomb()
    {
        PlaySFX(sfxBomb);
    }

    public void PlayExplosion()
    {
        PlaySFX(sfxExplosion);
    }

    public void PlayDeath()
    {
        PlaySFX(sfxDeath);
    }

    public void PlayPowerUp()
    {
        PlaySFX(sfxPowerUp);
    }

    /// <summary>
    /// Para cualquier SFX extra que quieras disparar desde código.
    /// </summary>
    public void PlaySFX(AudioClip clip)
    {
        if (clip == null) return;
        sfxSource.PlayOneShot(clip, sfxVolume);
    }
}
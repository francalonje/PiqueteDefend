using System.Collections.Generic;
using UnityEngine;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Reproductor de audio global. Singleton persistente (sobrevive cambios de escena) que se
    /// auto-instancia antes de cargar la primera escena, así no hay que ponerlo en cada escena.
    /// Carga los clips desde <c>Resources/Audio/</c> por nombre (ver <see cref="AudioId"/>);
    /// si un clip falta, no reproduce nada (placeholder-friendly).
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        private AudioSource _music;
        private AudioSource _sfx;
        private string _currentMusic;
        private readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("AudioManager");
            go.AddComponent<AudioManager>();   // Awake hace el resto
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = true;
            _music.playOnAwake = false;

            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
        }

        /// <summary>Reproduce un efecto puntual (no interrumpe otros SFX ni la música).</summary>
        public void PlaySfx(string clipName)
        {
            AudioClip clip = Load(clipName);
            if (clip != null) _sfx.PlayOneShot(clip);
        }

        /// <summary>
        /// Cambia la música de fondo (loop). Si ya está sonando esa pista, no hace nada.
        /// Si el clip no existe (placeholder), detiene la música actual — útil para no
        /// arrastrar la música de una escena a otra.
        /// </summary>
        public void PlayMusic(string clipName)
        {
            if (_currentMusic == clipName && _music.isPlaying) return;
            _currentMusic = clipName;

            AudioClip clip = Load(clipName);
            if (clip == null) { _music.Stop(); return; }

            _music.clip = clip;
            _music.Play();
        }

        public void StopMusic()
        {
            _music.Stop();
            _currentMusic = null;
        }

        private AudioClip Load(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return null;
            if (_cache.TryGetValue(clipName, out AudioClip cached)) return cached;

            AudioClip clip = Resources.Load<AudioClip>("Audio/" + clipName);
            _cache[clipName] = clip;   // cachea incluso null para no reintentar cada vez
            return clip;
        }
    }
}

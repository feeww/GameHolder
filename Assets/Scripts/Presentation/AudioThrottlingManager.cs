using System.Collections.Generic;
using UnityEngine;

namespace GameHolder.PureDots
{
    public class AudioThrottlingManager : MonoBehaviour
    {
        public static AudioThrottlingManager Instance { get; private set; }

        [Tooltip("AudioSource components created on startup for placeholder playback. Zero disables playback.")]
        [SerializeField] private int m_AudioSourcePoolSize = PresentationConstants.AudioSourcePoolSize;
        [Tooltip("Maximum playback requests per clip per rendered frame; not a limit on all playing voices.")]
        [SerializeField] private int m_MaxConcurrentPerSound = PresentationConstants.MaxSoundsPerClipPerFrame;

        private readonly Dictionary<AudioClip, int> m_FrameCounts = new Dictionary<AudioClip, int>(PresentationConstants.SoundClipCount);
        private readonly List<AudioSource> m_AudioSources = new List<AudioSource>(PresentationConstants.AudioSourcePoolSize);
        private int m_CurrentSourceIndex;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            for (int i = 0; i < m_AudioSourcePoolSize; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                m_AudioSources.Add(src);
            }
        }

        public void StopAll()
        { for (int i = 0; i < m_AudioSources.Count; i++) m_AudioSources[i].Stop(); }
        private void OnDestroy() { if (Instance == this) Instance = null; }
        public void ResetFrameCounters()
        {
            m_FrameCounts.Clear();
        }

        public void PlaySoundThrottled(AudioClip clip, float volume = 1.0f)
        {
            if (clip == null) return;

            m_FrameCounts.TryGetValue(clip, out int count);
            if (count >= m_MaxConcurrentPerSound)
            {
                return; // Throttled: drop sound to prevent comb filtering and audio clipping
            }

            m_FrameCounts[clip] = count + 1;

            if (m_AudioSources.Count == 0) return;

            var source = m_AudioSources[m_CurrentSourceIndex];
            m_CurrentSourceIndex = (m_CurrentSourceIndex + 1) % m_AudioSources.Count;

            // Pitch variation +/- 10%
            source.pitch = 1.0f + Random.Range(-PresentationConstants.AudioPitchVariation, PresentationConstants.AudioPitchVariation);
            source.PlayOneShot(clip, volume);
        }
    }
}

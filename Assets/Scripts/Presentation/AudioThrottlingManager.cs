using System.Collections.Generic;
using UnityEngine;

namespace GameHolder.PureDots
{
    public class AudioThrottlingManager : MonoBehaviour
    {
        public static AudioThrottlingManager Instance { get; private set; }

        [SerializeField] private AudioSource m_AudioSourcePrefab;
        [SerializeField] private int m_AudioSourcePoolSize = 16;
        [SerializeField] private int m_MaxConcurrentPerSound = 3;

        private readonly Dictionary<AudioClip, int> m_FrameCounts = new Dictionary<AudioClip, int>();
        private readonly List<AudioSource> m_AudioSources = new List<AudioSource>();
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
            source.pitch = 1.0f + Random.Range(-0.1f, 0.1f);
            source.PlayOneShot(clip, volume);
        }
    }
}

using UnityEngine;

namespace GameHolder.PureDots
{
    public static class PureDotsAudioGenerator
    {
        public static AudioClip CreateShootClip()
        {
            const int sampleRate = 44100;
            const float duration = 0.08f;
            int numSamples = (int)(sampleRate * duration);
            var samples = new float[numSamples];

            for (int i = 0; i < numSamples; i++)
            {
                float t = i / (float)sampleRate;
                float progress = i / (float)numSamples;
                // Frequency sweeps down from 900 Hz to 250 Hz
                float freq = Mathf.Lerp(900.0f, 250.0f, progress);
                float env = 1.0f - progress;
                samples[i] = Mathf.Sin(2.0f * Mathf.PI * freq * t) * env * 0.4f;
            }

            var clip = AudioClip.Create("ShootSFX", numSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public static AudioClip CreateEnemyDeathClip()
        {
            const int sampleRate = 44100;
            const float duration = 0.18f;
            int numSamples = (int)(sampleRate * duration);
            var samples = new float[numSamples];

            var rng = new System.Random(42);
            for (int i = 0; i < numSamples; i++)
            {
                float t = i / (float)sampleRate;
                float progress = i / (float)numSamples;
                float env = (1.0f - progress) * (1.0f - progress);
                // Low rumble mixed with noise
                float rumble = Mathf.Sin(2.0f * Mathf.PI * 90.0f * t);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                samples[i] = (rumble * 0.5f + noise * 0.5f) * env * 0.5f;
            }

            var clip = AudioClip.Create("DeathSFX", numSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public static AudioClip CreateGemCollectClip()
        {
            const int sampleRate = 44100;
            const float duration = 0.12f;
            int numSamples = (int)(sampleRate * duration);
            var samples = new float[numSamples];

            for (int i = 0; i < numSamples; i++)
            {
                float t = i / (float)sampleRate;
                float progress = i / (float)numSamples;
                // High frequency crystal arpeggio: 1200 Hz to 1800 Hz
                float freq = Mathf.Lerp(1200.0f, 1800.0f, progress);
                float env = Mathf.Sin(progress * Mathf.PI) * (1.0f - progress * 0.5f);
                samples[i] = Mathf.Sin(2.0f * Mathf.PI * freq * t) * env * 0.35f;
            }

            var clip = AudioClip.Create("GemCollectSFX", numSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public static AudioClip CreatePlayerHitClip()
        {
            const int sampleRate = 44100;
            const float duration = 0.2f;
            int numSamples = (int)(sampleRate * duration);
            var samples = new float[numSamples];

            for (int i = 0; i < numSamples; i++)
            {
                float t = i / (float)sampleRate;
                float progress = i / (float)numSamples;
                float env = 1.0f - progress;
                // 150 Hz alarm buzz
                float buzz = Mathf.Sin(2.0f * Mathf.PI * 150.0f * t) > 0.0f ? 1.0f : -1.0f;
                samples[i] = buzz * env * 0.4f;
            }

            var clip = AudioClip.Create("PlayerHitSFX", numSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}

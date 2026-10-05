using Unity.Mathematics;
using UnityEngine;

namespace GameHolder.PureDots
{
    public static class PresentationConstants
    {
        public const float CameraOrthographicSize = 10.0f;
        public const float CameraNearClip = -50.0f;
        public const float CameraFarClip = 50.0f;
        public const float CameraZPosition = -10.0f;
        public const float FloorZPosition = 10.0f;
        public const float FloorQuadSize = 100.0f;
        public const float CameraViewportExtentY = 15.0f;
        public const float CameraDepthScale = 0.25f;
        public const float CameraZMinOffset = -5.0f;
        public const float GemZOffset = 2.0f;
        public const float SpeedDepthScale = 0.2f;

        public const float CameraSmoothSpeed = 15;
        public const float FloorTileScale = 2;
        public const float MinimumFloorTileScale = .1f;
        public const float GamepadDeadZoneSq = .04f;
        public const float GemFlashDuration = .18f;

        public const int AudioSourcePoolSize = 16;
        public const int SoundClipCount = 3;
        public const int MaxSoundsPerClipPerFrame = 3;
        public const float AudioPitchVariation = .1f;
        public const float EnemyDeathVolume = .45f;
        public const float PlayerHitVolume = .6f;
        public const float GemCollectVolume = .35f;

        public const int ParticleCapacity = 2048;
        public const int ParticleSystemCount = 3;
        public const int ParticlesPerFrame = 512;
        public const int EmitCallsPerFrame = 64;
        public const int MaxBurstParticles = 32;
        public const int DeathBatchCount = 4;
        public const int DeathBurstParticles = 8;
        public const int HitBurstParticles = 6;
        public const int DefaultHitBurstParticles = 5;
        public const int GemBurstParticles = 6;
        public const float ParticleLifetime = .35f;
        public const float ParticleSpawnRadius = .2f;
        public const float ParticleZ = -.1f;
        public const float DeathParticleSize = .35f, DeathParticleSpeed = 4;
        public const float HitParticleSize = .25f, HitParticleSpeed = 3;
        public const float GemParticleSize = .2f, GemParticleSpeed = 2.5f;
        public static Color DeathParticleColor => new Color(1, .35f, .2f, 1);
        public static Color HitParticleColor => new Color(1, .9f, .2f, 1);
        public static Color GemParticleColor => new Color(.2f, 1, .8f, 1);

        public const int HudTextCapacity = 512;
        public const int HudFontSize = 14;
        public const float HudFontBaseline = 17;
        public const float HudLineHeight = 26;
        public const float FpsSampleInterval = .5f;
        public const float SpriteAlphaCutoff = .3f;
        public const int SpriteTextureSize = 64;
        public const int ProjectileTextureSize = 32;
        public const int GemAtlasSize = 128;
        public const int GemAtlasGridSize = 2;
        public const float GemAtlasUVScale = 1f / GemAtlasGridSize;
        public static float4 GemTier0Color => new float4(.2f, 1, .4f, 1);
        public static float4 GemTier1Color => new float4(.2f, .8f, 1, 1);
        public static float4 GemTier2Color => new float4(.9f, .3f, 1, 1);
        public static float4 GemTier3Color => new float4(1, .84f, 0, 1);

        public const float BlastAlphaScale = .6f;
        public static float4 GemFlashColor => new float4(2, 2, 2, 1);
    }

    public static class HudLayout
    {
        public const int SortingOrder = 100;
        public const float FirstControlY = 40, ControlSpacing = 34, DeathRestartY = 106;
        public static Vector2 StatsPanelSize => new Vector2(310, 282);
        public static Vector2 StatsPanelPosition => new Vector2(10, 10);
        public static Vector2 StatsTextSize => new Vector2(286, 270);
        public static Vector2 StatsTextPosition => new Vector2(12, 10);
        public static Vector2 ControlsPanelSize => new Vector2(235, 315);
        public static Vector2 ControlsPanelPosition => new Vector2(-245, 10);
        public static Vector2 ControlsTitleSize => new Vector2(215, 24);
        public static Vector2 ControlsTitlePosition => new Vector2(10, 10);
        public static Vector2 DeathPanelSize => new Vector2(310, 160);
        public static Vector2 DeathPanelPosition => new Vector2(-155, -80);
        public static Vector2 DeathTextSize => new Vector2(270, 90);
        public static Vector2 DeathTextPosition => new Vector2(20, 10);
        public static Vector2 ButtonSize => new Vector2(215, 28);
        public static Vector2 ButtonTextSize => new Vector2(199, 26);
        public static Vector2 ButtonTextPosition => new Vector2(8, 1);
        public const float ButtonX = 10;
        public static Color PanelColor => new Color(.04f, .05f, .08f, .92f);
        public static Color ButtonColor => new Color(.16f, .2f, .28f);
    }
}

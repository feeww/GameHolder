namespace GameHolder.PureDots
{
    /// <summary>
    /// Centralized Single Source of Truth for simulation constants, capacities, LOD distances,
    /// combat numbers, and archetype identifiers across the Pure DOTS engine.
    /// Eliminates magic numbers and synchronizes tuning across simulation and presentation.
    /// </summary>
    public static class SimulationConstants
    {
        // -------------------------------------------------------------------------
        // Object Pool Capacities (Single Source of Truth)
        // -------------------------------------------------------------------------
        public const int MaxEnemies = 10000;
        public const int MaxProjectiles = 2000;
        public const int MaxGems = 1024; // Matches GemPoolSingleton.Capacity (32KB L1 cache aligned)

        // -------------------------------------------------------------------------
        // Floating Origin Coordinates
        // -------------------------------------------------------------------------
        public const float FloatingOriginThreshold = 2000.0f;
        public const float FloatingOriginThresholdSq = FloatingOriginThreshold * FloatingOriginThreshold; // 4,000,000 m^2

        // -------------------------------------------------------------------------
        // Spatial Hash Partitioning & Neighborhood Radii
        // -------------------------------------------------------------------------
        public const float SpatialCellSize = 1.25f;
        public const float SpatialInvCellSize = 1.0f / SpatialCellSize; // 0.8
        public const uint HashPrimeX = 73856093u;
        public const uint HashPrimeY = 19349663u;

        public const float Tier1Radius = 22.0f;
        public const float Tier1RadiusSq = Tier1Radius * Tier1Radius; // 484.0f
        public const float Tier2MaxRadius = 45.0f;
        public const float Tier2MaxRadiusSq = Tier2MaxRadius * Tier2MaxRadius; // 2025.0f
        public const float MaxCatchUpMultiplier = 3.0f;

        public const float SeparationRadius = 0.8f;
        public const int MaxSeparationNeighbors = 4;

        // -------------------------------------------------------------------------
        // Player Baseline Stats & Collision
        // -------------------------------------------------------------------------
        public const float PlayerDefaultMoveSpeed = 6.0f;
        public const float PlayerDefaultMagnetRadius = 4.0f;
        public const float PlayerDefaultMaxHealth = 100.0f;
        public const float PlayerDefaultInvulnDuration = 0.5f;
        public const float PlayerRespawnGracePeriod = 1.5f;
        public const float PlayerCollisionRadius = 0.4f;
        public const float EnemyCollisionRadius = 0.4f;
        public const float MaxEnemyCollisionRadius = 0.5f;
        public const float DefaultMeleeDamage = 10.0f;

        // -------------------------------------------------------------------------
        // Combat & Auto-Attack Weapon Tuning
        // -------------------------------------------------------------------------
        public const float AttackInterval = 0.25f;
        public const float AttackSearchRadius = 18.0f;
        public const float AttackSearchRadiusSq = AttackSearchRadius * AttackSearchRadius;
        public const float ProjectileSpeed = 16.0f;
        public const float ProjectileSpreadAngle = 0.2f;
        public const int ProjectileSpreadCount = 3;

        public const float PlayerProjectileDamage = 25.0f;
        public const float PlayerProjectileRadius = 0.35f;
        public const float PlayerProjectileLifetime = 1.8f;

        public const float EnemyProjectileDamage = 15.0f;
        public const float EnemyProjectileRadius = 0.25f;
        public const float EnemyProjectileLifetime = 2.5f;

        // -------------------------------------------------------------------------
        // Experience Progression & Gem Lifecycle
        // -------------------------------------------------------------------------
        public const uint ExpPerLevelMultiplier = 50;
        public const float HealthBonusPerLevel = 10.0f;
        public const float HealthHealPerLevel = 20.0f;
        public const float OffScreenGemRecycleDistance = 35.0f;
        public const float OffScreenGemRecycleDistanceSq = OffScreenGemRecycleDistance * OffScreenGemRecycleDistance; // 1225.0f
        public const uint DefaultEnemyExpDrop = 10;

        // -------------------------------------------------------------------------
        // Entity Type Identifiers
        // -------------------------------------------------------------------------
        public const uint EnemyTankTypeId = 0;
        public const uint EnemyRunnerTypeId = 1;
        public const uint PlayerTypeId = 999;

        // -------------------------------------------------------------------------
        // Camera & 2D Depth Mapping Bounds
        // -------------------------------------------------------------------------
        public const float CameraOrthographicSize = 10.0f;
        public const float CameraNearClip = -50.0f;
        public const float CameraFarClip = 50.0f;
        public const float CameraZPosition = -10.0f;
        public const float FloorZPosition = 10.0f;
        public const float FloorQuadSize = 100.0f;

        public const float CameraViewportExtentY = 15.0f;
        public const float CameraDepthScale = 0.25f;
        public const float CameraZMinOffset = -5.0f;
        public const float CameraZMaxOffset = 5.0f;
    }
}

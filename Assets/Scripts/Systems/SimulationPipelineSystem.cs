using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    // Serial stages own these writable lookups; no stage synchronizes on the main thread.
    public struct SimulationAccess
    {
        public Entity State, Wave;
        public ComponentLookup<SimulationRunState> Run;
        public ComponentLookup<WaveSpawnerConfig> Waves;
        public ComponentLookup<SimulationSnapshot> Snapshots;
        public ComponentLookup<LocalTransform> Transforms;
        public ComponentLookup<PreviousPosition> Previous;
        public ComponentLookup<MovementVelocity> Velocities;
        public ComponentLookup<SeparationCache> Separation;
        public ComponentLookup<CurrentHealth> Health;
        public ComponentLookup<TypeId> Types;
        public ComponentLookup<EnemyActiveTag> Enemies;
        public ComponentLookup<EnemyRangedTag> Ranged;
        public ComponentLookup<EnemyRangedCooldown> RangedCooldown;
        public ComponentLookup<EnemyMeleeCooldown> MeleeCooldown;
        public ComponentLookup<ProjectileActiveTag> Projectiles;
        public ComponentLookup<ProjectileData> ProjectileData;
        public ComponentLookup<ExplosiveProjectile> Explosives;
        public ComponentLookup<LaserBeam> Lasers;
        public ComponentLookup<PlayerWeapon> Weapons;
        [ReadOnly] public ComponentLookup<PlayerProjectileTag> PlayerProjectiles;
        public ComponentLookup<GemData> GemData;
        public ComponentLookup<GemActiveTag> Gems;
        public ComponentLookup<PlayerStats> Stats;
        public ComponentLookup<PlayerInvulnerability> Invulnerability;
        [ReadOnly] public ComponentLookup<SimulationInput> Input;
        public Entity InputEntity;
        public EnemyPoolSingleton EnemyPool;
        public PlayerProjectilePoolSingleton PlayerPool;
        public EnemyProjectilePoolSingleton EnemyProjectilePool;
        public GemPoolSingleton GemPool;
        public UnsafeParallelMultiHashMap<uint, GridEntry> Grid;
        public UnsafeParallelHashMap<Unity.Mathematics.int2, CrowdCell> CrowdCells;
        public UnsafeQueue<DamageEvent> Damage;
        public UnsafeQueue<PlayerDamageEvent> PlayerDamage;
        public UnsafeQueue<Entity> Deactivations;
        public UnsafeQueue<GemSpawnRequest> GemSpawns;
        public UnsafeQueue<SimulationCommand> Commands;
        public SimulationBridgeQueuesSingleton Bridge;
        public BlobAssetReference<EnemyConfigCatalog> Catalog;
        public StartingPlayerConfig StartingPlayer;
        public WaveSpawnerConfig StartingWave;
        public BlobAssetReference<RewardCatalog> Rewards;
        public TemporaryZoneConfig Zones;

        public void Initialize(ref SystemState state)
        {
            Run = state.GetComponentLookup<SimulationRunState>();
            Waves = state.GetComponentLookup<WaveSpawnerConfig>();
            Snapshots = state.GetComponentLookup<SimulationSnapshot>();
            Transforms = state.GetComponentLookup<LocalTransform>();
            Previous = state.GetComponentLookup<PreviousPosition>();
            Velocities = state.GetComponentLookup<MovementVelocity>();
            Separation = state.GetComponentLookup<SeparationCache>();
            Health = state.GetComponentLookup<CurrentHealth>();
            Types = state.GetComponentLookup<TypeId>();
            Enemies = state.GetComponentLookup<EnemyActiveTag>();
            Ranged = state.GetComponentLookup<EnemyRangedTag>();
            RangedCooldown = state.GetComponentLookup<EnemyRangedCooldown>();
            MeleeCooldown = state.GetComponentLookup<EnemyMeleeCooldown>();
            Projectiles = state.GetComponentLookup<ProjectileActiveTag>();
            ProjectileData = state.GetComponentLookup<ProjectileData>();
            Explosives = state.GetComponentLookup<ExplosiveProjectile>();
            Lasers = state.GetComponentLookup<LaserBeam>();
            Weapons = state.GetComponentLookup<PlayerWeapon>();
            PlayerProjectiles = state.GetComponentLookup<PlayerProjectileTag>(true);
            GemData = state.GetComponentLookup<GemData>();
            Gems = state.GetComponentLookup<GemActiveTag>();
            Stats = state.GetComponentLookup<PlayerStats>();
            Invulnerability = state.GetComponentLookup<PlayerInvulnerability>();
            Input = state.GetComponentLookup<SimulationInput>(true);
        }
        public void Update(ref SystemState state)
        {
            Run.Update(ref state);
            Waves.Update(ref state);
            Snapshots.Update(ref state);
            Transforms.Update(ref state);
            Previous.Update(ref state);
            Velocities.Update(ref state);
            Separation.Update(ref state);
            Health.Update(ref state);
            Types.Update(ref state);
            Enemies.Update(ref state);
            Ranged.Update(ref state);
            RangedCooldown.Update(ref state);
            MeleeCooldown.Update(ref state);
            Projectiles.Update(ref state);
            ProjectileData.Update(ref state);
            Explosives.Update(ref state);
            Lasers.Update(ref state);
            Weapons.Update(ref state);
            PlayerProjectiles.Update(ref state);
            GemData.Update(ref state);
            Gems.Update(ref state);
            Stats.Update(ref state);
            Invulnerability.Update(ref state);
            Input.Update(ref state);
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    public partial struct SimulationPipelineSystem : ISystem
    {
        private SimulationAccess m_Access;
        private NativeList<DamageEvent> m_Damage;
        private NativeList<Entity> m_Deactivations;
        private NativeParallelHashMap<Entity, float> m_AreaDamage;
        private NativeParallelHashMap<Unity.Mathematics.int2, int> m_GemOverflowSlots;
        private bool m_Bound;
        private ComponentLookup<SimulationRunState> m_ReadRun;
        private ComponentLookup<EnemyActiveTag> m_ReadEnemies;
        private ComponentLookup<TypeId> m_ReadTypes;
        private ComponentLookup<EnemyRangedCooldown> m_ReadRangedCooldown;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationRunState>();
            m_Access.Initialize(ref state);
            m_ReadRun = state.GetComponentLookup<SimulationRunState>(true);
            m_ReadEnemies = state.GetComponentLookup<EnemyActiveTag>(true);
            m_ReadTypes = state.GetComponentLookup<TypeId>(true);
            m_ReadRangedCooldown = state.GetComponentLookup<EnemyRangedCooldown>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!m_Bound)
            {
                m_Access.State = SystemAPI.GetSingletonEntity<SimulationRunState>();
                m_Access.Wave = SystemAPI.GetSingletonEntity<WaveSpawnerConfig>();
                m_Access.StartingWave = SystemAPI.GetSingleton<WaveSpawnerConfig>();
                m_Access.InputEntity = SystemAPI.GetSingletonEntity<SimulationInput>();
                m_Access.EnemyPool = SystemAPI.GetSingleton<EnemyPoolSingleton>();
                m_Access.PlayerPool = SystemAPI.GetSingleton<PlayerProjectilePoolSingleton>();
                m_Access.EnemyProjectilePool = SystemAPI.GetSingleton<EnemyProjectilePoolSingleton>();
                m_Access.GemPool = SystemAPI.GetSingleton<GemPoolSingleton>();
                m_Access.Grid = SystemAPI.GetSingleton<EnemySpatialGridSingleton>().Grid;
                m_Access.CrowdCells = SystemAPI.GetSingleton<EnemySpatialGridSingleton>().CrowdCells;
                m_Access.Damage = SystemAPI.GetSingleton<DamageEventQueueSingleton>().DamageQueue;
                m_Access.PlayerDamage = SystemAPI.GetSingleton<PlayerDamageEventQueueSingleton>().PlayerDamageQueue;
                m_Access.Deactivations = SystemAPI.GetSingleton<ProjectileDeactivationQueueSingleton>().StagedDeactivations;
                m_Access.GemSpawns = SystemAPI.GetSingleton<GemSpawnQueueSingleton>().SpawnQueue;
                m_Access.Commands = SystemAPI.GetSingleton<SimulationCommandQueue>().Commands;
                m_Access.Bridge = SystemAPI.GetSingleton<SimulationBridgeQueuesSingleton>();
                m_Access.Catalog = SystemAPI.GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
                m_Access.StartingPlayer = SystemAPI.GetSingleton<StartingPlayerConfig>();
                m_Access.Rewards = SystemAPI.GetSingleton<RewardCatalogSingleton>().Catalog;
                m_Access.Zones = SystemAPI.HasSingleton<TemporaryZoneConfig>() ? SystemAPI.GetSingleton<TemporaryZoneConfig>() : default;
                int enemies = m_Access.EnemyPool.AllEnemies.Length;
                int playerProjectiles = m_Access.PlayerPool.AllProjectiles.Length;
                int enemyProjectiles = m_Access.EnemyProjectilePool.AllProjectiles.Length;
                m_Damage = new NativeList<DamageEvent>(playerProjectiles + enemies * 2, Allocator.Persistent);
                m_Deactivations = new NativeList<Entity>((playerProjectiles + enemyProjectiles) * 2, Allocator.Persistent);
                m_AreaDamage = new NativeParallelHashMap<Entity, float>(enemies, Allocator.Persistent);
                m_GemOverflowSlots = new NativeParallelHashMap<Unity.Mathematics.int2, int>(enemies, Allocator.Persistent);
                m_Bound = true;
            }
            m_Access.Update(ref state);
            m_ReadRun.Update(ref state); m_ReadEnemies.Update(ref state);
            m_ReadTypes.Update(ref state);
            m_ReadRangedCooldown.Update(ref state);
            float dt = SystemAPI.Time.DeltaTime;
            // Unsafe containers are deliberately shared: every producer and consumer is in this chain.
            JobHandle chain = new SimulationControlJob { A = m_Access, Dt = dt }.Schedule(state.Dependency);
            chain = new PredictiveWaveSpawnJob { A = m_Access, Dt = dt }.Schedule(chain);
            chain = new MoveEnemiesJob
            {
                Entities = m_Access.EnemyPool.AllEnemies, Transforms = m_Access.Transforms,
                Previous = m_Access.Previous, Velocities = m_Access.Velocities, Separation = m_Access.Separation,
                Cooldown = m_Access.MeleeCooldown, Active = m_ReadEnemies,
                Types = m_ReadTypes, Run = m_ReadRun, RangedCooldown = m_ReadRangedCooldown,
                State = m_Access.State, Catalog = m_Access.Catalog, Dt = dt
            }.Schedule(m_Access.EnemyPool.AllEnemies.Length, SimulationConstants.JobBatchSize, chain);
            chain = new MoveProjectilesJob { A = m_Access, Dt = dt }.Schedule(chain);
            chain = new RebuildSpatialGridJob { A = m_Access }.Schedule(chain);
            chain = new CrowdContactJob
            {
                Entities = m_Access.EnemyPool.AllEnemies, Active = m_ReadEnemies,
                Transforms = m_Access.Transforms, Velocities = m_Access.Velocities, Previous = m_Access.Previous,
                Types = m_ReadTypes, Catalog = m_Access.Catalog, Cache = m_Access.Separation, Dt = dt,
                Run = m_ReadRun, State = m_Access.State, Grid = m_Access.Grid, Cells = m_Access.CrowdCells
            }.Schedule(m_Access.EnemyPool.AllEnemies.Length, SimulationConstants.JobBatchSize, chain);
            // Combat must see resolved contacts and include their displacement in relative projectile sweeps.
            chain = new RebuildSpatialGridJob { A = m_Access }.Schedule(chain);
            chain = new PlayerHitCheckJob { A = m_Access }.Schedule(chain);
            chain = new ProjectileBroadphaseJob { A = m_Access }.Schedule(chain);
            chain = new ExplosiveCombatJob { A = m_Access, AreaDamage = m_AreaDamage }.Schedule(chain);
            chain = new LaserCombatJob { A = m_Access, AreaDamage = m_AreaDamage }.Schedule(chain);
            chain = new DamageResolutionJob { A = m_Access, Damage = m_Damage, Deactivations = m_Deactivations, AreaDamage = m_AreaDamage }.Schedule(chain);
            chain = new GemLifecycleJob { A = m_Access, OverflowSlots = m_GemOverflowSlots }.Schedule(chain);
            chain = new TemporaryZoneJob { A = m_Access, Dt = dt }.Schedule(chain);
            chain = new PlayerAutoAttackJob { A = m_Access, Dt = dt }.Schedule(chain);
            chain = new EnemyRangedAttackJob { A = m_Access, Dt = dt }.Schedule(chain);
            chain = new PublishSimulationSnapshotJob { A = m_Access }.Schedule(chain);
            state.Dependency = chain;
            SystemAPI.SetSingleton(new SimulationJobFence { Handle = chain });
        }

        public void OnDestroy(ref SystemState state)
        {
            state.Dependency.Complete();
            if (m_Damage.IsCreated) m_Damage.Dispose();
            if (m_Deactivations.IsCreated) m_Deactivations.Dispose();
            if (m_AreaDamage.IsCreated) m_AreaDamage.Dispose();
            if (m_GemOverflowSlots.IsCreated) m_GemOverflowSlots.Dispose();
        }
    }
}

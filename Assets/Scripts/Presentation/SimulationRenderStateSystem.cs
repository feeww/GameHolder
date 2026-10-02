using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(PresentationBridgeSystem))]
    [UpdateBefore(typeof(EntitiesGraphicsSystem))]
    public partial class SimulationRenderStateSystem : SystemBase
    {
        private ComponentLookup<EnemyProjectileTag> m_EnemyProjectiles;
        protected override void OnCreate()
        {
            RequireForUpdate<SimulationSnapshot>();
            m_EnemyProjectiles = GetComponentLookup<EnemyProjectileTag>(true);
        }
        protected override void OnUpdate()
        {
            var snapshot = SystemAPI.GetSingleton<SimulationSnapshot>();
            Dependency = new EnemyRenderJob { CameraY = snapshot.PlayerPosition.y }.ScheduleParallel(Dependency);
            m_EnemyProjectiles.Update(this);
            Dependency = new ProjectileRenderJob { CameraY = snapshot.PlayerPosition.y, EnemyProjectiles = m_EnemyProjectiles }.ScheduleParallel(Dependency);
            Dependency = new GemRenderJob { CameraY = snapshot.PlayerPosition.y, Dt = SystemAPI.Time.DeltaTime,
                Generation = snapshot.Generation }.ScheduleParallel(Dependency);
            Dependency = new PlayerRenderJob { CameraY = snapshot.PlayerPosition.y }.Schedule(Dependency);
        }
    }
    [BurstCompile]
    [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
    public partial struct EnemyRenderJob : IJobEntity
    {
        public float CameraY;
        public void Execute(in LocalTransform transform, in MovementVelocity velocity, in TypeId type,
            EnabledRefRO<EnemyActiveTag> active, EnabledRefRW<MaterialMeshInfo> visible,
            ref MaterialMeshInfo mesh, ref LocalToWorld world, ref SpriteUVOffset uv, ref BaseColorOverride color)
        {
            visible.ValueRW = active.ValueRO;
            if (!active.ValueRO) return;
            mesh = MaterialMeshInfo.FromRenderMeshArrayIndices((int)type.Value, 0);
            uv.Value = new float4(1, 1, 0, 0); color.Value = new float4(1);
            float3 position = transform.Position;
            position.z = PresentationDepth.Calculate(position.y, CameraY, math.length(velocity.Value));
            world.Value = float4x4.TRS(position, transform.Rotation, new float3(transform.Scale));
        }
    }
    [BurstCompile]
    [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
    public partial struct ProjectileRenderJob : IJobEntity
    {
        public float CameraY;
        [ReadOnly] public ComponentLookup<EnemyProjectileTag> EnemyProjectiles;
        public void Execute(Entity entity, in LocalTransform transform, in MovementVelocity velocity, in ProjectileData data,
            EnabledRefRO<ProjectileActiveTag> active, EnabledRefRW<MaterialMeshInfo> visible, ref LocalToWorld world, ref BaseColorOverride color)
        {
            visible.ValueRW = active.ValueRO;
            if (!active.ValueRO) return;
            color.Value = EnemyProjectiles.HasComponent(entity)
                ? (data.Damage == SimulationConstants.RangedSniperProjectileDamage ? new float4(1, .45f, .1f, 1) : new float4(.9f, .3f, 1, 1))
                : new float4(.2f, .9f, 1, 1);
            float3 position = transform.Position;
            position.z = PresentationDepth.Calculate(position.y, CameraY, math.length(velocity.Value));
            world.Value = float4x4.TRS(position, transform.Rotation, new float3(transform.Scale));
        }
    }
    [BurstCompile]
    [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
    public partial struct GemRenderJob : IJobEntity
    {
        public float CameraY, Dt;
        public uint Generation;
        public void Execute(in LocalTransform transform, in GemData gem, EnabledRefRO<GemActiveTag> active,
            EnabledRefRW<MaterialMeshInfo> visible, ref LocalToWorld world, ref SpriteUVOffset uv,
            ref BaseColorOverride color, ref GemVisualState visual)
        {
            visible.ValueRW = active.ValueRO;
            if (visual.Generation != Generation) visual = new GemVisualState { Generation = Generation };
            if (!active.ValueRO) { visual.WasActive = 0; visual.FlashTimer = 0; return; }
            if (visual.WasActive != 0 && visual.Experience < gem.ExperienceValue) visual.FlashTimer = .18f;
            visual.Experience = gem.ExperienceValue; visual.WasActive = 1;
            visual.FlashTimer = math.max(0, visual.FlashTimer - Dt);
            uv.Value = PresentationDepth.TierUV(gem.Tier);
            color.Value = math.lerp(PresentationDepth.TierColor(gem.Tier), new float4(2, 2, 2, 1), math.saturate(visual.FlashTimer / .18f));
            float3 position = transform.Position;
            position.z = PresentationDepth.Calculate(position.y, CameraY) + SimulationConstants.GemZOffset;
            world.Value = float4x4.TRS(position, transform.Rotation, new float3(transform.Scale));
        }
    }
    [BurstCompile]
    [WithAll(typeof(PlayerTag))]
    public partial struct PlayerRenderJob : IJobEntity
    {
        public float CameraY;
        public void Execute(in LocalTransform transform, in MovementVelocity velocity, ref LocalToWorld world)
        {
            float3 position = transform.Position;
            position.z = PresentationDepth.Calculate(position.y, CameraY, math.length(velocity.Value));
            world.Value = float4x4.TRS(position, transform.Rotation, new float3(transform.Scale));
        }
    }
}

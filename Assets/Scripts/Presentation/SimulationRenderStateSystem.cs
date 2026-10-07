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
        protected override void OnCreate()
        {
            RequireForUpdate<SimulationSnapshot>();
        }
        protected override void OnUpdate()
        {
            var snapshot = SystemAPI.GetSingleton<SimulationSnapshot>();
            Dependency = new EnemyRenderJob { CameraY = snapshot.PlayerPosition.y,
                Catalog = SystemAPI.GetSingleton<EnemyConfigCatalogSingleton>().Catalog }.ScheduleParallel(Dependency);
            Dependency = new ProjectileRenderJob { CameraY = snapshot.PlayerPosition.y }.ScheduleParallel(Dependency);
            Dependency = new GemRenderJob { CameraY = snapshot.PlayerPosition.y,
                Dt = snapshot.InventoryOpen != 0 || snapshot.Rewards.Active != 0 ? 0 : SystemAPI.Time.DeltaTime,
                Generation = snapshot.Generation }.ScheduleParallel(Dependency);
            Dependency = new PlayerRenderJob { CameraY = snapshot.PlayerPosition.y }.Schedule(Dependency);
        }
    }
    [BurstCompile]
    [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
    public partial struct EnemyRenderJob : IJobEntity
    {
        public float CameraY;
        public BlobAssetReference<EnemyConfigCatalog> Catalog;
        public void Execute(in LocalTransform transform, in MovementVelocity velocity, in TypeId type,
            EnabledRefRO<EnemyActiveTag> active, EnabledRefRW<MaterialMeshInfo> visible,
            ref MaterialMeshInfo mesh, ref LocalToWorld world, ref SpriteUVOffset uv, ref BaseColorOverride color)
        {
            visible.ValueRW = active.ValueRO;
            if (!active.ValueRO) return;
            mesh = MaterialMeshInfo.FromRenderMeshArrayIndices((int)type.Value, 0);
            uv.Value = new float4(1, 1, 0, 0); color.Value = Catalog.Value.GetConfig(type).Tint;
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
        public void Execute(in LocalTransform transform, in MovementVelocity velocity, in ProjectileData data,
            in ExplosiveProjectile explosive, in LaserBeam beam,
            EnabledRefRO<ExplosiveProjectile> isExplosive, EnabledRefRO<LaserBeam> isLaser,
            EnabledRefRO<ProjectileActiveTag> active, EnabledRefRW<MaterialMeshInfo> visible,
            ref MaterialMeshInfo mesh, ref LocalToWorld world, ref BaseColorOverride color)
        {
            visible.ValueRW = active.ValueRO;
            if (!active.ValueRO) return;
            bool blast = isExplosive.ValueRO && explosive.Detonated != 0;
            mesh = MaterialMeshInfo.FromRenderMeshArrayIndices(blast ? 0 : data.MaterialIndex > 1 ? data.MaterialIndex : isLaser.ValueRO ? 1 : 0, 0);
            float4 tint = data.Color;
            float3 position = transform.Position;
            position.z = PresentationDepth.Calculate(position.y, CameraY,
                isExplosive.ValueRO || isLaser.ValueRO ? 0 : math.length(velocity.Value));
            quaternion rotation = transform.Rotation;
            float3 scale;
            if (isLaser.ValueRO)
            {
                rotation = quaternion.RotateZ(math.atan2(beam.Direction.y, beam.Direction.x) - math.PI * .5f);
                scale = new float3(data.Radius * 2, beam.Length, 1);
            }
            else
            {
                float radius = blast ? explosive.BlastRadius : data.Radius;
                float2 size = radius * 2 * (!blast && data.MaterialIndex > 1 ? data.TextureScale : new float2(1));
                position.y -= size.y * .5f;
                scale = new float3(size, 1);
                if (isExplosive.ValueRO) rotation = quaternion.identity;
                if (blast) tint.w *= PresentationConstants.BlastAlphaScale;
            }
            color.Value = tint;
            world.Value = float4x4.TRS(position, rotation, scale);
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
            ref BaseColorOverride color, ref GemVisualState visual, ref MaterialMeshInfo mesh)
        {
            visible.ValueRW = active.ValueRO;
            if (visual.Generation != Generation) visual = new GemVisualState { Generation = Generation };
            if (!active.ValueRO) { visual.WasActive = 0; visual.FlashTimer = 0; return; }
            if (visual.WasActive != 0 && visual.Experience < gem.ExperienceValue) visual.FlashTimer = PresentationConstants.GemFlashDuration;
            visual.Experience = gem.ExperienceValue; visual.WasActive = 1;
            visual.FlashTimer = math.max(0, visual.FlashTimer - Dt);
            mesh = MaterialMeshInfo.FromRenderMeshArrayIndices(gem.IsChest != 0 ? 1 : 0, 0);
            uv.Value = gem.IsChest != 0 ? new float4(1, 1, 0, 0) : PresentationDepth.TierUV(gem.Tier);
            color.Value = gem.IsChest != 0 ? new float4(1) : math.lerp(PresentationDepth.TierColor(gem.Tier), PresentationConstants.GemFlashColor, math.saturate(visual.FlashTimer / PresentationConstants.GemFlashDuration));
            float3 position = transform.Position;
            position.z = PresentationDepth.Calculate(position.y, CameraY) + PresentationConstants.GemZOffset;
            world.Value = float4x4.TRS(position, transform.Rotation, gem.IsChest != 0 ? new float3(1.1f, .85f, 1) : new float3(transform.Scale));
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

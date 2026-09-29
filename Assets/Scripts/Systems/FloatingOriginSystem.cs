using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerInputAndMotionSystem))]
    public partial struct FloatingOriginSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FloatingOriginConfig>();
            state.RequireForUpdate<SimulationCameraBounds>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var originConfig = SystemAPI.GetSingleton<FloatingOriginConfig>();
            ref var cameraBounds = ref SystemAPI.GetSingletonRW<SimulationCameraBounds>().ValueRW;

            float2 playerPos = float2.zero;
            bool shouldRebase = false;

            foreach (var transform in SystemAPI.Query<RefRW<LocalTransform>>().WithAll<PlayerTag>())
            {
                playerPos = transform.ValueRO.Position.xy;
                if (math.lengthsq(playerPos) >= originConfig.ThresholdSq)
                {
                    shouldRebase = true;
                    transform.ValueRW.Position = new float3(0.0f, 0.0f, transform.ValueRO.Position.z);
                }
                break;
            }

            if (!shouldRebase) return;

            float2 rebaseDelta = playerPos;
            cameraBounds.CameraPosition -= rebaseDelta;

            // Shift all non-player entities in parallel
            state.Dependency = new ShiftPositionsJob
            {
                RebaseDelta = rebaseDelta
            }.ScheduleParallel(state.Dependency);

            // Shift cached gem positions in GemPoolSingleton
            if (SystemAPI.HasSingleton<GemPoolSingleton>())
            {
                ref var gemPool = ref SystemAPI.GetSingletonRW<GemPoolSingleton>().ValueRW;
                for (int i = 0; i < gemPool.AllGems.Length; i++)
                {
                    var record = gemPool.AllGems[i];
                    record.Position -= rebaseDelta;
                    gemPool.AllGems[i] = record;
                }
            }

            // Dispatch origin rebase delta event to presentation bridge
            if (SystemAPI.HasSingleton<SimulationBridgeQueuesSingleton>())
            {
                var bridge = SystemAPI.GetSingleton<SimulationBridgeQueuesSingleton>();
                bridge.RebaseEventQueue.Enqueue(new OriginRebaseEvent { Delta = rebaseDelta });
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    [WithNone(typeof(PlayerTag))]
    public partial struct ShiftPositionsJob : IJobEntity
    {
        public float2 RebaseDelta;

        public void Execute(ref LocalTransform transform)
        {
            transform.Position.xy -= RebaseDelta;
        }
    }
}

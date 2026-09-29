using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct PlayerInputAndMotionSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationCameraBounds>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0.0f) return;

            ref var cameraBounds = ref SystemAPI.GetSingletonRW<SimulationCameraBounds>().ValueRW;

            foreach (var (transform, velocity, invuln, stats, input) in
                     SystemAPI.Query<RefRW<LocalTransform>, RefRW<MovementVelocity>, RefRW<PlayerInvulnerability>, RefRO<PlayerStats>, RefRO<PlayerInputData>>()
                         .WithAll<PlayerTag>())
            {
                if (stats.ValueRO.IsDead != 0)
                {
                    velocity.ValueRW.Value = float2.zero;
                    return;
                }

                float2 moveDir = input.ValueRO.MoveInput;
                float dirLenSq = math.lengthsq(moveDir);
                if (dirLenSq > 1.0f)
                {
                    moveDir = math.normalize(moveDir);
                }

                float2 targetVel = moveDir * stats.ValueRO.MoveSpeed;
                velocity.ValueRW.Value = targetVel;

                float3 pos = transform.ValueRO.Position;
                pos.xy += targetVel * dt;
                transform.ValueRW.Position = pos;

                // Decrement invulnerability timer
                invuln.ValueRW.Timer = math.max(0.0f, invuln.ValueRO.Timer - dt);

                // Synchronously update authoritative camera position anchor
                cameraBounds.CameraPosition = pos.xy;
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}

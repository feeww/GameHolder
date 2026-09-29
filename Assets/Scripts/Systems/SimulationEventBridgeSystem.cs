using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial struct SimulationEventBridgeSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationBridgeQueuesSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // System completes dependencies so presentation group on main thread can safely drain unmanaged queues
            state.CompleteDependency();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}

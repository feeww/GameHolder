using Unity.Entities;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateBefore(typeof(SimulationBootstrapSystem))]
    public partial class SimulationRateBootstrapSystem : SystemBase
    {
        protected override void OnCreate()
        {
            World.GetOrCreateSystemManaged<FixedStepSimulationSystemGroup>()
                .SetRateManagerCreateAllocator(new SingleTickRateManager());
            Enabled = false;
        }

        protected override void OnUpdate() { }
    }

    public sealed class SingleTickRateManager : IRateManager
    {
        private readonly RateUtils.FixedRateSimpleManager m_Ticks = new RateUtils.FixedRateSimpleManager(SimulationConstants.FixedTimestep);
        private double m_LastFrameTime, m_Accumulator;
        private bool m_Started, m_Updating;
        public float Timestep { get => m_Ticks.Timestep; set => m_Ticks.Timestep = value; }

        public bool ShouldGroupUpdate(ComponentSystemGroup group)
        {
            if (m_Updating)
            {
                m_Updating = false;
                return m_Ticks.ShouldGroupUpdate(group);
            }
            double now = group.World.Time.ElapsedTime;
            if (!m_Started)
            {
                m_Started = true;
                m_Accumulator = Timestep;
            }
            else m_Accumulator += math.max(0, now - m_LastFrameTime);
            m_LastFrameTime = now;
            if (m_Accumulator + 1e-9 < Timestep) return false;
            // Discard complete overdue ticks; keep the fractional phase for frames faster than 60 Hz.
            m_Accumulator = math.max(0, m_Accumulator - Timestep) % Timestep;
            m_Updating = m_Ticks.ShouldGroupUpdate(group);
            return m_Updating;
        }
    }
}

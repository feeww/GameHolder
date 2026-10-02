using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace GameHolder.PureDots
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(SimulationRenderStateSystem))]
    public partial class PresentationBridgeSystem : SystemBase
    {
        private double2 m_WorldOrigin;
        private uint m_Generation;
        private readonly float2[] m_DeathPositions = new float2[4];
        private readonly int[] m_DeathCounts = new int[4];
        protected override void OnCreate() => RequireForUpdate<SimulationJobFence>();
        protected override void OnUpdate()
        {
            // Synchronize once at the actual managed-consumer boundary, with the producers' handle.
            SystemAPI.GetSingleton<SimulationJobFence>().Handle.Complete();
            var snapshot = SystemAPI.GetSingleton<SimulationSnapshot>();
            var queues = SystemAPI.GetSingleton<SimulationBridgeQueuesSingleton>();
            var particles = BatchedParticleManager.Instance;
            var audio = AudioThrottlingManager.Instance;
            var camera = CameraPresentationController.Instance;
            var bootstrap = GamePresentationBootstrap.Instance;
            if (audio != null) audio.ResetFrameCounters();
            if (particles != null) particles.BeginFrame();
            if (snapshot.Generation != m_Generation)
            {
                m_Generation = snapshot.Generation;
                m_WorldOrigin = snapshot.WorldOrigin;
                if (particles != null) particles.ClearAll();
                if (audio != null) audio.StopAll();
                if (camera != null) camera.UpdateCameraPosition(snapshot.PlayerPosition, 0);
                if (bootstrap != null) bootstrap.ResetFloorPhase();
            }
            else
            {
                float2 delta = (float2)(snapshot.WorldOrigin - m_WorldOrigin);
                if (math.any(delta != 0))
                {
                    if (camera != null) camera.ApplyRebaseOffset(delta);
                    if (particles != null) particles.ShiftAllParticles(delta);
                    if (bootstrap != null) bootstrap.ApplyFloorRebase(delta);
                }
                m_WorldOrigin = snapshot.WorldOrigin;
            }
            for (int i = 0; i < 4; i++) { m_DeathCounts[i] = 0; m_DeathPositions[i] = float2.zero; }
            while (queues.DeathEventQueue.TryDequeue(out var death))
            {
                if (death.TypeId == SimulationConstants.PlayerTypeId)
                {
                    if (particles != null) particles.EmitDeathBurst(death.Position, 32);
                    if (audio != null) audio.PlaySoundThrottled(GamePresentationBootstrap.PlayerHitClip);
                }
                else
                {
                    int type = (int)math.min(death.TypeId, 3);
                    m_DeathPositions[type] += death.Position; m_DeathCounts[type]++;
                }
            }
            for (int i = 0; i < 4; i++)
            {
                if (m_DeathCounts[i] == 0) continue;
                if (particles != null) particles.EmitDeathBurst(m_DeathPositions[i] / m_DeathCounts[i], math.min(32, m_DeathCounts[i] * 8));
                if (audio != null) audio.PlaySoundThrottled(GamePresentationBootstrap.EnemyDeathClip, .45f);
            }
            bool gotHit = false;
            while (queues.HitReactionEventQueue.TryDequeue(out _)) gotHit = true;
            if (gotHit)
            {
                if (particles != null) particles.EmitHitBurst(snapshot.PlayerPosition, 6);
                if (audio != null) audio.PlaySoundThrottled(GamePresentationBootstrap.PlayerHitClip, .6f);
            }
            float2 gemPosition = float2.zero; int gems = 0;
            while (queues.GemCollectEventQueue.TryDequeue(out var gem)) { gemPosition += gem.Position; gems++; }
            if (gems > 0)
            {
                if (particles != null) particles.EmitGemCollectBurst(gemPosition / gems, math.min(32, gems * 6));
                if (audio != null) audio.PlaySoundThrottled(GamePresentationBootstrap.GemCollectClip, .35f);
            }
            if (camera != null) camera.UpdateCameraPosition(snapshot.PlayerPosition, SystemAPI.Time.DeltaTime);
            if (PureDotsHUD.Instance != null)
                PureDotsHUD.Instance.ApplySnapshot(snapshot, SystemAPI.GetSingleton<SimulationCommandQueue>().Commands);
        }
        protected override void OnDestroy()
        {
            if (PureDotsHUD.Instance != null) PureDotsHUD.Instance.Unbind();
        }
    }
}
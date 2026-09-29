using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace GameHolder.PureDots
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class PresentationBridgeSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<SimulationBridgeQueuesSingleton>();
            RequireForUpdate<SimulationCameraBounds>();
        }

        protected override void OnUpdate()
        {
            var bridgeQueues = SystemAPI.GetSingleton<SimulationBridgeQueuesSingleton>();
            var cameraBounds = SystemAPI.GetSingleton<SimulationCameraBounds>();

            float dt = SystemAPI.Time.DeltaTime;

            // 1. Process Floating Origin Rebase Deltas
            while (bridgeQueues.RebaseEventQueue.TryDequeue(out OriginRebaseEvent rebaseEv))
            {
                if (CameraPresentationController.Instance != null)
                {
                    CameraPresentationController.Instance.ApplyRebaseOffset(rebaseEv.Delta);
                }

                if (BatchedParticleManager.Instance != null)
                {
                    BatchedParticleManager.Instance.ShiftAllParticles(rebaseEv.Delta);
                }

                if (PureDotsHUD.Instance != null)
                {
                    PureDotsHUD.Instance.NotifyRebase();
                }
            }

            // 2. Process Enemy & Player Death Events
            while (bridgeQueues.DeathEventQueue.TryDequeue(out DeathEvent deathEv))
            {
                if (deathEv.TypeId == 999) // Player death
                {
                    if (BatchedParticleManager.Instance != null)
                    {
                        BatchedParticleManager.Instance.EmitDeathBurst(deathEv.Position, 32);
                    }
                    if (AudioThrottlingManager.Instance != null && GamePresentationBootstrap.PlayerHitClip != null)
                    {
                        AudioThrottlingManager.Instance.PlaySoundThrottled(GamePresentationBootstrap.PlayerHitClip, 1.0f);
                    }
                }
                else // Enemy death
                {
                    if (BatchedParticleManager.Instance != null)
                    {
                        BatchedParticleManager.Instance.EmitDeathBurst(deathEv.Position, 8);
                    }
                    if (AudioThrottlingManager.Instance != null && GamePresentationBootstrap.EnemyDeathClip != null)
                    {
                        AudioThrottlingManager.Instance.PlaySoundThrottled(GamePresentationBootstrap.EnemyDeathClip, 0.45f);
                    }
                }
            }

            // 3. Process Player Hit Reactions
            while (bridgeQueues.HitReactionEventQueue.TryDequeue(out PlayerHitReactionEvent hitEv))
            {
                if (BatchedParticleManager.Instance != null)
                {
                    BatchedParticleManager.Instance.EmitHitBurst(cameraBounds.CameraPosition, 6);
                }
                if (AudioThrottlingManager.Instance != null && GamePresentationBootstrap.PlayerHitClip != null)
                {
                    AudioThrottlingManager.Instance.PlaySoundThrottled(GamePresentationBootstrap.PlayerHitClip, 0.6f);
                }
            }

            // 4. Process Gem Collection Events
            while (bridgeQueues.GemCollectEventQueue.TryDequeue(out GemCollectEvent gemEv))
            {
                if (BatchedParticleManager.Instance != null)
                {
                    BatchedParticleManager.Instance.EmitGemCollectBurst(gemEv.Position, 6);
                }
                if (AudioThrottlingManager.Instance != null && GamePresentationBootstrap.GemCollectClip != null)
                {
                    AudioThrottlingManager.Instance.PlaySoundThrottled(GamePresentationBootstrap.GemCollectClip, 0.35f);
                }
                if (PureDotsHUD.Instance != null)
                {
                    PureDotsHUD.Instance.NotifyGemCollected(gemEv.ExperienceValue);
                }
            }

            // 5. Update Camera Position smoothly
            if (CameraPresentationController.Instance != null)
            {
                CameraPresentationController.Instance.UpdateCameraPosition(cameraBounds.CameraPosition, dt);
            }

            // 6. Reset audio throttle counters for the next frame
            if (AudioThrottlingManager.Instance != null)
            {
                AudioThrottlingManager.Instance.ResetFrameCounters();
            }
        }
    }
}

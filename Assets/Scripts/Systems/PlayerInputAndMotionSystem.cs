using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile]
    public struct SimulationControlJob : IJob
    {
        public SimulationAccess A;
        public float Dt;
        public void Execute()
        {
            var run = A.Run[A.State];
            run.RebaseDelta = float2.zero;
            while (A.Commands.TryDequeue(out var command))
            {
                switch (command.Kind)
                {
                    case SimulationCommandKind.SpawnExtra:
                        run.ExtraSpawns = math.min(SimulationConstants.MaxEnemies, run.ExtraSpawns + math.max(0, command.Value)); break;
                    case SimulationCommandKind.GodMode:
                        run.GodMode = (byte)(command.Value != 0 ? 1 : 0);
                        if (run.GodMode != 0)
                        {
                            var healed = A.Stats[run.Player]; healed.CurrentHealth = healed.MaxHealth; healed.IsDead = 0;
                            A.Stats[run.Player] = healed;
                        }
                        break;
                    case SimulationCommandKind.AutoAttack: run.AutoAttack = (byte)(command.Value != 0 ? 1 : 0); break;
                    case SimulationCommandKind.ForceRebase: run.ForceRebase = 1; break;
                    case SimulationCommandKind.KillAll: run.KillAllPending = 1; break;
                    case SimulationCommandKind.Restart: Reset(ref run); break;
                    case SimulationCommandKind.SelectReward:
                        var rewardStats = A.Stats[run.Player];
                        var rewardWeapon = A.Weapons[run.Player];
                        if (RewardRoll.Select(ref run, ref rewardStats, ref rewardWeapon, A.StartingPlayer, ref A.Rewards.Value, command))
                        { A.Stats[run.Player] = rewardStats; A.Weapons[run.Player] = rewardWeapon; }
                        break;
                }
            }
            if (run.Rewards.Active != 0)
            {
                run.PlayerVelocity = float2.zero;
                A.Velocities[run.Player] = default;
                A.Run[A.State] = run;
                return;
            }
            if (run.KillAllPending != 0)
            {
                run.KillAllPending = 0;
                for (int i = 0; i < A.EnemyPool.AllEnemies.Length; i++)
                {
                    Entity e = A.EnemyPool.AllEnemies[i];
                    if (A.Enemies.IsComponentEnabled(e)) A.Damage.Enqueue(new DamageEvent
                    { TargetEntity = e, TargetKey = DamageEvent.CreateTargetKey(e), Damage = float.MaxValue });
                }
            }
            var stats = A.Stats[run.Player];
            var invulnerability = A.Invulnerability[run.Player];
            invulnerability.Timer = math.max(0, invulnerability.Timer - Dt);
            A.Invulnerability[run.Player] = invulnerability;
            run.PreviousPlayerPosition = A.Transforms[run.Player].Position.xy;
            float2 movement = A.Input[A.InputEntity].Movement;
            run.PlayerVelocity = stats.IsDead == 0 ? movement / math.max(1, math.length(movement)) * stats.MoveSpeed : float2.zero;
            run.PlayerVelocity *= CrowdSpeedScale(run.PreviousPlayerPosition, run.PlayerVelocity * Dt, run.PlayerCollisionRadius);
            run.PlayerPosition = run.PreviousPlayerPosition + run.PlayerVelocity * Dt;
            if (run.ForceRebase != 0 || math.lengthsq(run.PlayerPosition) > SimulationConstants.FloatingOriginThresholdSq)
            {
                // Debug teleport deliberately uses a non-tile-aligned delta to exercise phase preservation.
                if (run.ForceRebase != 0) run.PlayerPosition += SimulationConstants.DebugRebaseOffset;
                run.RebaseDelta = run.PlayerPosition;
                run.WorldOrigin += (double2)run.RebaseDelta;
                run.PreviousPlayerPosition -= run.RebaseDelta;
                run.PlayerPosition = float2.zero;
                run.ForceRebase = 0;
                ShiftPools(run.RebaseDelta);
            }
            A.Transforms[run.Player] = LocalTransform.FromPosition(new float3(run.PlayerPosition, 0));
            A.Previous[run.Player] = new PreviousPosition { Value = run.PreviousPlayerPosition };
            A.Velocities[run.Player] = new MovementVelocity { Value = run.PlayerVelocity };
            run.Tick++;
            A.Run[A.State] = run;
        }
        private float CrowdSpeedScale(float2 position, float2 step, float playerRadius)
        {
            float stepSq = math.lengthsq(step);
            if (stepSq < NumericalConstants.MinimumSweepLengthSq) return 1;
            float stepLength = math.sqrt(stepSq);
            float2 direction = step / stepLength;
            float load = 0, pushScale = 1;
            // One exact O(N) sweep for the player; density must not be truncated by the enemy query budget.
            for (int i = 0; i < A.EnemyPool.AllEnemies.Length; i++)
            {
                Entity e = A.EnemyPool.AllEnemies[i];
                if (!A.Enemies.IsComponentEnabled(e)) continue;
                float2 offset = A.Transforms[e].Position.xy - position;
                if (math.dot(offset, direction) < 0) continue;
                var config = A.Catalog.Value.Configs[(int)A.Types[e].Value];
                float clearance = playerRadius + config.CollisionRadius + CrowdConstants.PlayerContactSkin;
                if (SweptCollision.TryHit(offset, offset - step, clearance, out float contactTime))
                    pushScale = math.min(pushScale, contactTime + CrowdConstants.PlayerCrowdPushSpeed * Dt / (math.max(1, config.Mass) * stepLength));
                float radius = playerRadius + config.CollisionRadius +
                    CrowdConstants.PlayerContactSkin + CrowdConstants.CrowdSteeringMargin;
                float t = math.saturate(math.dot(offset, step) / stepSq);
                float weight = math.saturate(1 - math.length(offset - step * t) / radius);
                load += weight * weight * config.Mass;
            }
            // The minimum input speed wins at extreme mass/density, so the player can always escape.
            return math.max(CrowdConstants.PlayerCrowdMinimumSpeed, math.min(pushScale, 1 / (1 + load * CrowdConstants.PlayerCrowdResistance)));
        }
        private void Reset(ref SimulationRunState run)
        {
            uint generation = run.Generation + 1;
            Entity player = run.Player;
            byte godMode = run.GodMode, autoAttack = run.AutoAttack;
            run = new SimulationRunState { Player = player, Generation = generation, GodMode = godMode, AutoAttack = autoAttack,
                Loadout = RewardRoll.StartingLoadout(), Rewards = new RewardSelection { RandomState = RewardRoll.SeedForRun(ref A.Rewards.Value, generation) },
                PlayerCollisionRadius = A.StartingPlayer.Stats.CollisionRadius };
            A.Stats[player] = A.StartingPlayer.Stats;
            A.Weapons[player] = A.StartingPlayer.Weapon;
            A.Invulnerability[player] = new PlayerInvulnerability
            { Timer = A.StartingPlayer.RespawnGracePeriod, InvulnerabilityDuration = A.StartingPlayer.InvulnerabilityDuration };
            A.Transforms[player] = LocalTransform.Identity;
            A.Previous[player] = default;
            A.Waves[A.Wave] = RunDefaults.Wave;
            A.Grid.Clear(); A.CrowdCells.Clear(); A.Damage.Clear(); A.PlayerDamage.Clear(); A.Deactivations.Clear(); A.GemSpawns.Clear();
            A.Bridge.DeathEventQueue.Clear(); A.Bridge.HitReactionEventQueue.Clear();
            A.Bridge.GemCollectEventQueue.Clear(); A.Bridge.RebaseEventQueue.Clear();
            A.EnemyPool.InactiveEnemies.Clear();
            for (int i = 0; i < A.EnemyPool.AllEnemies.Length; i++)
            {
                Entity e = A.EnemyPool.AllEnemies[i];
                A.Enemies.SetComponentEnabled(e, false); A.Ranged.SetComponentEnabled(e, false);
                A.Separation[e] = default; A.MeleeCooldown[e] = default; A.RangedCooldown[e] = default;
                A.Velocities[e] = default; A.Previous[e] = default; A.Transforms[e] = LocalTransform.Identity;
                A.EnemyPool.InactiveEnemies.Enqueue(e);
            }
            ResetProjectiles(A.PlayerPool.AllProjectiles, A.PlayerPool.InactiveProjectiles);
            ResetProjectiles(A.EnemyProjectilePool.AllProjectiles, A.EnemyProjectilePool.InactiveProjectiles);
            A.GemPool.FreeGems.Clear();
            for (int i = 0; i < A.GemPool.AllGems.Length; i++)
            {
                var record = A.GemPool.AllGems[i];
                record.Position = float2.zero; record.IsActive = 0; record.ExperienceValue = 0; record.Tier = 0;
                A.GemPool.AllGems[i] = record;
                A.GemData[record.Entity] = new GemData { SlotIndex = (uint)i };
                A.Gems.SetComponentEnabled(record.Entity, false);
                A.Transforms[record.Entity] = LocalTransform.Identity;
                A.GemPool.FreeGems.Enqueue(record.Entity);
            }
        }
        private void ResetProjectiles(Unity.Collections.LowLevel.Unsafe.UnsafeList<Entity> entities,
            Unity.Collections.UnsafeQueue<Entity> pool)
        {
            pool.Clear();
            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                A.Projectiles.SetComponentEnabled(e, false); A.ProjectileData[e] = default;
                if (A.Explosives.HasComponent(e)) { A.Explosives.SetComponentEnabled(e, false); A.Explosives[e] = default; }
                if (A.Lasers.HasComponent(e)) { A.Lasers.SetComponentEnabled(e, false); A.Lasers[e] = default; }
                A.Transforms[e] = LocalTransform.Identity; A.Previous[e] = default; A.Velocities[e] = default;
                pool.Enqueue(e);
            }
        }
        private void ShiftPools(float2 delta)
        {
            int deaths = A.Bridge.DeathEventQueue.Count;
            for (int i = 0; i < deaths; i++)
            {
                A.Bridge.DeathEventQueue.TryDequeue(out var death); death.Position -= delta;
                A.Bridge.DeathEventQueue.Enqueue(death);
            }
            int collected = A.Bridge.GemCollectEventQueue.Count;
            for (int i = 0; i < collected; i++)
            {
                A.Bridge.GemCollectEventQueue.TryDequeue(out var gem); gem.Position -= delta;
                A.Bridge.GemCollectEventQueue.Enqueue(gem);
            }
            for (int i = 0; i < A.EnemyPool.AllEnemies.Length; i++)
            {
                Entity e = A.EnemyPool.AllEnemies[i];
                if (A.Enemies.IsComponentEnabled(e)) Shift(e, delta);
            }
            for (int i = 0; i < A.PlayerPool.AllProjectiles.Length; i++)
                if (A.Projectiles.IsComponentEnabled(A.PlayerPool.AllProjectiles[i])) Shift(A.PlayerPool.AllProjectiles[i], delta);
            for (int i = 0; i < A.EnemyProjectilePool.AllProjectiles.Length; i++)
                if (A.Projectiles.IsComponentEnabled(A.EnemyProjectilePool.AllProjectiles[i])) Shift(A.EnemyProjectilePool.AllProjectiles[i], delta);
            for (int i = 0; i < A.GemPool.AllGems.Length; i++)
            {
                var record = A.GemPool.AllGems[i];
                if (record.IsActive == 0) continue;
                record.Position -= delta; A.GemPool.AllGems[i] = record;
                Shift(record.Entity, delta);
            }
        }
        private void Shift(Entity e, float2 delta)
        {
            var transform = A.Transforms[e]; transform.Position.xy -= delta; A.Transforms[e] = transform;
            if (A.Previous.HasComponent(e))
            { var previous = A.Previous[e]; previous.Value -= delta; A.Previous[e] = previous; }
        }
    }
}

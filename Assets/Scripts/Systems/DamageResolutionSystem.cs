using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public struct EntityOrder : IComparer<Entity>
    {
        public int Compare(Entity x, Entity y)
        {
            ulong a = DamageEvent.CreateTargetKey(x), b = DamageEvent.CreateTargetKey(y);
            return a < b ? -1 : a > b ? 1 : 0;
        }
    }
    [BurstCompile]
    public struct DamageResolutionJob : IJob
    {
        public SimulationAccess A;
        public NativeList<DamageEvent> Damage;
        public NativeList<Entity> Deactivations;
        public NativeParallelHashMap<Entity, float> AreaDamage;
        public void Execute()
        {
            var run = A.Run[A.State];
            if (run.Rewards.Active != 0) return;
            Damage.Clear();
            // Area weapons stage at most one event per enemy, regardless of overlapping shots.
            foreach (var entry in AreaDamage)
                Damage.Add(new DamageEvent { TargetEntity = entry.Key, TargetKey = DamageEvent.CreateTargetKey(entry.Key), Damage = entry.Value });
            AreaDamage.Clear();
            while (A.Damage.TryDequeue(out var damage)) Damage.Add(damage);
            Damage.AsArray().Sort(new DamageEventComparator());
            for (int i = 0; i < Damage.Length;)
            {
                var first = Damage[i++]; float amount = first.Damage;
                while (i < Damage.Length && Damage[i].TargetKey == first.TargetKey) amount += Damage[i++].Damage;
                Entity e = first.TargetEntity;
                if (!A.Enemies.HasComponent(e) || !A.Enemies.IsComponentEnabled(e)) continue;
                var health = A.Health[e]; health.Value -= amount; A.Health[e] = health;
                if (health.Value > 0) continue;
                A.Enemies.SetComponentEnabled(e, false); A.Ranged.SetComponentEnabled(e, false);
                A.EnemyPool.InactiveEnemies.Enqueue(e); run.ActiveEnemies--; run.Kills++;
                float2 position = A.Transforms[e].Position.xy; uint type = A.Types[e].Value;
                A.GemSpawns.Enqueue(new GemSpawnRequest { Position = position, ExperienceValue = A.Catalog.Value.Configs[(int)type].ExperienceValue });
                if (A.Bridge.DeathEventQueue.Count < SimulationConstants.CosmeticQueueCapacity)
                    A.Bridge.DeathEventQueue.Enqueue(new DeathEvent { Position = position, TypeId = type });
            }
            Deactivations.Clear();
            while (A.Deactivations.TryDequeue(out var e)) Deactivations.Add(e);
            Deactivations.AsArray().Sort(new EntityOrder());
            for (int i = 0; i < Deactivations.Length; i++)
            {
                Entity e = Deactivations[i];
                if (!A.Projectiles.IsComponentEnabled(e)) continue;
                A.Projectiles.SetComponentEnabled(e, false);
                if (A.PlayerProjectiles.HasComponent(e)) { A.PlayerPool.InactiveProjectiles.Enqueue(e); run.PlayerProjectiles--; }
                else { A.EnemyProjectilePool.InactiveProjectiles.Enqueue(e); run.EnemyProjectiles--; }
            }
            PlayerDamageEvent best = default; bool hasHit = false;
            while (A.PlayerDamage.TryDequeue(out var hit))
            {
                if (!(hit.Damage > 0)) continue;
                if (!hasHit || hit.HitTime < best.HitTime || hit.HitTime == best.HitTime &&
                    DamageEvent.CreateTargetKey(hit.SourceEntity) < DamageEvent.CreateTargetKey(best.SourceEntity))
                { best = hit; hasHit = true; }
            }
            var stats = A.Stats[run.Player];
            var invulnerability = A.Invulnerability[run.Player];
            if (hasHit && stats.IsDead == 0 && run.GodMode == 0 && invulnerability.Timer <= 0)
            {
                stats.CurrentHealth = math.max(0, stats.CurrentHealth - best.Damage);
                invulnerability.Timer = invulnerability.InvulnerabilityDuration;
                if (stats.CurrentHealth <= 0)
                {
                    stats.IsDead = 1;
                    if (A.Bridge.DeathEventQueue.Count < SimulationConstants.CosmeticQueueCapacity)
                        A.Bridge.DeathEventQueue.Enqueue(new DeathEvent { Position = run.PlayerPosition, TypeId = CombatConstants.PlayerTypeId });
                }
                else if (A.Bridge.HitReactionEventQueue.Count < SimulationConstants.CosmeticQueueCapacity)
                    A.Bridge.HitReactionEventQueue.Enqueue(new PlayerHitReactionEvent { Damage = best.Damage, HitDirection = best.HitDirection });
                A.Stats[run.Player] = stats; A.Invulnerability[run.Player] = invulnerability;
            }
            A.Run[A.State] = run;
        }
    }
}

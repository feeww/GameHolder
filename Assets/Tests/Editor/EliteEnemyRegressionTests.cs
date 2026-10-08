using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameHolder.PureDots.Tests
{
    public partial class SimulationRegressionTests
    {
        private static EliteEnemyConfig TestElites(float probability) => new EliteEnemyConfig
        {
            SpawnProbability = probability, HealthMultiplier = 3, SpeedMultiplier = 2,
            SizeMultiplier = 1.5f, MassMultiplier = 2,
            DamageMultiplier = 3, ExperienceMultiplier = 2.5f, ChestDropChanceMultiplier = 4
        };

        [TestCase(0f)] [TestCase(.35f)] [TestCase(1f)]
        public void EliteRollUsesWaveRandomOncePerSpawnAndReplaysWithFixedSeed(float probability)
        {
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            var previous = catalog.Value.Elites;
            var pool = m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>();
            uint firstHash = 0;
            try
            {
                catalog.Value.Elites = TestElites(probability);
                for (int replay = 0; replay < 2; replay++)
                {
                    Command(SimulationCommandKind.Restart); Tick(0);
                    var wave = m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave);
                    wave.RandomSeed = 12345; wave.SpawnInterval = 100000;
                    m_Em.SetComponentData(m_Wave, wave);
                    Command(SimulationCommandKind.SpawnExtra, 64); Tick(0);
                    var random = new Unity.Mathematics.Random(12345);
                    uint hash = 0; int elites = 0;
                    for (int i = 0; i < 64; i++)
                    {
                        random.NextFloat(); random.NextFloat(); random.NextFloat(); // Angle, radius, roster choice.
                        byte expected = (byte)(probability > 0 && random.NextFloat() < probability ? 1 : 0);
                        random.NextFloat(); // Initial ranged cooldown, including melee enemies.
                        Entity enemy = pool.AllEnemies[i];
                        var type = m_Em.GetComponentData<TypeId>(enemy);
                        Assert.That(type.IsElite, Is.EqualTo(expected));
                        elites += type.IsElite;
                        float health = m_Em.GetComponentData<CurrentHealth>(enemy).Value;
                        Assert.That(health, Is.EqualTo(catalog.Value.Configs[(int)type.Value].MaxHealth * (expected != 0 ? 3 : 1)));
                        hash = math.hash(new uint4(hash, type.Value, type.IsElite,
                            math.hash(m_Em.GetComponentData<LocalTransform>(enemy).Position)));
                    }
                    Assert.That(m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave).RandomSeed, Is.EqualTo(random.state));
                    Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(64));
                    if (probability == .35f) Assert.That(elites, Is.InRange(1, 63));
                    if (replay == 0) firstHash = hash; else Assert.That(hash, Is.EqualTo(firstHash));
                    uint seed = random.state;
                    Tick(0);
                    Assert.That(m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave).RandomSeed, Is.EqualTo(seed));
                }
            }
            finally { catalog.Value.Elites = previous; }
        }

        [Test]
        public void ElitePoolReuseRestoresStandardHealthRewardsAndTint()
        {
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            var previous = catalog.Value.Elites;
            var pool = m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>();
            Entity enemy = Entity.Null;
            try
            {
                catalog.Value.Elites = TestElites(1);
                Command(SimulationCommandKind.SpawnExtra, 1); Tick(0);
                enemy = pool.AllEnemies[0];
                Assert.That(m_Em.GetComponentData<TypeId>(enemy).IsElite, Is.EqualTo(1));
                m_Em.AddComponent<LocalToWorld>(enemy); m_Em.AddComponent<MaterialMeshInfo>(enemy);
                m_Em.AddComponent<SpriteUVOffset>(enemy); m_Em.AddComponent<BaseColorOverride>(enemy);
                var render = m_World.GetOrCreateSystemManaged<SimulationRenderStateSystem>();
                render.Update(); m_Em.CompleteAllTrackedJobs();
                var type = m_Em.GetComponentData<TypeId>(enemy);
                var tint = m_Em.GetComponentData<BaseColorOverride>(enemy).Value;
                Assert.That(tint.xyz, Is.Not.EqualTo(catalog.Value.Configs[(int)type.Value].Tint.xyz));
                Assert.That(tint.w, Is.EqualTo(catalog.Value.Configs[(int)type.Value].Tint.w));
                Command(SimulationCommandKind.KillAll); Tick(0);
                Assert.That(m_Em.GetComponentData<TypeId>(enemy).IsElite, Is.Zero);
                while (pool.InactiveEnemies.TryDequeue(out _)) { } pool.InactiveEnemies.Enqueue(enemy);
                catalog.Value.Elites = TestElites(0);
                Command(SimulationCommandKind.SpawnExtra, 1); Tick(0);
                type = m_Em.GetComponentData<TypeId>(enemy);
                Assert.That(type.IsElite, Is.Zero);
                Assert.That(m_Em.IsComponentEnabled<EnemyActiveTag>(enemy), Is.True);
                Assert.That(m_Em.GetComponentData<CurrentHealth>(enemy).Value, Is.EqualTo(catalog.Value.Configs[(int)type.Value].MaxHealth));
                Assert.That(catalog.Value.GetConfig(type).ExperienceValue, Is.EqualTo(catalog.Value.Configs[(int)type.Value].ExperienceValue));
                Assert.That(catalog.Value.GetConfig(type).ChestDropChance, Is.EqualTo(catalog.Value.Configs[(int)type.Value].ChestDropChance));
                render.Update(); m_Em.CompleteAllTrackedJobs();
                Assert.That(m_Em.GetComponentData<BaseColorOverride>(enemy).Value, Is.EqualTo(catalog.Value.Configs[(int)type.Value].Tint));
                catalog.Value.Elites = TestElites(1);
                Command(SimulationCommandKind.Restart); Tick(0);
                Assert.That(m_Em.GetComponentData<TypeId>(enemy).IsElite, Is.Zero);
            }
            finally
            {
                catalog.Value.Elites = previous;
                if (enemy != Entity.Null)
                {
                    m_Em.RemoveComponent<LocalToWorld>(enemy); m_Em.RemoveComponent<MaterialMeshInfo>(enemy);
                    m_Em.RemoveComponent<SpriteUVOffset>(enemy); m_Em.RemoveComponent<BaseColorOverride>(enemy);
                }
            }
        }

        [Test]
        public void EliteMovementContactDamageExperienceAndChestChanceUseMultipliers()
        {
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            var previous = catalog.Value.Elites;
            var config = catalog.Value.Configs[0];
            var gems = m_Em.CreateEntityQuery(typeof(GemPoolSingleton)).GetSingleton<GemPoolSingleton>();
            try
            {
                catalog.Value.Elites = TestElites(0);
                catalog.Value.Configs[0].ChestDropChance = .5f;
                Entity enemy = Enemy(new float2(10, 0), 0);
                m_Em.SetComponentData(enemy, new TypeId { IsElite = 1 });
                Tick(.1f);
                Assert.That(m_Em.GetComponentData<LocalTransform>(enemy).Position.x, Is.EqualTo(10 - config.MoveSpeed * 2 * .1f).Within(.0001f));
                m_Em.SetComponentData(enemy, LocalTransform.FromPosition(new float3(.9f, 0, 0)));
                Tick(0);
                Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth - config.BaseDamage * 3));
                m_Em.SetComponentData(enemy, LocalTransform.FromPosition(new float3(20, 0, 0)));
                Command(SimulationCommandKind.KillAll); Tick(0);
                ulong experience = 0;
                for (int i = 0; i < gems.AllGems.Length; i++) if (gems.AllGems[i].IsActive != 0) experience += gems.AllGems[i].ExperienceValue;
                Assert.That(experience, Is.EqualTo((uint)math.round(config.ExperienceValue * 2.5f)));
                Assert.That(gems.FreeChests.Length, Is.EqualTo(GemPoolSingleton.ChestCapacity - 1));
                catalog.Value.Configs[0].ExperienceValue = uint.MaxValue;
                Assert.That(catalog.Value.GetConfig(new TypeId { IsElite = 1 }).ExperienceValue, Is.EqualTo(uint.MaxValue));
                catalog.Value.Elites.ExperienceMultiplier = 0;
                catalog.Value.Elites.ChestDropChanceMultiplier = 0;
                Assert.That(catalog.Value.GetConfig(new TypeId { IsElite = 1 }).ExperienceValue, Is.Zero);
                Assert.That(catalog.Value.GetConfig(new TypeId { IsElite = 1 }).ChestDropChance, Is.Zero);
            }
            finally { catalog.Value.Elites = previous; catalog.Value.Configs[0] = config; }
        }

        [TestCase(1f, 4f)] [TestCase(2f, 1f)] [TestCase(2f, 4f)]
        public void ElitePlayerPushUsesScaledMassAndRadius(float sizeMultiplier, float massMultiplier)
        {
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            var previous = catalog.Value.Elites;
            var config = catalog.Value.Configs[0];
            try
            {
                catalog.Value.Configs[0].Mass = 1;
                catalog.Value.Configs[0].MoveSpeed = 0;
                catalog.Value.Elites = TestElites(0);
                catalog.Value.Elites.SizeMultiplier = sizeMultiplier;
                catalog.Value.Elites.MassMultiplier = massMultiplier;
                float standardStep = 0;
                foreach (byte isElite in new byte[] { 0, 1, 0 })
                {
                    Reset();
                    var type = new TypeId { IsElite = isElite };
                    var effective = catalog.Value.GetConfig(type);
                    float clearance = DefaultPlayer.Stats.CollisionRadius + effective.CollisionRadius + CrowdConstants.PlayerContactSkin;
                    Entity enemy = Enemy(new float2(clearance, 0), 0);
                    m_Em.SetComponentData(enemy, type);
                    m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(1, 0) });
                    Tick(.1f);
                    float step = Snapshot.PlayerPosition.x;
                    if (standardStep == 0) standardStep = step;
                    Assert.That(step, Is.EqualTo(.2f / (isElite != 0 ? massMultiplier : 1)).Within(.0001f));
                    Assert.That(step, Is.LessThanOrEqualTo(standardStep + .0001f));
                    Assert.That(m_Em.GetComponentData<LocalTransform>(enemy).Position.x - step,
                        Is.GreaterThanOrEqualTo(clearance - .0001f));
                }
            }
            finally { catalog.Value.Elites = previous; catalog.Value.Configs[0] = config; }
        }

        [TestCase(WeaponType.Standard)] [TestCase(WeaponType.Explosive)] [TestCase(WeaponType.Laser)]
        public void EliteRangedAttacksScaleDamageForEveryWeapon(WeaponType weaponType)
        {
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            var previous = catalog.Value.Elites;
            var config = catalog.Value.Configs[2];
            try
            {
                catalog.Value.Elites = TestElites(0);
                catalog.Value.Configs[2].Weapon.Type = weaponType;
                Entity enemy = Enemy(new float2(6, 0), 2);
                m_Em.SetComponentData(enemy, new TypeId { Value = 2, IsElite = 1 });
                m_Em.SetComponentData(enemy, new EnemyRangedCooldown());
                Tick(0);
                if (weaponType == WeaponType.Laser) Tick(CombatConstants.EnemyLaserChargeDuration);
                using var projectiles = m_Em.CreateEntityQuery(typeof(EnemyProjectileTag), typeof(ProjectileActiveTag)).ToEntityArray(Allocator.Temp);
                Assert.That(projectiles.Length, Is.GreaterThan(0));
                foreach (Entity projectile in projectiles)
                    Assert.That(m_Em.GetComponentData<ProjectileData>(projectile).Damage, Is.EqualTo(config.Weapon.Damage * 3));
            }
            finally { catalog.Value.Elites = previous; catalog.Value.Configs[2] = config; }
        }

        [TestCase("SpawnProbability", -1f)] [TestCase("SpawnProbability", 2f)] [TestCase("SpawnProbability", float.NaN)]
        [TestCase("HealthMultiplier", 0f)] [TestCase("HealthMultiplier", float.PositiveInfinity)]
        [TestCase("SpeedMultiplier", -1f)] [TestCase("SpeedMultiplier", float.NaN)]
        [TestCase("DamageMultiplier", 0f)] [TestCase("DamageMultiplier", float.NegativeInfinity)]
        [TestCase("ExperienceMultiplier", -1f)] [TestCase("ExperienceMultiplier", float.NaN)]
        [TestCase("ChestDropChanceMultiplier", -1f)] [TestCase("ChestDropChanceMultiplier", float.PositiveInfinity)]
        public void EliteInspectorRejectsInvalidInputs(string field, float value)
        {
            var go = new GameObject("Elite settings validation"); go.SetActive(false);
            try
            {
                var settings = go.AddComponent<GamePresentationBootstrap>();
                var serialized = new UnityEditor.SerializedObject(settings);
                serialized.FindProperty("m_Elite" + field).floatValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(settings.TryValidateConfiguration(out string error), Is.False);
                Assert.That(error, Does.StartWith("Elite "));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}

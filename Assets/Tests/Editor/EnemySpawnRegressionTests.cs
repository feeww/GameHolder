using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameHolder.PureDots.Tests
{
    public partial class SimulationRegressionTests
    {
        [TestCase(0, 4, 1f, 1f, 1f)]
        [TestCase(5, 7, 1.403f, 1.051f, 1.188f)]
        [TestCase(10, 10, 1.967f, 1.105f, 1.411f)]
        [TestCase(20, 23, 3.870f, 1.220f, 1.990f)]
        [TestCase(30, 54, 7.612f, 1.348f, 2.807f)]
        public void BalancedRunIncreasesDensityWithoutOutrunningThePlayer(int minute, int count, float health, float speed, float damage)
        {
            var wave = new EnemySpawnSettings().ToConfig();
            wave.ElapsedSeconds = minute * 60;
            m_Em.SetComponentData(m_Wave, wave);
            Tick(1);
            Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(count));
            var pool = m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>();
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            for (int i = 0; i < count; i++)
            {
                var type = m_Em.GetComponentData<TypeId>(pool.AllEnemies[i]);
                Assert.That(type.StatMultipliers.x, Is.EqualTo(health).Within(.001f));
                Assert.That(type.StatMultipliers.y, Is.EqualTo(speed).Within(.001f));
                Assert.That(type.StatMultipliers.z, Is.EqualTo(damage).Within(.001f));
                Assert.That(catalog.Value.GetConfig(type).MoveSpeed, Is.LessThan(DefaultPlayer.Stats.MoveSpeed));
            }
        }

        [Test]
        public void SpawnScalingCompoundsAtIntervalsAndPreservesEarlierEnemyStats()
        {
            var wave = new EnemySpawnSettings
            {
                SpawnInterval = 1, BatchSize = 2,
                SpawnRateScalingInterval = 2, SpawnRateMultiplier = 2,
                StatScalingInterval = 2, HealthMultiplier = 2, SpeedMultiplier = 3, DamageMultiplier = 4
            }.ToConfig();
            m_Em.SetComponentData(m_Wave, wave);
            Command(SimulationCommandKind.GodMode, 1);
            Tick(1); Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(2));
            Tick(1); Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(6));
            Tick(2); Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(22));
            var pool = m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>();
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            for (int i = 0; i < 22; i++)
            {
                var type = m_Em.GetComponentData<TypeId>(pool.AllEnemies[i]);
                float3 scale = i < 2 ? new float3(1) : i < 6 ? new float3(2, 3, 4) : new float3(4, 9, 16);
                Assert.That(type.StatMultipliers, Is.EqualTo(scale));
                var basis = catalog.Value.Configs[(int)type.Value];
                var config = catalog.Value.GetConfig(type);
                Assert.That(m_Em.GetComponentData<CurrentHealth>(pool.AllEnemies[i]).Value, Is.EqualTo(basis.MaxHealth * scale.x));
                Assert.That(config.MoveSpeed, Is.EqualTo(basis.MoveSpeed * scale.y));
                Assert.That(config.BaseDamage, Is.EqualTo(basis.BaseDamage * scale.z));
                Assert.That(config.Weapon.Damage, Is.EqualTo(basis.Weapon.Damage * scale.z));
                Assert.That(config.ExperienceValue, Is.EqualTo(basis.ExperienceValue));
            }
            Assert.That(m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave).BatchSize, Is.EqualTo(2));
        }

        [Test]
        public void DelayedEnemyTypesRespectAppearanceTimesAndEligibleWeights()
        {
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            var previous = new EnemyConfigData[catalog.Value.Configs.Length];
            for (int i = 0; i < previous.Length; i++) previous[i] = catalog.Value.Configs[i];
            var pool = m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>();
            try
            {
                for (int i = 0; i < previous.Length; i++) catalog.Value.Configs[i].AvailableAfterSeconds = 100;
                var wave = m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave); wave.ElapsedSeconds = 0;
                m_Em.SetComponentData(m_Wave, wave);
                Command(SimulationCommandKind.SpawnExtra, 1000); Tick(0);
                Assert.That(Snapshot.ActiveEnemies, Is.Zero);
                Assert.That(pool.InactiveEnemies.Length, Is.EqualTo(SimulationConstants.DefaultMaxEnemies));
                Assert.That(m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave).RandomSeed, Is.EqualTo(wave.RandomSeed));
                catalog.Value.Configs[1].AvailableAfterSeconds = 0;
                catalog.Value.Configs[2].AvailableAfterSeconds = 2;
                catalog.Value.Configs[0].SpawnThreshold = 1;
                catalog.Value.Configs[1].SpawnThreshold = 3;
                catalog.Value.Configs[2].SpawnThreshold = 9;
                catalog.Value.Configs[3].SpawnThreshold = 10;
                Command(SimulationCommandKind.SpawnExtra, 1000); Tick(0);
                for (int i = 0; i < 1000; i++)
                    Assert.That(m_Em.GetComponentData<TypeId>(pool.AllEnemies[i]).Value, Is.EqualTo(1));
                wave = m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave); wave.ElapsedSeconds = 2;
                m_Em.SetComponentData(m_Wave, wave);
                Command(SimulationCommandKind.SpawnExtra, 2000); Tick(0);
                int runners = 0;
                for (int i = 1000; i < 3000; i++)
                {
                    uint type = m_Em.GetComponentData<TypeId>(pool.AllEnemies[i]).Value;
                    Assert.That(type, Is.EqualTo(1u).Or.EqualTo(2u));
                    if (type == 1) runners++;
                }
                Assert.That(runners, Is.InRange(440, 560), "Eligible weights 2:6 must be renormalized after unlocking.");
            }
            finally { for (int i = 0; i < previous.Length; i++) catalog.Value.Configs[i] = previous[i]; }
        }

        [Test]
        public void LargeSpawnsAreAdditionalPeriodicOptionalAndPoolLimited()
        {
            var settings = new EnemySpawnSettings { SpawnInterval = 1, BatchSize = 2, EnableLargeSpawns = true, LargeSpawnInterval = 2, LargeSpawnCount = 5 };
            m_Em.SetComponentData(m_Wave, settings.ToConfig());
            Command(SimulationCommandKind.GodMode, 1);
            Tick(1); Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(2));
            Tick(1); Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(9));
            Tick(0); Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(9));
            Tick(4); Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(27));
            var wave = m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave);
            Assert.That(wave.Timer, Is.Zero); Assert.That(wave.LargeSpawnTimer, Is.Zero);
            wave.SpawnRateScalingInterval = 1; wave.SpawnRateMultiplier = float.MaxValue;
            m_Em.SetComponentData(m_Wave, wave);
            Tick(1); Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(SimulationConstants.DefaultMaxEnemies));
            Command(SimulationCommandKind.Restart); Tick(0);
            settings.SpawnInterval = 0; settings.EnableLargeSpawns = false;
            m_Em.SetComponentData(m_Wave, settings.ToConfig());
            Tick(100); Assert.That(Snapshot.ActiveEnemies, Is.Zero);
        }

        [Test]
        public void SpawningTimersFreezeDuringPauseAndDeathAndResetOnRestart()
        {
            m_Em.SetComponentData(m_Wave, new EnemySpawnSettings
            { SpawnInterval = 0, EnableLargeSpawns = true, LargeSpawnInterval = 10, StatScalingInterval = 5, HealthMultiplier = 2 }.ToConfig());
            Tick(1);
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run); run.InventoryOpen = 1;
            m_Em.SetComponentData(m_Run, run); Tick(100);
            var wave = m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave);
            Assert.That(wave.ElapsedSeconds, Is.EqualTo(1)); Assert.That(wave.LargeSpawnTimer, Is.EqualTo(1));
            run = m_Em.GetComponentData<SimulationRunState>(m_Run); run.InventoryOpen = 0;
            m_Em.SetComponentData(m_Run, run);
            var stats = m_Em.GetComponentData<PlayerStats>(m_Player); stats.IsDead = 1;
            m_Em.SetComponentData(m_Player, stats); Tick(100);
            wave = m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave);
            Assert.That(wave.ElapsedSeconds, Is.EqualTo(1)); Assert.That(wave.LargeSpawnTimer, Is.EqualTo(1));
            Command(SimulationCommandKind.Restart); Tick(0);
            wave = m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave);
            Assert.That(wave.ElapsedSeconds, Is.Zero); Assert.That(wave.LargeSpawnTimer, Is.Zero);
        }

        [Test]
        public void TimedStatsComposeWithElitesAndStayFiniteWhenScalingOverflows()
        {
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            var previous = catalog.Value.Elites;
            try
            {
                catalog.Value.Elites = TestElites(1);
                var basis = catalog.Value.Configs[2];
                var config = catalog.Value.GetConfig(new TypeId { Value = 2, IsElite = 1, StatMultipliers = new float3(2, 3, 4) });
                Assert.That(config.MaxHealth, Is.EqualTo(basis.MaxHealth * 3 * 2));
                Assert.That(config.MoveSpeed, Is.EqualTo(basis.MoveSpeed * 2 * 3));
                Assert.That(config.BaseDamage, Is.EqualTo(basis.BaseDamage * 3 * 4));
                Assert.That(config.Weapon.Damage, Is.EqualTo(basis.Weapon.Damage * 3 * 4));
                config = catalog.Value.GetConfig(new TypeId { Value = 2, IsElite = 1, StatMultipliers = new float3(float.MaxValue) });
                Assert.That(math.all(math.isfinite(new float4(config.MaxHealth, config.MoveSpeed, config.BaseDamage, config.Weapon.Damage))), Is.True);
            }
            finally { catalog.Value.Elites = previous; }
        }

        [TestCase("SpawnInterval", -1f)] [TestCase("SpawnInterval", float.NaN)]
        [TestCase("MinRadius", 0f)] [TestCase("MaxRadius", float.PositiveInfinity)]
        [TestCase("SpawnRateScalingInterval", -1f)] [TestCase("SpawnRateScalingInterval", float.NaN)]
        [TestCase("SpawnRateMultiplier", .5f)] [TestCase("SpawnRateMultiplier", float.PositiveInfinity)]
        [TestCase("StatScalingInterval", -1f)] [TestCase("StatScalingInterval", float.NaN)]
        [TestCase("HealthMultiplier", 0f)] [TestCase("SpeedMultiplier", float.NaN)]
        [TestCase("DamageMultiplier", float.NegativeInfinity)]
        [TestCase("LargeSpawnInterval", 0f)] [TestCase("LargeSpawnInterval", float.PositiveInfinity)]
        public void EnemySpawnInspectorRejectsInvalidValues(string field, float value)
        {
            var settings = new EnemySpawnSettings { EnableLargeSpawns = true };
            typeof(EnemySpawnSettings).GetField(field).SetValue(settings, value);
            Assert.That(settings.TryValidate(out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void EnemyAppearanceTimeRejectsInvalidValues(float time)
        {
            var go = new GameObject("Enemy appearance validation"); go.SetActive(false);
            var enemy = ScriptableObject.CreateInstance<EnemyDefinition>();
            try
            {
                var settings = go.AddComponent<GamePresentationBootstrap>();
                var serialized = new UnityEditor.SerializedObject(settings);
                serialized.FindProperty("m_StartingCharacter").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/GameData/Characters/DefaultCharacter.asset");
                serialized.FindProperty("m_EnemyTypes").arraySize = 1;
                serialized.FindProperty("m_EnemyTypes").GetArrayElementAtIndex(0).objectReferenceValue = enemy;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                enemy.AvailableAfterSeconds = time;
                Assert.That(settings.TryValidateConfiguration(out string error), Is.False);
                Assert.That(error, Does.Contain("appearance time"));
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(enemy); }
        }
    }
}

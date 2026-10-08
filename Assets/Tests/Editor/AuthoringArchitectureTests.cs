using System;
using System.Linq;
using System.Reflection;
using GameHolder.PureDots.Editor;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameHolder.PureDots.Tests
{
    public class AuthoringArchitectureTests
    {
        [TestCase("m_MaxEnemies", 0)] [TestCase("m_MaxEnemies", int.MaxValue)]
        [TestCase("m_MaxGems", -1)] [TestCase("m_MaxPlayerProjectiles", 0)]
        [TestCase("m_MaxPlayerProjectiles", int.MaxValue)] [TestCase("m_MaxEnemyProjectiles", int.MaxValue)]
        public void InvalidPoolLimitsAreRejectedBeforeAllocation(string field, int value)
        {
            var go = new GameObject("Pool settings validation"); go.SetActive(false);
            try
            {
                var settings = go.AddComponent<GamePresentationBootstrap>();
                var serialized = new SerializedObject(settings);
                serialized.FindProperty(field).intValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(settings.TryValidateConfiguration(out string error), Is.False);
                Assert.That(error, Does.Contain("Pool limits"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void PoolFreeListsShareJobStateAndReuseWithoutNativeAllocations()
        {
            using var helper = new AllocatorHelper<PoolAllocationCounter>(Allocator.Persistent);
            var allocator = helper.Allocator.Handle;
            var enemies = new EnemyPoolSingleton { InactiveEnemies = new NativeRingQueue<Entity>(7, allocator) };
            var player = new PlayerProjectilePoolSingleton { InactiveProjectiles = new NativeRingQueue<Entity>(2, allocator) };
            var enemy = new EnemyProjectilePoolSingleton { InactiveProjectiles = new NativeRingQueue<Entity>(5, allocator) };
            var gems = new GemPoolSingleton { FreeGems = new NativeRingQueue<Entity>(3, allocator),
                FreeChests = new NativeRingQueue<Entity>(GemPoolSingleton.ChestCapacity, allocator) };
            var queues = new[] { enemies.InactiveEnemies, player.InactiveProjectiles, enemy.InactiveProjectiles, gems.FreeGems, gems.FreeChests };
            int allocations = helper.Allocator.Allocations;
            try
            {
                foreach (var queue in queues)
                {
                    new ReusePoolJob { Pool = queue }.Schedule().Complete();
                    Assert.That(queue.Length, Is.EqualTo(queue.Capacity));
                    Assert.That(queue.TryEnqueue(Entity.Null), Is.False);
                    for (int i = 0; i < queue.Capacity; i++) Assert.That(queue.Dequeue().Index, Is.EqualTo(i + 1));
                    Assert.That(queue.TryDequeue(out _), Is.False);
                }
                Assert.That(helper.Allocator.Allocations, Is.EqualTo(allocations));
            }
            finally { foreach (var queue in queues) queue.Dispose(); helper.Allocator.Dispose(); }
        }

        [BurstCompile]
        private struct ReusePoolJob : IJob
        {
            public NativeRingQueue<Entity> Pool;
            public void Execute()
            {
                for (int cycle = 0; cycle < 64; cycle++)
                {
                    while (Pool.TryDequeue(out _)) { }
                    for (int i = 0; i < Pool.Capacity; i++) Pool.Enqueue(new Entity { Index = i + 1, Version = 1 });
                }
            }
        }

        private struct PoolAllocationCounter : AllocatorManager.IAllocator
        {
            public AllocatorManager.AllocatorHandle Handle { get; set; }
            public Allocator ToAllocator => Handle.ToAllocator;
            public bool IsCustomAllocator => Handle.IsCustomAllocator;
            public AllocatorManager.TryFunction Function => Allocate;
            public int Allocations;
            public int Try(ref AllocatorManager.Block block)
            {
                bool allocating = block.Range.Pointer == IntPtr.Zero;
                var original = block.Range.Allocator; block.Range.Allocator = Allocator.Persistent;
                int error = AllocatorManager.Try(ref block); block.Range.Allocator = original;
                if (error == 0 && allocating) Allocations++;
                return error;
            }
            [BurstCompile(CompileSynchronously = true)]
            [AOT.MonoPInvokeCallback(typeof(AllocatorManager.TryFunction))]
            private static unsafe int Allocate(IntPtr state, ref AllocatorManager.Block block)
                => ((PoolAllocationCounter*)state)->Try(ref block);
            public void Dispose() => Handle.Dispose();
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonFiniteAuthoringCannotEnterSimulation(float invalid)
        {
            var weapon = ScriptableObject.CreateInstance<CharacterWeaponDefinition>();
            var character = ScriptableObject.CreateInstance<CharacterDefinition>();
            var enemy = ScriptableObject.CreateInstance<EnemyDefinition>();
            character.Weapon = weapon;
            try
            {
                foreach (var asset in new ScriptableObject[] { character, enemy, weapon })
                {
                    foreach (var field in asset.GetType().GetFields().Where(field => field.FieldType == typeof(float)))
                    {
                        object original = field.GetValue(asset);
                        field.SetValue(asset, invalid);
                        Assert.That(Valid(asset), Is.False, asset.GetType().Name + "." + field.Name);
                        field.SetValue(asset, original);
                    }
                    var tint = asset.GetType().GetField("Tint");
                    tint.SetValue(asset, new Color(invalid, 1, 1, 1));
                    Assert.That(Valid(asset), Is.False, asset.GetType().Name + ".Tint");
                    tint.SetValue(asset, Color.white);
                }
                weapon.Damage = invalid;
                Assert.Throws<InvalidOperationException>(() => weapon.ToConfig());
                Assert.Throws<InvalidOperationException>(() => character.ToConfig());
                enemy.Ranged = true;
                var enemyWeapon = ScriptableObject.CreateInstance<EnemyWeaponDefinition>();
                try
                {
                    enemy.Weapon = enemyWeapon; enemyWeapon.ProjectileSpeed = invalid;
                    Assert.That(enemy.TryValidate(out _), Is.False);
                    Assert.Throws<InvalidOperationException>(() => enemy.ToConfig(0, .4f));
                }
                finally { Object.DestroyImmediate(enemyWeapon); }
            }
            finally { Object.DestroyImmediate(character); Object.DestroyImmediate(enemy); Object.DestroyImmediate(weapon); }
        }

        private static bool Valid(ScriptableObject asset) => asset switch {
            CharacterDefinition character => character.TryValidate(out _),
            EnemyDefinition enemy => enemy.TryValidate(out _),
            WeaponDefinition weapon => weapon.TryValidate(out _), _ => false };

        [Test]
        public void AdditionalRewardWeaponsAndUnsupportedTypesAreValidated()
        {
            var character = AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/GameData/Characters/DefaultCharacter.asset");
            var weapon = ScriptableObject.CreateInstance<CharacterWeaponDefinition>();
            try
            {
                var rewards = new RewardSettings { Weapons = new[] { weapon } };
                weapon.Damage = float.PositiveInfinity;
                Assert.That(rewards.TryValidate(character, character.Weapon, out _), Is.False);
                Assert.That(new TemporaryZoneSettings().TryValidate(rewards, character, weapon, out _), Is.False);
                Assert.Throws<InvalidOperationException>(() => rewards.BuildCatalog(character, character.Weapon));
                weapon.Damage = 1; weapon.Type = (WeaponType)255;
                Assert.That(rewards.TryValidate(character, character.Weapon, out _), Is.False);
                Assert.Throws<InvalidOperationException>(() => weapon.ToConfig());
            }
            finally { Object.DestroyImmediate(weapon); }
        }

        [Test]
        public void AllAuthoredInspectorFieldsHaveDescriptions()
        {
            var types = new[] { typeof(GamePresentationBootstrap), typeof(CharacterDefinition), typeof(WeaponDefinition),
                typeof(CharacterWeaponDefinition), typeof(EnemyDefinition), typeof(EnemySpawnSettings), typeof(RewardSettings),
                typeof(RewardTierSettings), typeof(TemporaryZoneSettings), typeof(ArtifactDefinition), typeof(ArtifactChestSettings),
                typeof(CameraPresentationController), typeof(AudioThrottlingManager), typeof(BatchedParticleManager) };
            foreach (var type in types)
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null) continue;
                    if (field.GetCustomAttribute<HideInInspector>() != null &&
                        !new[] { "Color", "CharacterStats", "WeaponStats", "Rarities" }.Contains(field.Name)) continue;
                    Assert.That(field.GetCustomAttribute<TooltipAttribute>()?.tooltip, Is.Not.Null.And.Not.Empty, type.Name + "." + field.Name);
                }
            var descriptions = (string[])typeof(RaritySettingsWindow).GetField("StatDescriptions", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert.That(descriptions.Length, Is.EqualTo((int)UpgradeStat.Lifetime + 1));
            Assert.That(descriptions.All(description => !string.IsNullOrWhiteSpace(description)), Is.True);
        }

        [Test]
        public void SettingsPagesReachEveryBootstrapFieldExactlyOnce()
        {
            var pages = (string[][])typeof(RaritySettingsWindow).GetField("PageProperties", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var fields = pages.Where(page => page != null).SelectMany(page => page).ToArray();
            Assert.That(fields.Distinct().Count(), Is.EqualTo(fields.Length));
            var authored = typeof(GamePresentationBootstrap).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(field => field.GetCustomAttribute<SerializeField>() != null).Select(field => field.Name);
            Assert.That(fields, Is.EquivalentTo(authored));
            foreach (string method in new[] { "OpenRunSetup", "OpenEnemies", "OpenProgression", "OpenWorldRewards", "OpenPresentation" })
                Assert.That(typeof(RaritySettingsWindow).GetMethod(method).GetCustomAttribute<MenuItem>(), Is.Not.Null);
        }

        [Test]
        public void MultipleSceneBootstrapsRequireAnExplicitEditorContext()
        {
            var context = typeof(RaritySettingsWindow).GetProperty("Context", BindingFlags.Static | BindingFlags.NonPublic);
            var original = context.GetValue(null);
            var first = new GameObject("First editor settings");
            var second = new GameObject("Second editor settings");
            try
            {
                var selected = first.AddComponent<GamePresentationBootstrap>();
                second.AddComponent<GamePresentationBootstrap>();
                context.SetValue(null, null);
                Assert.That(context.GetValue(null), Is.Null);
                context.SetValue(null, selected);
                Assert.That(context.GetValue(null), Is.SameAs(selected));
            }
            finally
            {
                Object.DestroyImmediate(first); Object.DestroyImmediate(second);
                context.SetValue(null, original);
            }
        }

        [Test]
        public void ShaderPropertiesHaveDescriptions()
        {
            foreach (string name in new[] { "PureDots/SpriteDOTS", "PureDots/InfiniteFloor" })
            {
                var shader = Shader.Find(name);
                Assert.That(shader, Is.Not.Null);
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                    Assert.That(PureDotsShaderGUI.Description(shader.GetPropertyName(i)), Is.Not.Empty, shader.GetPropertyName(i));
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void HeadlessBootstrapCreatesNativePrefabsAndRunsWithoutRenderer(int poolCase)
        {
            using var world = new World("Native startup without presentation");
            var bootstrap = world.GetOrCreateSystem<SimulationBootstrapSystem>();
            var em = world.EntityManager;
            var limits = poolCase == 1
                ? new PoolLimits { MaxEnemies = 7, MaxGems = 3, MaxPlayerProjectiles = 2, MaxEnemyProjectiles = 5 }
                : PoolLimits.Defaults;
            if (poolCase == 2) { limits.MaxEnemies += 7; limits.MaxGems += 3; limits.MaxPlayerProjectiles += 2; limits.MaxEnemyProjectiles += 5; }
            if (poolCase != 0) em.AddComponentData(em.CreateEntity(), limits);
            var player = new StartingPlayerConfig { Stats = new PlayerStats { MaxHealth = 100, CurrentHealth = 100,
                MoveSpeed = 5, CollisionRadius = .4f, MagnetRadius = 4, Level = 1 },
                Weapon = new PlayerWeapon { Interval = 1, Damage = 5, Range = 12, Radius = .1f, Speed = 12,
                    Lifetime = 3, Count = 1, Color = new float4(1), TextureScale = new float2(1) } };
            em.AddComponentData(em.CreateEntity(), player);
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var catalog = ref builder.ConstructRoot<EnemyConfigCatalog>();
                builder.Allocate(ref catalog.Configs, 1)[0] = new EnemyConfigData { MaxHealth = 10, MoveSpeed = 1,
                    CollisionRadius = .4f, Mass = 1, SpawnThreshold = 1, ContactAttackInterval = 1 };
                em.AddComponentData(em.CreateEntity(), new EnemyConfigCatalogSingleton
                { Catalog = builder.CreateBlobAssetReference<EnemyConfigCatalog>(Allocator.Persistent) });
            }
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var catalog = ref builder.ConstructRoot<RewardCatalog>();
                catalog.MaxWeapons = 2; catalog.RandomSeed = 123; catalog.ExperiencePerLevel = 100;
                em.AddComponentData(em.CreateEntity(), new RewardCatalogSingleton
                { Catalog = builder.CreateBlobAssetReference<RewardCatalog>(Allocator.Persistent) });
            }
            bootstrap.Update(world.Unmanaged);
            var prefabs = em.CreateEntityQuery(typeof(PureDotsPrefabsSingleton)).GetSingleton<PureDotsPrefabsSingleton>();
            Assert.That(em.HasComponent<Unity.Rendering.MaterialMeshInfo>(prefabs.PlayerPrefab), Is.False);
            var enemies = em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>();
            var gems = em.CreateEntityQuery(typeof(GemPoolSingleton)).GetSingleton<GemPoolSingleton>();
            var playerProjectiles = em.CreateEntityQuery(typeof(PlayerProjectilePoolSingleton)).GetSingleton<PlayerProjectilePoolSingleton>();
            var enemyProjectiles = em.CreateEntityQuery(typeof(EnemyProjectilePoolSingleton)).GetSingleton<EnemyProjectilePoolSingleton>();
            Assert.That(enemies.AllEnemies.Length, Is.EqualTo(limits.MaxEnemies));
            Assert.That(gems.AllGems.Length, Is.EqualTo(limits.MaxGems));
            Assert.That(playerProjectiles.AllProjectiles.Length, Is.EqualTo(limits.MaxPlayerProjectiles));
            Assert.That(enemyProjectiles.AllProjectiles.Length, Is.EqualTo(limits.MaxEnemyProjectiles));
            var input = em.CreateEntityQuery(typeof(SimulationInput)).GetSingletonEntity();
            em.SetComponentData(input, new SimulationInput { Movement = new float2(1, 0) });
            world.SetTime(new TimeData(1.0 / 60, 1f / 60));
            world.GetOrCreateSystem<SimulationPipelineSystem>().Update(world.Unmanaged);
            em.CreateEntityQuery(typeof(SimulationJobFence)).GetSingleton<SimulationJobFence>().Handle.Complete();
            Assert.That(em.CreateEntityQuery(typeof(SimulationSnapshot)).GetSingleton<SimulationSnapshot>().PlayerPosition.x, Is.GreaterThan(0));
            var references = typeof(SimulationBootstrapSystem).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name);
            Assert.That(references, Does.Not.Contain("GameHolder.PureDots.Presentation").And.Not.Contain("Unity.Entities.Graphics"));
            if (poolCase == 0) return;
            var pipeline = world.GetOrCreateSystem<SimulationPipelineSystem>();
            var commands = em.CreateEntityQuery(typeof(SimulationCommandQueue)).GetSingleton<SimulationCommandQueue>().Commands;
            var runEntity = em.CreateEntityQuery(typeof(SimulationRunState)).GetSingletonEntity();
            em.SetComponentData(input, default(SimulationInput));
            world.SetTime(new TimeData(1, 0));
            for (int cycle = 0; cycle < 3; cycle++)
            {
                commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.SpawnExtra, Value = int.MaxValue });
                if (poolCase == 1)
                {
                    var run = em.GetComponentData<SimulationRunState>(runEntity); run.AttackTimer = 1;
                    run.EnemyProjectiles = limits.MaxEnemyProjectiles; em.SetComponentData(runEntity, run);
                    var weapon = player.Weapon; weapon.Count = limits.MaxPlayerProjectiles + 1; weapon.Range = 64;
                    em.SetComponentData(run.Player, weapon);
                    for (int i = 0; i < limits.MaxEnemyProjectiles; i++)
                    {
                        Assert.That(enemyProjectiles.InactiveProjectiles.TryDequeue(out Entity projectile), Is.True);
                        em.SetComponentData(projectile, Unity.Transforms.LocalTransform.FromPosition(-100, -100, 0));
                        em.SetComponentData(projectile, new ProjectileData { RemainingLifetime = 1, Radius = .1f });
                        em.SetComponentEnabled<ProjectileActiveTag>(projectile, true);
                    }
                    var drops = em.CreateEntityQuery(typeof(GemSpawnQueueSingleton)).GetSingleton<GemSpawnQueueSingleton>().SpawnQueue;
                    for (int i = 0; i < limits.MaxGems + 2; i++) drops.Enqueue(new GemSpawnRequest { Position = new float2(40), ExperienceValue = 1 });
                }
                pipeline.Update(world.Unmanaged);
                em.GetComponentData<SimulationJobFence>(runEntity).Handle.Complete();
                Assert.That(em.GetComponentData<SimulationSnapshot>(runEntity).ActiveEnemies, Is.EqualTo(limits.MaxEnemies));
                Assert.That(enemies.InactiveEnemies.Length, Is.Zero);
                if (poolCase == 1)
                {
                    var snapshot = em.GetComponentData<SimulationSnapshot>(runEntity);
                    Assert.That(snapshot.PlayerProjectiles, Is.EqualTo(limits.MaxPlayerProjectiles));
                    Assert.That(snapshot.EnemyProjectiles, Is.EqualTo(limits.MaxEnemyProjectiles));
                    Assert.That(snapshot.ActiveGems, Is.EqualTo(limits.MaxGems));
                    ulong xp = 0; foreach (var gem in gems.AllGems) xp += gem.ExperienceValue;
                    Assert.That(xp, Is.EqualTo(limits.MaxGems + 2));
                    Assert.That(playerProjectiles.InactiveProjectiles.Length, Is.Zero);
                    Assert.That(enemyProjectiles.InactiveProjectiles.Length, Is.Zero);
                    foreach (Entity projectile in playerProjectiles.AllProjectiles) em.SetComponentData(projectile, default(ProjectileData));
                    foreach (Entity projectile in enemyProjectiles.AllProjectiles) em.SetComponentData(projectile, default(ProjectileData));
                    commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.KillAll });
                    pipeline.Update(world.Unmanaged);
                    em.GetComponentData<SimulationJobFence>(runEntity).Handle.Complete();
                    Assert.That(enemies.InactiveEnemies.Length, Is.EqualTo(limits.MaxEnemies));
                    Assert.That(playerProjectiles.InactiveProjectiles.Length, Is.EqualTo(limits.MaxPlayerProjectiles));
                    Assert.That(enemyProjectiles.InactiveProjectiles.Length, Is.EqualTo(limits.MaxEnemyProjectiles));
                }
                commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.Restart });
                pipeline.Update(world.Unmanaged);
                em.GetComponentData<SimulationJobFence>(runEntity).Handle.Complete();
                Assert.That(enemies.InactiveEnemies.Length, Is.EqualTo(limits.MaxEnemies));
                Assert.That(playerProjectiles.InactiveProjectiles.Length, Is.EqualTo(limits.MaxPlayerProjectiles));
                Assert.That(enemyProjectiles.InactiveProjectiles.Length, Is.EqualTo(limits.MaxEnemyProjectiles));
                Assert.That(gems.FreeGems.Length, Is.EqualTo(limits.MaxGems));
                Assert.That(gems.FreeChests.Length, Is.EqualTo(GemPoolSingleton.ChestCapacity));
            }
        }
    }
}

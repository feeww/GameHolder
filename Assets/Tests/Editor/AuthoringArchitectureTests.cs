using System;
using System.Linq;
using System.Reflection;
using GameHolder.PureDots.Editor;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameHolder.PureDots.Tests
{
    public class AuthoringArchitectureTests
    {
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

        [Test]
        public void HeadlessBootstrapCreatesNativePrefabsAndRunsWithoutRenderer()
        {
            using var world = new World("Native startup without presentation");
            var bootstrap = world.GetOrCreateSystem<SimulationBootstrapSystem>();
            var em = world.EntityManager;
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
            Assert.That(em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>().AllEnemies.Length, Is.EqualTo(SimulationConstants.MaxEnemies));
            var input = em.CreateEntityQuery(typeof(SimulationInput)).GetSingletonEntity();
            em.SetComponentData(input, new SimulationInput { Movement = new float2(1, 0) });
            world.SetTime(new TimeData(1.0 / 60, 1f / 60));
            world.GetOrCreateSystem<SimulationPipelineSystem>().Update(world.Unmanaged);
            em.CreateEntityQuery(typeof(SimulationJobFence)).GetSingleton<SimulationJobFence>().Handle.Complete();
            Assert.That(em.CreateEntityQuery(typeof(SimulationSnapshot)).GetSingleton<SimulationSnapshot>().PlayerPosition.x, Is.GreaterThan(0));
            var references = typeof(SimulationBootstrapSystem).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name);
            Assert.That(references, Does.Not.Contain("GameHolder.PureDots.Presentation").And.Not.Contain("Unity.Entities.Graphics"));
        }
    }
}

using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = Unity.Mathematics.Random;

namespace GameHolder.PureDots.Tests
{
    public class TemporaryZoneTests
    {
        private TemporaryZoneConfig m_Config;
        private SimulationInput m_View;
        private TemporaryZoneState m_Zone;

        [SetUp]
        public void Setup()
        {
            m_Config = new TemporaryZoneSettings().ToConfig();
            m_View = new SimulationInput { ViewValid = 1, ViewCenterWorld = new double2(20, -30), ViewHalfSize = new float2(16, 9) };
            m_Zone = new TemporaryZoneState { RandomState = 12345 };
        }

        private bool Tick(float dt, float2 player = default, float2 previous = default, bool paused = false, bool dead = false)
            => TemporaryZone.Update(ref m_Zone, m_Config, m_View, default, previous, player, paused, dead, dt);

        [Test]
        public void SpawnsOnIntervalOutsideViewAndHonorsConfigurableCap()
        {
            m_Config.SpawnChance = 1;
            Tick(9); Assert.That(m_Zone.Active.Length, Is.Zero);
            Tick(1); Assert.That(m_Zone.Active.Length, Is.EqualTo(1));
            var first = m_Zone.Active[0].Position;
            var clearance = math.abs(first - (float2)m_View.ViewCenterWorld) - m_View.ViewHalfSize;
            Assert.That(math.any(clearance > math.max(new float2(m_Config.Radius), m_Config.StructureSize) + m_Config.ViewPadding), Is.True);
            Tick(10); Assert.That(m_Zone.Active.Length, Is.EqualTo(1));
            Assert.That(m_Zone.Active[0].Position, Is.EqualTo(first));
            m_Config.MaxActiveZones = 2;
            Tick(10); Assert.That(m_Zone.Active.Length, Is.EqualTo(2));
            Tick(10); Assert.That(m_Zone.Active.Length, Is.EqualTo(2));
            m_Config.SpawnChance = 0; m_Zone.Active.Clear();
            Tick(10); Assert.That(m_Zone.Active.Length, Is.Zero);
            m_Config.SpawnChance = 1; m_View.ViewValid = 0;
            Tick(10); Assert.That(m_Zone.Active.Length, Is.Zero);
        }

        [Test]
        public void TenPercentTrialsAreDeterministicAndSeparateFromOtherRewards()
        {
            int spawned = 0;
            var replay = m_Zone;
            for (int i = 0; i < 10000; i++)
            {
                m_Zone.Active.Clear(); replay.Active.Clear();
                Tick(10);
                TemporaryZone.Update(ref replay, m_Config, m_View, default, default, default, false, false, 10);
                Assert.That(replay.Active.Length, Is.EqualTo(m_Zone.Active.Length));
                Assert.That(replay.RandomState, Is.EqualTo(m_Zone.RandomState));
                spawned += m_Zone.Active.Length;
            }
            Assert.That(spawned, Is.InRange(850, 1150));
            m_Config.SpawnChance = 1; m_Config.MaxActiveZones = 8;
            m_Zone.Active.Clear(); Tick(1000);
            Assert.That(m_Zone.Active.Length, Is.EqualTo(1), "Missed ticks must not cause an unbounded spawn burst.");
        }

        [Test]
        public void ContinuousHoldResetsOnExitPausesAndExpiresWithoutLateRewards()
        {
            m_Config.SpawnChance = 0;
            m_Zone.Active.Add(new TemporaryZoneSlot { Remaining = 60 });
            Assert.That(Tick(4), Is.False);
            Assert.That(m_Zone.Active[0].HoldTime, Is.EqualTo(4));
            Tick(20, paused: true);
            Assert.That(m_Zone.Active[0].Remaining, Is.EqualTo(56));
            Tick(1, player: new float2(4, 0));
            Assert.That(m_Zone.Active[0].HoldTime, Is.Zero);
            Tick(5, previous: new float2(4, 0));
            Assert.That(m_Zone.Active[0].HoldTime, Is.Zero, "Entering the circle must not count the preceding outside interval.");
            Assert.That(Tick(5), Is.True);
            Assert.That(m_Zone.Active.Length, Is.Zero);
            Assert.That(Tick(1), Is.False);
            m_Zone.Active.Add(new TemporaryZoneSlot { Remaining = .25f, HoldTime = 4.5f });
            Assert.That(Tick(1), Is.False); Assert.That(m_Zone.Active.Length, Is.Zero);
            m_Zone.Active.Add(new TemporaryZoneSlot { Remaining = 60 });
            Tick(60, player: new float2(4, 0)); Assert.That(m_Zone.Active.Length, Is.Zero);
            m_Zone.Active.Add(new TemporaryZoneSlot { Remaining = 60 });
            Tick(1, dead: true); Assert.That(m_Zone.Active.Length, Is.Zero);
        }

        [Test]
        public void RandomStatUsesSharedRarityAppliesOnceAndAcknowledgementCannotReroll()
        {
            var character = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/GameData/Characters/DefaultCharacter.asset");
            var settings = new RewardSettings { UseFixedSeed = true, NewWeaponChance = 1 };
            using var catalog = settings.BuildCatalog(character, character.Weapon);
            catalog.Value.Rarities[0].Weight = 0;
            catalog.Value.RarityWeightTotal -= settings.Rarities[0].Weight;
            var run = new SimulationRunState { Generation = 3, Loadout = RewardRoll.StartingLoadout(),
                Rewards = new RewardSelection { RandomState = 777, Pending = 2 } };
            var baseline = character.ToConfig(); var stats = baseline.Stats; var weapon = baseline.Weapon;
            var random = new Random(12345); var previous = default(FixedList4096Bytes<RewardChoice>);
            var choice = RewardRoll.StatChoice(ref random, run.Loadout, ref catalog.Value, ref previous,
                CharacterUpgradeStats.MaxHealth, WeaponUpgradeStats.None);
            Assert.That(choice.Kind, Is.EqualTo(RewardKind.StatUpgrade));
            Assert.That(choice.Stat, Is.EqualTo(UpgradeStat.MaxHealth)); Assert.That(choice.Rarity, Is.Not.Zero);
            Assert.That(RewardRoll.ApplyStat(ref run, ref stats, ref weapon, baseline, choice), Is.True);
            Assert.That(stats.MaxHealth, Is.EqualTo(baseline.Stats.MaxHealth * (1 + choice.Bonus)).Within(.0001f));
            Assert.That(run.Rewards.Pending, Is.EqualTo(2)); Assert.That(run.Rewards.RandomState, Is.EqualTo(777));
            run.Zone.Reward = choice; run.Zone.ReceiptActive = 1; run.Zone.ReceiptId = 9;
            Assert.That(run.Paused, Is.True);
            Assert.That(RewardRoll.Reroll(ref run, stats, ref catalog.Value,
                new SimulationCommand { Kind = SimulationCommandKind.RerollUpgrades, Generation = 3, PromptId = 9 }), Is.False);
            var ack = new SimulationCommand { Kind = SimulationCommandKind.AcknowledgeZoneReward, Generation = 2, PromptId = 9 };
            Assert.That(TemporaryZone.Acknowledge(ref run, ack), Is.False);
            ack.Generation = 3; ack.PromptId = 8; Assert.That(TemporaryZone.Acknowledge(ref run, ack), Is.False);
            ack.PromptId = 9; Assert.That(TemporaryZone.Acknowledge(ref run, ack), Is.True);
            Assert.That(TemporaryZone.Acknowledge(ref run, ack), Is.False);
            Assert.That(run.Paused, Is.False);
            Assert.That(stats.MaxHealth, Is.EqualTo(baseline.Stats.MaxHealth * (1 + choice.Bonus)).Within(.0001f));
        }

        [Test]
        public void ConfirmationHasOnlyOKAndQueuesOneAcknowledgement()
        {
            var go = new GameObject("Zone receipt test"); var commands = new UnsafeQueue<SimulationCommand>(Allocator.Temp);
            try
            {
                var hud = go.AddComponent<PureDotsHUD>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                if (PureDotsHUD.Instance != hud) typeof(PureDotsHUD).GetMethod("Awake", flags).Invoke(hud, null);
                Assert.That(go.transform.Find("HUD canvas/Zone indicator 1"), Is.Null);
                var snapshot = new SimulationSnapshot { Generation = 3, Zone = new TemporaryZoneState {
                    ReceiptActive = 1, ReceiptId = 8, Reward = new RewardChoice { Kind = RewardKind.StatUpgrade,
                        Stat = UpgradeStat.MaxHealth, Bonus = .1f, Name = new FixedString64Bytes("Rare"), Color = new float4(1) } } };
                hud.ApplySnapshot(snapshot, commands);
                var overlay = go.transform.Find("HUD canvas/Zone reward overlay");
                Assert.That(overlay.gameObject.activeSelf, Is.True);
                var buttons = overlay.GetComponentsInChildren<Button>();
                Assert.That(buttons.Length, Is.EqualTo(1)); Assert.That(buttons[0].name, Is.EqualTo("OK"));
                Assert.That(go.transform.Find("HUD canvas/Reward panel").gameObject.activeSelf, Is.False);
                buttons[0].onClick.Invoke(); buttons[0].onClick.Invoke(); hud.ApplySnapshot(snapshot, commands);
                Assert.That(commands.Count, Is.EqualTo(1)); Assert.That(commands.TryDequeue(out var command), Is.True);
                Assert.That(command.Kind, Is.EqualTo(SimulationCommandKind.AcknowledgeZoneReward));
                Assert.That(command.Generation, Is.EqualTo(3)); Assert.That(command.PromptId, Is.EqualTo(8));
                snapshot.Zone.ReceiptActive = 0; hud.ApplySnapshot(snapshot, commands);
                Assert.That(overlay.gameObject.activeSelf, Is.False);
            }
            finally { Object.DestroyImmediate(go); commands.Dispose(); }
        }

        [Test]
        public void TexturedArrowAndTimerUseWorldRenderersAndTrackZoneVisibility()
        {
            var go = new GameObject("Zone sprites test"); var cameraObject = new GameObject("Camera");
            var texture = new Texture2D(20, 10);
            try
            {
                var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true;
                camera.transform.position = new Vector3(0, 0, -10); camera.pixelRect = new Rect(0, 0, 800, 600);
                var settings = new TemporaryZoneSettings { ArrowTexture = texture };
                var presentation = go.AddComponent<TemporaryZonePresentation>(); presentation.Initialize(settings, camera);
                var snapshot = new SimulationSnapshot();
                snapshot.Zone.Active.Add(new TemporaryZoneSlot { Position = new float2(100, 0), Remaining = 59.1f });
                presentation.ApplySnapshot(snapshot);
                var arrow = go.transform.Find("Zone indicator 1/Direction arrow");
                var timer = go.transform.Find("Zone indicator 1/Zone timer").GetComponent<TextMesh>();
                Assert.That(arrow.gameObject.activeInHierarchy, Is.True);
                Assert.That(arrow.GetComponent<MeshRenderer>().sharedMaterial.mainTexture, Is.SameAs(texture));
                Assert.That(arrow.localScale.y / arrow.localScale.x, Is.EqualTo(.5f).Within(.001f));
                Assert.That(timer.text, Is.EqualTo("60s"));
                Assert.That(go.GetComponentsInChildren<Canvas>(true), Is.Empty);
                Assert.That(go.GetComponentsInChildren<CanvasRenderer>(true), Is.Empty);
                var screen = camera.WorldToScreenPoint(arrow.position);
                Assert.That(camera.pixelRect.Contains(screen), Is.True);
                snapshot.Zone.Active[0] = new TemporaryZoneSlot { Remaining = 20, HoldTime = 2.5f };
                presentation.ApplySnapshot(snapshot);
                Assert.That(arrow.gameObject.activeSelf, Is.False);
                Assert.That(timer.text, Does.Contain("20s | Hold 2.5 / 5.0s"));
                snapshot.Zone.Active.Clear(); presentation.ApplySnapshot(snapshot);
                Assert.That(timer.gameObject.activeInHierarchy, Is.False);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(texture); }
        }

        [Test]
        public void IndicatorStaysWithinViewportAtEveryAspectAndDirection()
        {
            foreach (var size in new[] { new Vector2(1920, 1080), new Vector2(480, 900), new Vector2(160, 100) })
            {
                var viewport = new Rect(new Vector2(40, 20), size);
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8;
                    var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    var point = TemporaryZonePresentation.IndicatorPosition(viewport.center + direction * 10000, viewport, 90);
                    Assert.That(viewport.Contains(point), Is.True);
                    Assert.That(Mathf.Abs(Vector2.SignedAngle(direction, point - viewport.center)), Is.LessThan(.01f));
                }
            }
        }

        [Test]
        public void AuthoringRejectsImpossibleTimersNonfiniteValuesAndUnusableStats()
        {
            var character = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/GameData/Characters/DefaultCharacter.asset");
            var rewards = new RewardSettings(); var settings = new TemporaryZoneSettings();
            Assert.That(settings.TryValidate(rewards, character, character.Weapon, out _), Is.True);
            settings.HoldDuration = 61; Assert.That(settings.TryValidate(rewards, character, character.Weapon, out _), Is.False);
            settings.HoldDuration = 5; settings.SpawnInterval = float.NaN;
            Assert.That(settings.TryValidate(rewards, character, character.Weapon, out _), Is.False);
            settings.SpawnInterval = 10; settings.CharacterStats = CharacterUpgradeStats.None; settings.WeaponStats = WeaponUpgradeStats.None;
            Assert.That(settings.TryValidate(rewards, character, character.Weapon, out _), Is.False);
            settings.Enabled = false; Assert.That(settings.TryValidate(rewards, character, character.Weapon, out _), Is.True);
        }
    }

    public partial class SimulationRegressionTests
    {
        [Test]
        public void ZoneJobAppliesRewardBeforeConfirmationPublishesItAndRestartClearsIt()
        {
            var config = new TemporaryZoneSettings { CharacterStats = CharacterUpgradeStats.MaxHealth,
                WeaponStats = WeaponUpgradeStats.None }.ToConfig();
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
            run.Zone.Active.Add(new TemporaryZoneSlot { Position = run.PlayerPosition, Remaining = 60 });
            uint pending = run.Rewards.Pending, rewardSeed = run.Rewards.RandomState;
            float health = Snapshot.Player.MaxHealth;
            m_Em.SetComponentData(m_Run, run);
            ref var system = ref m_World.Unmanaged.ResolveSystemStateRef(m_Pipeline);
            var access = new SimulationAccess { State = m_Run, InputEntity = m_Input, Zones = config, StartingPlayer = DefaultPlayer,
                Rewards = m_Em.CreateEntityQuery(typeof(RewardCatalogSingleton)).GetSingleton<RewardCatalogSingleton>().Catalog };
            access.Initialize(ref system);
            var handle = new TemporaryZoneJob { A = access, Dt = 5 }.Schedule();
            new PublishSimulationSnapshotJob { A = access }.Schedule(handle).Complete();
            Assert.That(Snapshot.Zone.ReceiptActive, Is.EqualTo(1));
            Assert.That(Snapshot.Zone.Active.Length, Is.Zero);
            Assert.That(Snapshot.Zone.Reward.Stat, Is.EqualTo(UpgradeStat.MaxHealth));
            Assert.That(Snapshot.Player.MaxHealth, Is.GreaterThan(health));
            Assert.That(Snapshot.Rewards.Pending, Is.EqualTo(pending));
            Assert.That(Snapshot.Rewards.RandomState, Is.EqualTo(rewardSeed));
            run = m_Em.GetComponentData<SimulationRunState>(m_Run);
            Assert.That(run.Paused, Is.True);
            Assert.That(TemporaryZone.Acknowledge(ref run, new SimulationCommand { Kind = SimulationCommandKind.AcknowledgeZoneReward,
                Generation = run.Generation, PromptId = run.Zone.ReceiptId }), Is.True);
            m_Em.SetComponentData(m_Run, run);
            Command(SimulationCommandKind.Restart); Tick(0);
            Assert.That(Snapshot.Zone.Active.Length, Is.Zero); Assert.That(Snapshot.Zone.ReceiptActive, Is.Zero);
            Assert.That(Snapshot.Player.MaxHealth, Is.EqualTo(health));
        }
    }
}

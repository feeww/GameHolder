using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameHolder.PureDots.Tests
{
    public class ParticlePoolTests
    {
        [TestCase("m_DeathParticleSystem")]
        [TestCase("m_HitParticleSystem")]
        [TestCase("m_GemCollectParticleSystem")]
        public void RebasePreservesEveryParticleInCustomSystems(string field)
        {
            var managerObject = new GameObject("Particle pool regression"); managerObject.SetActive(false);
            var particleObject = new GameObject("Custom particle system");
            try
            {
                var system = particleObject.AddComponent<ParticleSystem>();
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                const int count = PresentationConstants.ParticleCapacity + 3;
                var main = system.main;
                main.maxParticles = count; main.simulationSpace = ParticleSystemSimulationSpace.World;
                system.Simulate(0, true, true, false);
                system.Emit(new ParticleSystem.EmitParams { position = new Vector3(10, 20, 0),
                    startLifetime = 60, startSize = 1, velocity = Vector3.zero }, count);
                system.Simulate(.01f, true, false, false);
                Assert.That(system.particleCount, Is.EqualTo(count));
                var manager = managerObject.AddComponent<BatchedParticleManager>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(BatchedParticleManager).GetField(field, flags).SetValue(manager, system);
                typeof(BatchedParticleManager).GetMethod("Awake", flags).Invoke(manager, null);
                var buffer = (ParticleSystem.Particle[])typeof(BatchedParticleManager).GetField("m_ParticlesBuffer", flags).GetValue(manager);
                manager.ShiftAllParticles(new Vector2(1, 2));
                manager.ShiftAllParticles(new Vector2(1, 2));
                Assert.That(system.particleCount, Is.EqualTo(count));
                Assert.That(system.GetParticles(buffer), Is.EqualTo(count));
                foreach (var particle in buffer) Assert.That(particle.position, Is.EqualTo(new Vector3(8, 16, 0)));
                Assert.That(typeof(BatchedParticleManager).GetField("m_ParticlesBuffer", flags).GetValue(manager), Is.SameAs(buffer));
            }
            finally { Object.DestroyImmediate(managerObject); Object.DestroyImmediate(particleObject); }
        }
    }
}

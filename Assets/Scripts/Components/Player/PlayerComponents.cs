using Unity.Entities;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public struct PlayerTag : IComponentData
    {
    }

    public struct PlayerInputData : IComponentData
    {
        public float2 MoveInput;
    }

    public struct PlayerInvulnerability : IComponentData
    {
        public float Timer;
        public float InvulnerabilityDuration;
    }

    public struct PlayerStats : IComponentData
    {
        public float MoveSpeed;
        public float MagnetRadius;
        public float CurrentHealth;
        public float MaxHealth;
        public uint Experience;
        public uint Level;
        public byte IsDead;
    }
}

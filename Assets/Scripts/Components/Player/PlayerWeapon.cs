using Unity.Entities;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public enum WeaponType : byte { Standard, Explosive, Laser }

    public struct PlayerWeapon : IComponentData
    {
        public WeaponType Type;
        public float Interval, Damage, Radius, Speed, Lifetime, Range, BlastRadius;
        public int Count;
        public float SpreadAngle;
        public float4 Color;
    }

    public struct StartingPlayerConfig : IComponentData
    {
        public PlayerStats Stats;
        public PlayerWeapon Weapon;
        public float InvulnerabilityDuration, RespawnGracePeriod;
    }

}

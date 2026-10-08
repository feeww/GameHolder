namespace GameHolder.PureDots
{
    // Shared combat rules and event identifiers; balance lives in Unity assets.
    public static class CombatConstants
    {
        public const float MeleeContactReach = 0.08f;
        public const uint PlayerTypeId = uint.MaxValue;
        public const float AutoAimAngleStep = .37f;
        public const float BlastVisualLifetime = .12f;
        public const float InitialCooldownMinScale = .1f;
        public const float CooldownMinScale = .9f;
        public const float CooldownMaxScale = 1.1f;
        public const float EnemyLaserMinimumRange = 3;
        public const float EnemyLaserMaximumRange = 7;
        public const float EnemyLaserChargeDuration = 1;
        public const float EnemyLaserMeleeSpeedMultiplier = 1.15f;
    }
}

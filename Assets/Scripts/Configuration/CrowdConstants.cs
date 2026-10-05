namespace GameHolder.PureDots
{
    public static class CrowdConstants
    {
        public const float SpatialCellSize = 1.25f;
        public const float SpatialInvCellSize = 1.0f / SpatialCellSize;
        public const uint HashPrimeX = 73856093u;
        public const uint HashPrimeY = 19349663u;
        public const float Tier1Radius = 22.0f;
        public const float Tier1RadiusSq = Tier1Radius * Tier1Radius;
        public const float Tier2MaxRadius = 45.0f;
        public const float Tier2MaxRadiusSq = Tier2MaxRadius * Tier2MaxRadius;
        public const float MaxCatchUpMultiplier = 3.0f;
        public const int MaxCrowdEntries = 32;
        public const float CrowdPushSpeed = 2.5f;
        public const float CrowdSteeringMargin = 0.25f;
        public const float CrowdBodyRadiusScale = 0.85f;
        public const float CrowdTargetDensity = 1.0f;
        public const float CrowdContactResponse = 8.0f;
        public const float CrowdPackingRange = 3.0f;
        public const float CrowdContactDeadZone = 0.01f;
        public const float PlayerCrowdResistance = 0.2f;
        public const float PlayerCrowdMinimumSpeed = 0.08f;
        public const float PlayerCrowdPushSpeed = 2.0f;
        public const float PlayerContactSkin = 0.01f;

        public const float MinimumPushPriority = .01f;
        public const float OverlapDirectionDistance = 1e-5f;
        public const float FlatGradientLengthSq = 1e-10f;
        public const float SideSelectionThreshold = .5f;
        public const float LateralSteeringScale = .65f;
        public const int NeighborsForFullSteering = 4;
        public const uint SteeringUpdateInterval = 4;
        public const float ContactCorrectionSpeedScale = .5f;
    }
}

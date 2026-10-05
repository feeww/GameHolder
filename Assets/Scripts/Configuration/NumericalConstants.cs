namespace GameHolder.PureDots
{
    // Guard denominators and degenerate geometry; these are not balance settings.
    public static class NumericalConstants
    {
        public const float MinimumDivisor = 1e-6f;
        public const float MinimumSweepLengthSq = 1e-12f;
    }
}

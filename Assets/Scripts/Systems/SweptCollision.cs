using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public static class SweptCollision
    {
        // Relative motion reduces two moving circles to a segment against a circle at the origin.
        public static bool TryHit(float2 start, float2 end, float radius, out float time)
        {
            time = 0;
            float c = math.lengthsq(start) - radius * radius;
            if (c <= 0) return true;
            float2 delta = end - start;
            float a = math.lengthsq(delta);
            if (a < 1e-12f) return false;
            float b = math.dot(start, delta);
            float discriminant = b * b - a * c;
            if (b >= 0 || discriminant < 0) return false;
            time = (-b - math.sqrt(discriminant)) / a;
            return time >= 0 && time <= 1;
        }
    }
}

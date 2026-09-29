using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public struct GridEntry
    {
        public Entity Entity;    // 8 bytes
        public float2 Position;  // 8 bytes
        public int2 CellCoord;   // 8 bytes
        public float2 Padding;   // 8 bytes (Total: exactly 32 bytes)
    }

    public static class SpatialHashUtils
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 QuantizeToCell(float2 position, float invCellSize)
        {
            return (int2)math.floor(position * invCellSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ComputeHash(int2 cell)
        {
            return unchecked(((uint)cell.x * 73856093u) ^ ((uint)cell.y * 19349663u));
        }
    }

    public struct EnemySpatialGridSingleton : IComponentData
    {
        public UnsafeParallelMultiHashMap<uint, GridEntry> Grid;
    }

    public struct EnemyProjectileGridSingleton : IComponentData
    {
        public UnsafeParallelMultiHashMap<uint, GridEntry> Grid;
    }
}

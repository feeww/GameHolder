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
        public float2 PreviousPosition;
        public float Radius;
        public float PushPriority;
        public int PoolIndex;
    }

    public struct CrowdCell
    {
        public int Count;
        public float Density;
        public float PrioritySum;
    }

    public static class SpatialHashUtils
    {
        public const float CellSize = CrowdConstants.SpatialCellSize;
        public const float InvCellSize = CrowdConstants.SpatialInvCellSize;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 QuantizeToCell(float2 position)
        {
            return (int2)math.floor(position * InvCellSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 QuantizeToCell(float2 position, float invCellSize)
        {
            return (int2)math.floor(position * invCellSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ComputeHash(int2 cell)
        {
            return unchecked(((uint)cell.x * CrowdConstants.HashPrimeX) ^ ((uint)cell.y * CrowdConstants.HashPrimeY));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ComputeHash(int cx, int cy)
        {
            return unchecked(((uint)cx * CrowdConstants.HashPrimeX) ^ ((uint)cy * CrowdConstants.HashPrimeY));
        }
    }

    public struct EnemySpatialGridSingleton : IComponentData
    {
        public UnsafeParallelMultiHashMap<uint, GridEntry> Grid;
        public UnsafeParallelHashMap<int2, CrowdCell> CrowdCells;
    }

    public struct EnemyProjectileGridSingleton : IComponentData
    {
        public UnsafeParallelMultiHashMap<uint, GridEntry> Grid;
    }
}

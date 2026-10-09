using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace GameHolder.PureDots
{
    [BurstCompile]
    public struct EventQueueAllocator : AllocatorManager.IAllocator
    {
        private RewindableAllocator m_Blocks;
        private IntPtr m_Header;
        private bool m_HasHeader;
        public AllocatorManager.AllocatorHandle Handle { get; set; }
        public Allocator ToAllocator => Handle.ToAllocator;
        public bool IsCustomAllocator => Handle.IsCustomAllocator;
        public AllocatorManager.TryFunction Function => Allocate;
        public int BlocksAllocated => m_Blocks.BlocksAllocated;

        public static UnsafeQueue<T> CreateQueue<T>(int capacity, bool parallel,
            out AllocatorHelper<EventQueueAllocator> owner) where T : unmanaged
        {
            owner = new AllocatorHelper<EventQueueAllocator>(Allocator.Persistent);
            // Collections 6.6 uses 16 KiB queue blocks with a 16-byte header; writers may each leave a partial block.
            const int blockSize = 16 * 1024;
            int itemsPerBlock = (blockSize - 16) / UnsafeUtility.SizeOf<T>();
            long blocks = (capacity + (long)itemsPerBlock - 1) / itemsPerBlock + (parallel ? JobsUtility.ThreadIndexCount : 1);
            int bytes = (int)Math.Min(int.MaxValue, blocks * blockSize);
            owner.Allocator.m_Blocks.Handle = owner.Allocator.Handle;
            owner.Allocator.m_Blocks.Initialize(bytes, true);
            return new UnsafeQueue<T>(owner.Allocator.Handle);
        }

        public int Try(ref AllocatorManager.Block block)
        {
            // A queue allocates its header first. Keeping it outside the arena lets fully drained payload blocks rewind.
            if (!m_HasHeader || block.Range.Pointer == m_Header)
            {
                bool allocating = block.Range.Pointer == IntPtr.Zero;
                var original = block.Range.Allocator;
                block.Range.Allocator = Allocator.Persistent;
                int error = AllocatorManager.Try(ref block);
                block.Range.Allocator = original;
                if (error == 0 && allocating) { m_Header = block.Range.Pointer; m_HasHeader = true; }
                return error;
            }
            return m_Blocks.Try(ref block);
        }

        [BurstCompile(CompileSynchronously = true)]
        [AOT.MonoPInvokeCallback(typeof(AllocatorManager.TryFunction))]
        private static unsafe int Allocate(IntPtr state, ref AllocatorManager.Block block)
            => ((EventQueueAllocator*)state)->Try(ref block);

        public void Dispose() => m_Blocks.Dispose();
    }
}

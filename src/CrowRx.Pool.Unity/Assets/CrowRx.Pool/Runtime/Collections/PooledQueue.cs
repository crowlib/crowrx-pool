// ReSharper disable InconsistentNaming

using System;
using System.Collections.Generic;


namespace CrowRx.Pool.Collections
{
    public class PooledQueue<T> : Queue<T>, IDisposable
    {
        internal bool IsDisposed;


        internal PooledQueue()
        {
        }

        internal PooledQueue(IEnumerable<T> data) : base(data)
        {
        }


        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;

            Clear();

            QueuePool<T>.Restore(this);
        }
    }

    public static class QueuePool<T>
    {
        private const int ResizeCapacity = 4;

        private static Stack<PooledQueue<T>> _pool = new();


        public static PooledQueue<T> Get()
        {
            CreatePool();

            if (_pool.TryPop(out PooledQueue<T> pooledQueue))
            {
                pooledQueue.IsDisposed = false;

                return pooledQueue;
            }

            return new PooledQueue<T>();
        }

        public static PooledQueue<T> Get(in IEnumerable<T> source)
        {
            if (source is null)
            {
                return Get();
            }

            CreatePool();

            if (_pool.TryPop(out PooledQueue<T> pooled))
            {
                pooled.IsDisposed = false;

                foreach (T item in source)
                {
                    pooled.Enqueue(item);
                }

                return pooled;
            }

            return new PooledQueue<T>(source);
        }

        internal static void Restore(PooledQueue<T> pooled) => _pool?.Push(pooled);

        private static void CreatePool()
        {
            if (_pool is not null)
            {
                return;
            }

            _pool = new Stack<PooledQueue<T>>();

            for (int i = 0; i < ResizeCapacity; i++)
            {
                _pool.Push(new PooledQueue<T>());
            }
        }
    }
}
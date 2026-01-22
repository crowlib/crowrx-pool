// ReSharper disable InconsistentNaming

using System;
using System.Collections.Generic;


namespace CrowRx.Pool.Collections
{
    public class PooledList<T> : List<T>, IDisposable
    {
        internal bool IsDisposed;


        internal PooledList()
        {
        }

        internal PooledList(IEnumerable<T> data) : base(data)
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

            ListPool<T>.Restore(this);
        }
    }

    public static class ListPool<T>
    {
        private const int ResizeCapacity = 4;

        private static Stack<PooledList<T>> _pool;


        public static PooledList<T> Get()
        {
            CreatePool();

            if (_pool.TryPop(out PooledList<T> pooled))
            {
                pooled.IsDisposed = false;

                return pooled;
            }

            return new PooledList<T>();
        }

        public static PooledList<T> Get(in IEnumerable<T> source)
        {
            if (source is null)
            {
                return Get();
            }

            CreatePool();

            if (_pool.TryPop(out PooledList<T> pooled))
            {
                pooled.IsDisposed = false;
                pooled.AddRange(source);

                return pooled;
            }

            return new PooledList<T>(source);
        }

        internal static void Restore(PooledList<T> pooled) => _pool?.Push(pooled);

        private static void CreatePool()
        {
            if (_pool is not null)
            {
                return;
            }

            _pool = new Stack<PooledList<T>>();

            for (int i = 0; i < ResizeCapacity; i++)
            {
                _pool.Push(new PooledList<T>());
            }
        }
    }
}
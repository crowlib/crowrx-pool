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

        internal PooledList(in IEnumerable<T> data) : base(data)
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

        private static Stack<PooledList<T>>? _pool;


        private static Stack<PooledList<T>> Pool
        {
            get
            {
                if (_pool is not null)
                {
                    return _pool;
                }

                _pool = new Stack<PooledList<T>>();

                for (int i = 0; i < ResizeCapacity; i++)
                {
                    _pool.Push(new PooledList<T>());
                }

                return _pool;
            }
        }


        public static PooledList<T> Get()
        {
            if (Pool.TryPop(out PooledList<T> pooled))
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

            if (Pool.TryPop(out PooledList<T> pooled))
            {
                pooled.IsDisposed = false;
                pooled.AddRange(source);

                return pooled;
            }

            return new PooledList<T>(source);
        }

        internal static void Restore(PooledList<T> pooled) => _pool?.Push(pooled);
    }
}
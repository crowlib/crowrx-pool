// ReSharper disable InconsistentNaming

using System;
using System.Collections.Generic;


namespace CrowRx.Pool.Collections
{
    public class PooledHashSet<T> : HashSet<T>, IDisposable
    {
        internal bool IsDisposed;


        internal PooledHashSet()
        {
        }

        internal PooledHashSet(in IEnumerable<T> source, in IEqualityComparer<T> comparer) : base(source, comparer)
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

            HashSetPool<T>.Restore(this);
        }
    }

    public static class HashSetPool<T>
    {
        private static Stack<PooledHashSet<T>> _pool;


        public static PooledHashSet<T> Get()
        {
            _pool ??= new Stack<PooledHashSet<T>>();

            if (_pool.TryPop(out PooledHashSet<T> pooled))
            {
                pooled.IsDisposed = false;

                return pooled;
            }

            return new PooledHashSet<T>();
        }

        public static PooledHashSet<T> Get(in IEnumerable<T> source, in IEqualityComparer<T> comparer)
        {
            if (source is null)
            {
                return Get();
            }
            
            _pool ??= new Stack<PooledHashSet<T>>();

            if (_pool.TryPop(out PooledHashSet<T> pooled))
            {
                pooled.UnionWith(source);
                pooled.IsDisposed = false;

                return pooled;
            }

            return new PooledHashSet<T>(source, comparer);
        }

        public static PooledHashSet<T> Get(IEnumerable<T> source) => Get(source, null);

        internal static void Restore(PooledHashSet<T> pooled) => _pool?.Push(pooled);
    }
}
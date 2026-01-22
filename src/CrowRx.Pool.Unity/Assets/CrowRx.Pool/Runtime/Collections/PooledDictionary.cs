// ReSharper disable InconsistentNaming

using System;
using System.Collections.Generic;


namespace CrowRx.Pool.Collections
{
    public class PooledDictionary<TKey, T> : Dictionary<TKey, T>, IDisposable
    {
        internal bool IsDisposed;


        internal PooledDictionary()
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

            DictionaryPool<TKey, T>.Restore(this);
        }
    }

    public static class DictionaryPool<TKey, T>
    {
        private static Stack<PooledDictionary<TKey, T>> _pool;


        public static PooledDictionary<TKey, T> Get()
        {
            _pool ??= new Stack<PooledDictionary<TKey, T>>();

            if (_pool.TryPop(out PooledDictionary<TKey, T> pooled))
            {
                pooled.IsDisposed = false;

                return pooled;
            }

            return new PooledDictionary<TKey, T>();
        }

        internal static void Restore(PooledDictionary<TKey, T> pooled) => _pool?.Push(pooled);
    }
}
using System;
using System.Collections.Generic;
using System.Text;


namespace CrowRx.Pool.Text
{
    public interface IPooledStringBuilder : IDisposable
    {
        StringBuilder StringBuilder { get; }
    }


    public static class StringBuilderPool
    {
        private class PooledStringBuilder : IPooledStringBuilder
        {
            internal bool IsDisposed;


            public StringBuilder StringBuilder { get; }


            public PooledStringBuilder(StringBuilder stringBuilder)
            {
                StringBuilder = stringBuilder;
            }


            public void Dispose()
            {
                if (IsDisposed)
                {
                    return;
                }

                IsDisposed = true;

                StringBuilder.Clear();

                _pool?.Push(this);
            }
        }


        private const int ResizeCapacity = 4;

        private static Stack<PooledStringBuilder>? _pool;


        private static Stack<PooledStringBuilder> Pool
        {
            get
            {
                if (_pool is not null)
                {
                    return _pool;
                }

                _pool = new Stack<PooledStringBuilder>();

                for (int i = 0; i < ResizeCapacity; i++)
                {
                    _pool.Push(new PooledStringBuilder(new StringBuilder()));
                }

                return _pool;
            }
        }


        public static IPooledStringBuilder Get()
        {
            if (Pool.TryPop(out PooledStringBuilder pooled))
            {
                pooled.IsDisposed = false;

                return pooled;
            }

            return new PooledStringBuilder(new StringBuilder());
        }

        public static IPooledStringBuilder Get(in string source)
        {
            if (source is null)
            {
                return Get();
            }

            if (Pool.TryPop(out PooledStringBuilder pooled))
            {
                pooled.IsDisposed = false;
                pooled.StringBuilder.Append(source);

                return pooled;
            }

            return new PooledStringBuilder(new StringBuilder(source));
        }
    }
}
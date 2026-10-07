using System;
using System.Collections.Generic;
using System.Text;

namespace CrowRx.Pool.Text
{
    /// <summary>Dispose로 풀에 반환하는 StringBuilder 대여입니다.</summary>
    public interface IPooledStringBuilder : IDisposable
    {
        /// <summary>문자열 조립에 사용할 StringBuilder입니다. 반환 후에는 이 참조를 사용하지 않아야 합니다.</summary>
        StringBuilder StringBuilder { get; }
    }

    /// <summary>StringBuilder와 대여 객체를 재사용하는 풀입니다. 필요할 때 생성하며 예열과 용량 확보에는 할당이 발생할 수 있습니다.</summary>
    /// <remarks>스레드 안전하지 않습니다. 반환 후 참조 접근과 재대여된 객체의 오래된 참조로 호출하는 Dispose를 검출하지 않습니다.</remarks>
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

        private static Stack<PooledStringBuilder>? _pool;

        private static Stack<PooledStringBuilder> Pool => _pool ??= new Stack<PooledStringBuilder>();

        /// <summary>내용이 없는 StringBuilder를 대여합니다. 대기 객체가 없으면 하나를 새로 생성합니다.</summary>
        public static IPooledStringBuilder Get()
        {
            if (Pool.TryPop(out PooledStringBuilder pooled))
            {
                pooled.IsDisposed = false;

                return pooled;
            }

            return new PooledStringBuilder(new StringBuilder());
        }

        /// <summary>최소 capacity개의 문자를 담을 용량의 빈 StringBuilder를 대여합니다. 음수는 ArgumentOutOfRangeException입니다.</summary>
        /// <remarks>기존 용량은 줄이지 않습니다. 용량 확보에 실패하면 대여한 객체를 반환한 뒤 예외를 다시 던집니다.</remarks>
        public static IPooledStringBuilder Get(int capacity)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (!Pool.TryPop(out PooledStringBuilder pooled))
            {
                return new PooledStringBuilder(new StringBuilder(capacity));
            }

            pooled.IsDisposed = false;
            try
            {
                pooled.StringBuilder.EnsureCapacity(capacity);

                return pooled;
            }
            catch
            {
                pooled.Dispose();
                throw;
            }
        }

        /// <summary>source를 초기 내용으로 갖는 StringBuilder를 대여합니다. null은 빈 대여입니다.</summary>
        /// <remarks>재사용 시 source 길이만큼 용량을 확보한 뒤 복사합니다. 입력 처리에 실패하면 대여한 객체를 반환한 뒤 예외를 다시 던집니다.</remarks>
        public static IPooledStringBuilder Get(in string? source)
        {
            if (source is null)
            {
                return Get();
            }

            if (Pool.TryPop(out PooledStringBuilder pooled))
            {
                pooled.IsDisposed = false;
                try
                {
                    pooled.StringBuilder.EnsureCapacity(source.Length);
                    pooled.StringBuilder.Append(source);

                    return pooled;
                }
                catch
                {
                    pooled.Dispose();
                    throw;
                }
            }

            return new PooledStringBuilder(new StringBuilder(source));
        }

        /// <summary>최소 inactiveCount개의 대기 객체를 준비하고 각각 최소 capacity개의 문자를 담을 용량을 확보합니다.</summary>
        /// <remarks>활성 대여와 기존의 더 큰 용량은 유지합니다. 음수 인수는 ArgumentOutOfRangeException입니다.</remarks>
        public static void Warmup(int inactiveCount, int capacity = 0)
        {
            if (inactiveCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(inactiveCount));
            }

            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Stack<PooledStringBuilder> pool = Pool;
            foreach (PooledStringBuilder pooled in pool)
            {
                pooled.StringBuilder.EnsureCapacity(capacity);
            }

            while (pool.Count < inactiveCount)
            {
                pool.Push(new PooledStringBuilder(new StringBuilder(capacity)) { IsDisposed = true });
            }
        }
    }
}
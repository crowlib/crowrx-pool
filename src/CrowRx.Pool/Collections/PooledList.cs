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

                return _pool;
            }
        }

        /// <summary>빈 리스트를 대여합니다. 대기 리스트가 없으면 필요할 때 새로 생성합니다.</summary>
        public static PooledList<T> Get()
        {
            if (Pool.TryPop(out PooledList<T> pooled))
            {
                pooled.IsDisposed = false;

                return pooled;
            }

            return new PooledList<T>();
        }

        /// <summary>최소 capacity개의 원소를 증설 없이 담을 빈 리스트를 대여합니다. 기존 용량은 줄이지 않으며 음수는 ArgumentOutOfRangeException입니다.</summary>
        /// <remarks>대기 리스트가 없거나 용량이 부족하면 객체 또는 배열 할당이 발생합니다.</remarks>
        public static PooledList<T> Get(int capacity)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            PooledList<T> pooled = Get();
            try
            {
                if (pooled.Capacity < capacity)
                {
                    pooled.Capacity = capacity;
                }

                return pooled;
            }
            catch
            {
                pooled.Dispose();
                throw;
            }
        }

        /// <summary>source의 원소를 복사하여 대여합니다. null은 빈 대여이며 복사 중 예외가 발생하면 리스트를 정리하고 풀에 반환한 뒤 다시 던집니다.</summary>
        /// <remarks>배열과 List 등 ICollection 입력은 AddRange의 일괄 복사를 사용합니다. IReadOnlyCollection만 구현한 입력은 Count로 용량을 먼저 확보하며, 입력의 열거자는 할당할 수 있습니다.</remarks>
        public static PooledList<T> Get(in IEnumerable<T>? source)
        {
            if (source is null)
            {
                return Get();
            }

            // ICollection은 AddRange에서 개수 확인과 복사를 처리하므로 추가로 순회하지 않는다.
            PooledList<T> pooled =
                source is not ICollection<T> && source is IReadOnlyCollection<T> readOnly
                    ? Get(readOnly.Count)
                    : Get();
            try
            {
                pooled.AddRange(source);

                return pooled;
            }
            catch
            {
                pooled.Dispose();
                throw;
            }
        }

        /// <summary>최소 inactiveCount개의 대기 리스트와 원소 배열을 예열합니다.</summary>
        /// <remarks>기존 대기 리스트도 최소 capacity로 준비하며 용량은 줄이지 않습니다. 활성 대여는 건드리지 않습니다. 음수 인수는 ArgumentOutOfRangeException입니다.</remarks>
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

            Stack<PooledList<T>> pool = Pool;
            foreach (PooledList<T> pooled in pool)
            {
                if (pooled.Capacity < capacity)
                {
                    pooled.Capacity = capacity;
                }
            }

            while (pool.Count < inactiveCount)
            {
                pool.Push(new PooledList<T> { Capacity = capacity });
            }
        }

        internal static void Restore(PooledList<T> pooled) => _pool?.Push(pooled);
    }
}
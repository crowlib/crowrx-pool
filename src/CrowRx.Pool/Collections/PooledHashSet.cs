using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CrowRx.Pool.Collections
{
    public class PooledHashSet<T> : HashSet<T>, IDisposable
    {
        internal bool IsDisposed;

        internal PooledHashSet()
        {
        }

        internal PooledHashSet(IEqualityComparer<T>? comparer) : base(comparer)
        {
        }

        internal PooledHashSet(in IEnumerable<T> source, in IEqualityComparer<T>? comparer) : base(source, comparer)
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
        private static Stack<PooledHashSet<T>>? _pool;
        private static Dictionary<IEqualityComparer<T>, Stack<PooledHashSet<T>>>? _comparerPools;

        /// <summary>기본 비교 규칙의 빈 집합을 대여합니다. 대기 집합이 없으면 필요할 때 새로 생성합니다.</summary>
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

        /// <summary>지정한 비교 규칙으로 최소 capacity개의 서로 다른 원소를 증설 없이 담을 빈 집합을 대여합니다.</summary>
        /// <remarks>용량은 줄이지 않습니다. 음수는 ArgumentOutOfRangeException입니다. null은 기본 비교 규칙이며 다른 comparer 인스턴스는 별도 풀을 사용합니다.</remarks>
        public static PooledHashSet<T> Get(int capacity, IEqualityComparer<T>? comparer = null)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Stack<PooledHashSet<T>> pool = GetPool(comparer);
            if (!pool.TryPop(out PooledHashSet<T> pooled))
            {
                pooled = new PooledHashSet<T>(comparer);
            }

            pooled.IsDisposed = false;
            try
            {
                pooled.EnsureCapacity(capacity);

                return pooled;
            }
            catch
            {
                pooled.Dispose();
                throw;
            }
        }

        /// <summary>source의 원소를 지정한 비교 규칙으로 모아 대여합니다. 중복은 하나로 합쳐지며 null은 빈 대여입니다.</summary>
        /// <remarks>새 객체가 필요한 HashSet 입력은 기본 HashSet의 복사 생성자를 사용합니다. 그 외에는 알 수 있는 입력 개수로 용량을 준비하고 배열/List/HashSet 입력을 구체 타입으로 순회합니다. 대여한 객체의 입력 처리 중 예외는 집합을 정리하고 반환한 뒤 다시 던집니다.</remarks>
        public static PooledHashSet<T> Get(in IEnumerable<T>? source, in IEqualityComparer<T>? comparer)
        {
            int capacity = source switch
            {
                ICollection<T> collection => collection.Count,
                IReadOnlyCollection<T> readOnly => readOnly.Count,
                _ => 0
            };

            Stack<PooledHashSet<T>> pool = GetPool(comparer);
            if (pool.TryPop(out PooledHashSet<T> pooled))
            {
                pooled.IsDisposed = false;
            }
            else if (source is HashSet<T> setSource)
            {
                return new PooledHashSet<T>(setSource, comparer);
            }
            else
            {
                pooled = new PooledHashSet<T>(comparer);
            }

            try
            {
                pooled.EnsureCapacity(capacity);

                switch (source)
                {
                    case T[] array:
                    {
                        foreach (T item in array)
                        {
                            pooled.Add(item);
                        }

                        break;
                    }
                    case List<T> list:
                    {
                        foreach (T item in list)
                        {
                            pooled.Add(item);
                        }

                        break;
                    }
                    case HashSet<T> set:
                    {
                        foreach (T item in set)
                        {
                            pooled.Add(item);
                        }

                        break;
                    }
                    default:
                    {
                        if (source is not null)
                        {
                            pooled.UnionWith(source);
                        }

                        break;
                    }
                }

                return pooled;
            }
            catch
            {
                pooled.Dispose();
                throw;
            }
        }

        /// <summary>source의 원소를 기본 비교 규칙으로 모아 대여합니다. null은 빈 대여입니다.</summary>
        public static PooledHashSet<T> Get(IEnumerable<T>? source) => Get(source, null);

        /// <summary>지정한 비교 규칙의 대기 집합을 최소 inactiveCount개 준비하고 각각 최소 capacity의 원소 용량을 확보합니다.</summary>
        /// <remarks>기존 용량은 줄이지 않으며 활성 대여는 건드리지 않습니다. 음수 인수는 ArgumentOutOfRangeException입니다. 기본 비교 규칙과 각 comparer 인스턴스의 풀은 따로 예열합니다.</remarks>
        public static void Warmup(int inactiveCount, int capacity = 0, IEqualityComparer<T>? comparer = null)
        {
            if (inactiveCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(inactiveCount));
            }

            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Stack<PooledHashSet<T>> pool = GetPool(comparer);
            foreach (PooledHashSet<T> pooled in pool)
            {
                pooled.EnsureCapacity(capacity);
            }

            while (pool.Count < inactiveCount)
            {
                var pooled = new PooledHashSet<T>(comparer);
                pooled.EnsureCapacity(capacity);
                pool.Push(pooled);
            }
        }

        private static Stack<PooledHashSet<T>> GetPool(IEqualityComparer<T>? comparer)
        {
            if (comparer is null || ReferenceEquals(comparer, EqualityComparer<T>.Default))
            {
                return _pool ??= new Stack<PooledHashSet<T>>();
            }

            _comparerPools ??= new Dictionary<IEqualityComparer<T>, Stack<PooledHashSet<T>>>(new ComparerIdentity());
            if (!_comparerPools.TryGetValue(comparer, out Stack<PooledHashSet<T>> pool))
            {
                pool = new Stack<PooledHashSet<T>>();
                _comparerPools.Add(comparer, pool);
            }

            return pool;
        }

        private sealed class ComparerIdentity : IEqualityComparer<IEqualityComparer<T>>
        {
            public bool Equals(IEqualityComparer<T>? x, IEqualityComparer<T>? y) => ReferenceEquals(x, y);

            public int GetHashCode(IEqualityComparer<T> obj) => RuntimeHelpers.GetHashCode(obj);
        }

        internal static void Restore(PooledHashSet<T> pooled) => GetPool(pooled.Comparer).Push(pooled);
    }
}
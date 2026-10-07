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

        internal PooledDictionary(IDictionary<TKey, T> source) : base(source)
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
        private static Stack<PooledDictionary<TKey, T>>? _pool;

        /// <summary>기본 비교 규칙의 빈 딕셔너리를 대여합니다. 대기 딕셔너리가 없으면 필요할 때 새로 생성합니다.</summary>
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

        /// <summary>최소 capacity개의 키와 값을 증설 없이 담을 빈 딕셔너리를 대여합니다. 용량은 줄이지 않으며 음수는 ArgumentOutOfRangeException입니다.</summary>
        public static PooledDictionary<TKey, T> Get(int capacity)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            PooledDictionary<TKey, T> pooled = Get();
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

        /// <summary>source의 키와 값을 기본 비교 규칙으로 복사하여 대여합니다. null은 빈 대여이며 중복 키는 ArgumentException입니다.</summary>
        /// <remarks>새 객체가 필요한 IDictionary 입력은 기본 Dictionary의 복사 생성자를 사용합니다. 재사용 시에는 알 수 있는 입력 개수로 용량을 준비하고 배열/List/Dictionary 입력을 구체 타입으로 순회합니다. 대여한 객체의 입력 처리 중 예외는 딕셔너리를 정리하고 반환한 뒤 다시 던집니다.</remarks>
        public static PooledDictionary<TKey, T> Get(in IEnumerable<KeyValuePair<TKey, T>>? source)
        {
            int capacity = source switch
            {
                ICollection<KeyValuePair<TKey, T>> collection => collection.Count,
                IReadOnlyCollection<KeyValuePair<TKey, T>> readOnly => readOnly.Count,
                _ => 0
            };

            Stack<PooledDictionary<TKey, T>> pool = _pool ??= new Stack<PooledDictionary<TKey, T>>();
            if (pool.TryPop(out PooledDictionary<TKey, T> pooled))
            {
                pooled.IsDisposed = false;
            }
            else if (source is IDictionary<TKey, T> dictionarySource)
            {
                return new PooledDictionary<TKey, T>(dictionarySource);
            }
            else
            {
                pooled = new PooledDictionary<TKey, T>();
            }

            try
            {
                pooled.EnsureCapacity(capacity);

                switch (source)
                {
                    case KeyValuePair<TKey, T>[] array:
                    {
                        foreach (KeyValuePair<TKey, T> item in array)
                        {
                            pooled.Add(item.Key, item.Value);
                        }

                        break;
                    }
                    case List<KeyValuePair<TKey, T>> list:
                    {
                        foreach (KeyValuePair<TKey, T> item in list)
                        {
                            pooled.Add(item.Key, item.Value);
                        }

                        break;
                    }
                    case Dictionary<TKey, T> dictionary:
                    {
                        foreach (KeyValuePair<TKey, T> item in dictionary)
                        {
                            pooled.Add(item.Key, item.Value);
                        }

                        break;
                    }
                    default:
                    {
                        if (source is not null)
                        {
                            foreach (KeyValuePair<TKey, T> item in source)
                            {
                                pooled.Add(item.Key, item.Value);
                            }
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

        /// <summary>최소 inactiveCount개의 대기 딕셔너리를 준비하고 각각 최소 capacity의 키와 값 용량을 확보합니다.</summary>
        /// <remarks>기존 용량은 줄이지 않으며 활성 대여는 건드리지 않습니다. 음수 인수는 ArgumentOutOfRangeException입니다.</remarks>
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

            Stack<PooledDictionary<TKey, T>> pool = _pool ??= new Stack<PooledDictionary<TKey, T>>();
            foreach (PooledDictionary<TKey, T> pooled in pool)
            {
                pooled.EnsureCapacity(capacity);
            }

            while (pool.Count < inactiveCount)
            {
                var pooled = new PooledDictionary<TKey, T>();
                pooled.EnsureCapacity(capacity);
                pool.Push(pooled);
            }
        }

        internal static void Restore(PooledDictionary<TKey, T> pooled) => _pool?.Push(pooled);
    }
}
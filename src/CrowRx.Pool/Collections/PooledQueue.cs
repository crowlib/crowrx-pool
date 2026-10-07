using System;
using System.Collections.Generic;

namespace CrowRx.Pool.Collections
{
    /// <summary>사용 후 Dispose로 풀에 반환하는 Queue입니다. 원소 접근과 순회는 기본 Queue 구현을 그대로 사용합니다.</summary>
    /// <remarks>
    /// 반환하면 원소를 정리하고 배열은 유지합니다. 반환 후에는 참조를 사용하지 않아야 합니다.
    /// 재대여 후 오래된 참조의 접근과 Dispose를 검출하지 않습니다.
    /// 스레드 안전하지 않으며 같은 T의 풀 작업과 큐 접근은 외부에서 직렬화해야 합니다.
    /// </remarks>
    public class PooledQueue<T> : Queue<T>, IDisposable
    {
        internal bool IsDisposed;
        internal int CapacityFloor;

        internal PooledQueue()
        {
        }

        internal PooledQueue(int capacity) : base(capacity)
        {
            CapacityFloor = capacity;
        }

        internal PooledQueue(ICollection<T> source) : base(source)
        {
            CapacityFloor = Count;
        }

        /// <summary>기본 Queue의 정책으로 배열을 축소하고 풀에서 사용하는 용량 하한을 낮춥니다.</summary>
        /// <remarks>재할당 가능성이 있으므로 반복 반환에는 사용하지 않습니다. Queue로 변환하여 호출하면 용량 하한 갱신을 우회합니다.</remarks>
        public new void TrimExcess()
        {
            base.TrimExcess();
            CapacityFloor = Count;
        }

        /// <summary>원소를 정리하고 풀에 반환합니다. 재대여 전의 중복 반환은 무시합니다.</summary>
        /// <remarks>재대여된 객체의 오래된 참조로 호출하면 현재 사용 중인 큐도 반환되므로 참조를 using 범위 밖으로 보관하지 않아야 합니다.</remarks>
        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            int count = Count;
            if (count > CapacityFloor)
            {
                CapacityFloor = count;
            }

            Clear();
            QueuePool<T>.Restore(this);
        }
    }

    /// <summary>엔진 독립적인 Queue 객체 풀입니다. 생성과 명시적 예열에 할당이 있으며 충분히 예열한 반복 사용은 객체와 배열을 재사용합니다.</summary>
    public static class QueuePool<T>
    {
        private static Stack<PooledQueue<T>>? _pool;
        private static int _maxInactive = 32;

        /// <summary>현재 대기 큐 수입니다. 활성 대여는 포함하지 않습니다.</summary>
        public static int CountInactive => _pool?.Count ?? 0;

        /// <summary>같은 T에서 보관할 대기 큐 수 상한입니다. 기본값은 32이며 배열 용량의 상한은 아닙니다.</summary>
        /// <remarks>음수는 ArgumentOutOfRangeException입니다. 축소 시 초과 대기 큐만 놓아주고 활성 대여는 건드리지 않습니다.</remarks>
        public static int MaxInactive
        {
            get => _maxInactive;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _maxInactive = value;
                while (_pool is not null && _pool.Count > value)
                {
                    _pool.Pop();
                }
            }
        }

        /// <summary>빈 큐를 대여합니다. 대기 큐가 없으면 필요할 때 새로 생성합니다.</summary>
        public static PooledQueue<T> Get()
        {
            _pool ??= new Stack<PooledQueue<T>>();
            if (_pool.TryPop(out PooledQueue<T> pooled))
            {
                pooled.IsDisposed = false;
                return pooled;
            }

            return new PooledQueue<T>();
        }

        /// <summary>최소 capacity개의 원소를 증설 없이 담을 빈 큐를 대여합니다. 음수는 ArgumentOutOfRangeException입니다.</summary>
        /// <remarks>
        /// 보관한 용량 하한이 부족하면 새 큐로 교체합니다. netstandard2.1에 Queue.EnsureCapacity가 없어 생성자로 용량을 준비합니다.
        /// 원소 연산에는 추적 비용을 추가하지 않으므로 큐를 비우기 전에 자동 증설한 여유 용량은 확인하지 못할 수 있습니다.
        /// Queue로 변환하여 TrimExcess를 직접 호출한 경우에는 기록한 용량 하한을 신뢰할 수 없습니다.
        /// </remarks>
        public static PooledQueue<T> Get(int capacity)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (capacity == 0)
            {
                return Get();
            }

            _pool ??= new Stack<PooledQueue<T>>();
            if (_pool.TryPop(out PooledQueue<T> pooled))
            {
                if (pooled.CapacityFloor >= capacity)
                {
                    pooled.IsDisposed = false;
                    return pooled;
                }

                try
                {
                    return new PooledQueue<T>(capacity);
                }
                catch
                {
                    _pool.Push(pooled);
                    throw;
                }
            }

            return new PooledQueue<T>(capacity);
        }

        /// <summary>source를 FIFO 순서로 복사하여 대여합니다. null은 빈 대여입니다.</summary>
        /// <remarks>새 객체가 필요한 ICollection 입력은 기본 Queue의 복사 생성자를 사용합니다. 그 외에는 알 수 있는 입력 개수로 용량을 준비하고 배열/List/Queue 입력을 구체 타입으로 순회합니다. 대여한 큐의 입력 처리 중 예외는 정리 후 반환하며, 교체 객체 생성이 실패하면 기존 대기 큐를 복원합니다.</remarks>
        public static PooledQueue<T> Get(in IEnumerable<T>? source)
        {
            int capacity = source switch
            {
                ICollection<T> collection => collection.Count,
                IReadOnlyCollection<T> readOnly => readOnly.Count,
                _ => 0
            };

            PooledQueue<T> pooled;
            if (source is ICollection<T> collectionSource)
            {
                if (capacity < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(capacity));
                }

                Stack<PooledQueue<T>> pool = _pool ??= new Stack<PooledQueue<T>>();
                if (!pool.TryPop(out pooled))
                {
                    return new PooledQueue<T>(collectionSource);
                }

                if (pooled.CapacityFloor < capacity)
                {
                    try
                    {
                        return new PooledQueue<T>(collectionSource);
                    }
                    catch
                    {
                        pool.Push(pooled);
                        throw;
                    }
                }

                pooled.IsDisposed = false;
            }
            else
            {
                pooled = Get(capacity);
            }

            try
            {
                switch (source)
                {
                    case T[] array:
                    {
                        foreach (T item in array)
                        {
                            pooled.Enqueue(item);
                        }

                        break;
                    }
                    case List<T> list:
                    {
                        foreach (T item in list)
                        {
                            pooled.Enqueue(item);
                        }

                        break;
                    }
                    case Queue<T> queue:
                    {
                        foreach (T item in queue)
                        {
                            pooled.Enqueue(item);
                        }

                        break;
                    }
                    default:
                    {
                        if (source is not null)
                        {
                            foreach (T item in source)
                            {
                                pooled.Enqueue(item);
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

        /// <summary>최소 inactiveCount개의 대기 큐와 각각 최소 capacity의 원소 용량을 준비합니다.</summary>
        /// <remarks>기존 용량 하한이 부족한 대기 큐는 교체하며 활성 대여는 건드리지 않습니다. 음수 인수 또는 MaxInactive를 초과하는 개수는 ArgumentOutOfRangeException입니다.</remarks>
        public static void Warmup(int inactiveCount, int capacity = 0)
        {
            if (inactiveCount < 0 || inactiveCount > MaxInactive)
            {
                throw new ArgumentOutOfRangeException(nameof(inactiveCount));
            }

            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Stack<PooledQueue<T>> pool = _pool ??= new Stack<PooledQueue<T>>();
            bool needsPreparation = false;
            foreach (PooledQueue<T> pooled in pool)
            {
                if (pooled.CapacityFloor < capacity)
                {
                    needsPreparation = true;
                    break;
                }
            }

            if (needsPreparation)
            {
                // 준비가 실패하면 기존 풀은 유지하도록 교체 객체를 먼저 모두 만든다.
                PooledQueue<T>[] prepared = pool.ToArray();
                for (int i = 0; i < prepared.Length; i++)
                {
                    if (prepared[i].CapacityFloor < capacity)
                    {
                        prepared[i] = new PooledQueue<T>(capacity) { IsDisposed = true };
                    }
                }

                pool.Clear();
                for (int i = prepared.Length - 1; i >= 0; i--)
                {
                    pool.Push(prepared[i]);
                }
            }

            while (pool.Count < inactiveCount)
            {
                pool.Push(new PooledQueue<T>(capacity) { IsDisposed = true });
            }
        }

        /// <summary>대기 큐를 풀에서 놓아줍니다. 활성 대여는 유지되며 이후 반환하면 현재 보관 상한을 적용합니다.</summary>
        public static void Clear() => _pool?.Clear();

        internal static void Restore(PooledQueue<T> pooled)
        {
            if (_pool is not null && _pool.Count < _maxInactive)
            {
                _pool.Push(pooled);
            }
        }
    }
}
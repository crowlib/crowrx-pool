using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CrowRx.Pool.Collections
{
    /// <summary>큐의 한 번의 대여를 나타내는 값형 핸들입니다.</summary>
    /// <remarks>
    /// 복사본은 같은 대여를 공유합니다. 하나를 반환하면 모든 복사본의 컬렉션 접근은
    /// ObjectDisposedException을 던지며 재대여되어도 다시 유효해지지 않습니다.
    /// default도 유효한 대여가 아닙니다. 스레드 안전하지 않으며 같은 T의 풀 호출과 접근을 외부에서 직렬화해야 합니다.
    /// 직접 using/foreach는 핸들과 열거자를 박싱하지 않습니다. 인터페이스 변환은 박싱할 수 있습니다.
    /// </remarks>
    public readonly struct PooledQueue<T> : IDisposable, IReadOnlyCollection<T>, ICollection
    {
        private readonly QueueLeaseState<T>? _state;
        private readonly ulong _generation;

        internal PooledQueue(QueueLeaseState<T> state)
        {
            _state = state;
            _generation = state.Generation;
        }

        /// <summary>유효한 대여의 원소 수입니다. 반환 후와 default에서는 ObjectDisposedException이 발생합니다.</summary>
        public int Count => State.Queue.Count;

        private QueueLeaseState<T> State
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => QueueLeaseState<T>.Validate(_state, _generation);
        }

        /// <summary>끝에 원소를 추가합니다. 무효한 대여에서는 ObjectDisposedException, 용량 증설 시에는 배열 할당이 발생합니다.</summary>
        public void Enqueue(T item)
        {
            QueueLeaseState<T> state = State;
            Queue<T> inner = state.Queue;

            inner.Enqueue(item);

            int count = inner.Count;
            if (count > state.CapacityFloor)
            {
                state.CapacityFloor = count;
            }
        }

        // 구체 입력의 순회를 유지하면서 수명 검사와 용량 하한 갱신을 배치 단위로 수행한다.
        internal void EnqueueRange(T[] source)
        {
            QueueLeaseState<T> state = State;
            Queue<T> inner = state.Queue;

            try
            {
                foreach (T item in source)
                {
                    inner.Enqueue(item);
                }
            }
            finally
            {
                int count = inner.Count;
                if (count > state.CapacityFloor)
                {
                    state.CapacityFloor = count;
                }
            }
        }

        internal void EnqueueRange(List<T> source)
        {
            QueueLeaseState<T> state = State;
            Queue<T> inner = state.Queue;

            try
            {
                foreach (T item in source)
                {
                    inner.Enqueue(item);
                }
            }
            finally
            {
                int count = inner.Count;
                if (count > state.CapacityFloor)
                {
                    state.CapacityFloor = count;
                }
            }
        }

        /// <summary>첫 원소를 제거하여 반환합니다. 무효한 대여는 ObjectDisposedException, 빈 큐는 InvalidOperationException을 던집니다.</summary>
        public T Dequeue() => State.Queue.Dequeue();

        /// <summary>첫 원소를 조회합니다. 무효한 대여는 ObjectDisposedException, 빈 큐는 InvalidOperationException을 던집니다.</summary>
        public T Peek() => State.Queue.Peek();

        /// <summary>첫 원소를 제거합니다. 빈 큐에서는 false를 반환하며 무효한 대여에서는 ObjectDisposedException이 발생합니다.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryDequeue(out T result) => State.Queue.TryDequeue(out result);

        /// <summary>첫 원소를 조회합니다. 빈 큐에서는 false를 반환하며 무효한 대여에서는 ObjectDisposedException이 발생합니다.</summary>
        public bool TryPeek(out T result) => State.Queue.TryPeek(out result);

        /// <summary>원소 포함 여부를 조회합니다. 무효한 대여에서는 ObjectDisposedException이 발생합니다.</summary>
        public bool Contains(T item) => State.Queue.Contains(item);

        /// <summary>원소 참조를 정리하되 대여와 배열은 유지합니다. 무효한 대여에서는 ObjectDisposedException이 발생합니다.</summary>
        public void Clear() => State.Queue.Clear();

        /// <summary>FIFO 순서로 기존 배열에 복사합니다. 무효한 대여에서는 ObjectDisposedException, 잘못된 배열/범위에는 Queue.CopyTo의 인수 예외가 발생합니다.</summary>
        public void CopyTo(T[] array, int arrayIndex) => State.Queue.CopyTo(array, arrayIndex);

        /// <summary>FIFO 순서의 새 배열을 만듭니다. 결과 배열 할당은 의도된 동작이며 무효한 대여에서는 ObjectDisposedException이 발생합니다.</summary>
        public T[] ToArray() => State.Queue.ToArray();

        /// <summary>Queue의 정책에 따라 배열을 축소합니다. 재할당 가능성이 있으므로 반복 반환에는 사용하지 않습니다. 무효한 대여에서는 ObjectDisposedException이 발생합니다.</summary>
        public void TrimExcess()
        {
            QueueLeaseState<T> state = State;
            state.Queue.TrimExcess();
            state.CapacityFloor = state.Queue.Count;
        }

        /// <summary>대여 검사를 수행하는 구조체 열거자를 만듭니다. 직접 foreach는 할당하지 않으며 무효한 대여에서는 ObjectDisposedException이 발생합니다.</summary>
        public Enumerator GetEnumerator() => new(State, _generation);

        /// <summary>유효한 대여만 종료하고 원소를 정리합니다. 복사본·중복·오래된 세대·default의 반환은 무해하며 현재의 다른 대여에 영향을 주지 않습니다.</summary>
        public void Dispose()
        {
            if (_state is null || !_state.Active || _state.Generation != _generation)
            {
                return;
            }

            _state.Active = false;
            _state.Queue.Clear();
            QueuePool<T>.Restore(_state);
        }

        /// <summary>유효성 검사 후 열거자를 박싱합니다. 반환된 대여에서는 ObjectDisposedException이 발생합니다.</summary>
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        /// <summary>유효성 검사 후 열거자를 박싱합니다. 반환된 대여에서는 ObjectDisposedException이 발생합니다.</summary>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>항상 false입니다. 무효한 대여에서는 ObjectDisposedException이 발생합니다.</summary>
        bool ICollection.IsSynchronized
        {
            get
            {
                _ = State;

                return false;
            }
        }

        /// <summary>내부 Queue를 노출하지 않는 동기화 토큰입니다. 풀 전체의 스레드 안전성을 제공하지 않으며 무효한 대여에서는 ObjectDisposedException이 발생합니다.</summary>
        object ICollection.SyncRoot => State;

        /// <summary>유효성 검사 후 Queue의 비제네릭 배열 복사 계약을 적용합니다. 무효한 대여에서는 ObjectDisposedException이 발생합니다.</summary>
        void ICollection.CopyTo(Array array, int index) => ((ICollection)State.Queue).CopyTo(array, index);

        /// <summary>대여 세대를 공유하는 구조체 열거자입니다. 반환 후 접근은 ObjectDisposedException, MoveNext/Reset 중 컬렉션 변경은 InvalidOperationException으로 검출합니다.</summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly QueueLeaseState<T>? _state;
            private readonly ulong _generation;
            private Queue<T>.Enumerator _inner;

            internal Enumerator(QueueLeaseState<T> state, ulong generation)
            {
                _state = state;
                _generation = generation;
                _inner = state.Queue.GetEnumerator();
            }

            private void Validate() => QueueLeaseState<T>.Validate(_state, _generation);

            /// <summary>현재 원소입니다. 무효한 대여는 ObjectDisposedException을 던집니다. 순회 전후 위치의 동작은 실행 런타임의 Queue 열거자 계약을 따릅니다.</summary>
            public T Current
            {
                get
                {
                    Validate();

                    return _inner.Current;
                }
            }

            /// <summary>현재 원소를 object로 반환하므로 값형 원소는 박싱될 수 있습니다. Current와 동일한 예외 계약입니다.</summary>
            object? IEnumerator.Current
            {
                get
                {
                    Validate();

                    return CurrentCore(ref _inner);
                }
            }

            private static object? CurrentCore<TEnumerator>(ref TEnumerator enumerator) where TEnumerator : struct, IEnumerator => enumerator.Current;

            /// <summary>다음 원소로 이동합니다. 무효한 대여와 순회 중 변경을 검출합니다.</summary>
            public bool MoveNext()
            {
                Validate();

                return _inner.MoveNext();
            }

            /// <summary>순회 시작 전으로 되돌립니다. 무효한 대여와 순회 중 변경을 검출하며 내부 열거자를 박싱하지 않습니다.</summary>
            public void Reset()
            {
                Validate();

                ResetCore(ref _inner);
            }

            private static void ResetCore<TEnumerator>(ref TEnumerator enumerator) where TEnumerator : struct, IEnumerator => enumerator.Reset();

            /// <summary>열거만 종료합니다. 큐 대여를 반환하지 않으며 대여 종료 후에도 정리용 호출은 허용합니다.</summary>
            public void Dispose() => _inner.Dispose();
        }
    }

    internal sealed class QueueLeaseState<T>
    {
        internal Queue<T> Queue;
        internal int CapacityFloor;
        internal ulong Generation;
        internal bool Active;
        internal QueueLeaseState<T>? NextInactive;

        internal QueueLeaseState(int capacity)
        {
            Queue = new Queue<T>(capacity);
            CapacityFloor = capacity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static QueueLeaseState<T> Validate(QueueLeaseState<T>? state, ulong generation)
        {
            if (state is null || !state.Active || state.Generation != generation)
            {
                throw CreateInvalidLeaseException();
            }

            return state;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ObjectDisposedException CreateInvalidLeaseException() => new(nameof(PooledQueue<T>), "종료되었거나 초기화되지 않은 큐 대여입니다.");

        internal void PrepareCapacity(int capacity)
        {
            if (CapacityFloor < capacity)
            {
                Queue = new Queue<T>(capacity);
                CapacityFloor = capacity;
            }
        }
    }

    /// <summary>엔진 독립적인 큐 대여 풀입니다. 같은 T의 모든 호출과 대여 접근은 외부에서 직렬화해야 하며 세대 검사는 스레드 안전성을 제공하지 않습니다.</summary>
    public static class QueuePool<T>
    {
        private const int InitialCapacity = 4;

        private static QueueLeaseState<T>? _cached;
        private static QueueLeaseState<T>? _overflow;

        private static int _maxInactive = 32;

        /// <summary>현재 대기 객체 수입니다. 활성 대여는 포함하지 않습니다.</summary>
        public static int CountInactive { get; private set; }

        /// <summary>같은 T에서 보관할 대기 객체 수 상한입니다(기본 32). 음수는 ArgumentOutOfRangeException이며 축소 시 초과 대기 객체만 놓아줍니다. 배열 용량의 상한은 아닙니다.</summary>
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

                while (CountInactive > value)
                {
                    if (PopOverflow() is null)
                    {
                        _cached = null;
                    }

                    CountInactive--;
                }
            }
        }

        /// <summary>빈 큐를 대여합니다. 최초 생성·배열 증설은 할당하며 같은 용량/동시 대여 수로 예열한 정상 반복은 할당하지 않습니다.</summary>
        public static PooledQueue<T> Get() => Get(InitialCapacity);

        /// <summary>최소 capacity개의 원소를 증설 없이 담을 빈 큐를 대여합니다. 요청 용량을 보장하는 대기 큐를 우선 재사용하며 그런 큐가 없으면 생성자로 배열을 준비합니다. 음수는 ArgumentOutOfRangeException입니다.</summary>
        public static PooledQueue<T> Get(int capacity)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            QueueLeaseState<T>? state = _cached;
            if (state is not null && state.CapacityFloor >= capacity)
            {
                _cached = null;
            }
            else
            {
                QueueLeaseState<T>? suitable = TakeSuitableOverflow(capacity);
                if (suitable is not null)
                {
                    state = suitable;
                }
                else if (state is not null)
                {
                    _cached = null;
                }
                else
                {
                    state = PopOverflow();
                }
            }

            if (state is not null)
            {
                CountInactive--;
            }

            state ??= new QueueLeaseState<T>(Math.Max(InitialCapacity, capacity));
            state.PrepareCapacity(capacity);
            state.Generation++;
            state.Active = true;

            return new PooledQueue<T>(state);
        }

        /// <summary>source를 FIFO 순서로 채워 대여합니다. null은 빈 대여이며 배열/List의 실제 타입 경로는 열거자 할당을 피합니다. 다른 입력의 열거 예외는 큐 정리·반환 후 다시 던집니다.</summary>
        public static PooledQueue<T> Get(in IEnumerable<T>? source)
        {
            int capacity = source switch
            {
                ICollection<T> collection => collection.Count,
                IReadOnlyCollection<T> readOnly => readOnly.Count,
                _ => InitialCapacity
            };

            PooledQueue<T> queue = Get(capacity);
            try
            {
                switch (source)
                {
                    case T[] array:
                    {
                        queue.EnqueueRange(array);
                        break;
                    }
                    case List<T> list:
                    {
                        queue.EnqueueRange(list);
                        break;
                    }
                    default:
                    {
                        if (source is not null)
                        {
                            foreach (T item in source)
                            {
                                queue.Enqueue(item);
                            }
                        }

                        break;
                    }
                }

                return queue;
            }
            catch
            {
                queue.Dispose();
                throw;
            }
        }

        /// <summary>최소 지정 수의 대기 큐와 원소 배열을 예열합니다.</summary>
        /// <remarks>음수 또는 inactiveCount가 MaxInactive보다 크면 ArgumentOutOfRangeException입니다. 기존 대기 큐도 최소 capacity로 준비하며 활성 대여는 건드리지 않습니다. 동시 대여 수와 최대 원소 수에 맞춰 호출합니다.</remarks>
        public static void Warmup(int inactiveCount, int capacity = InitialCapacity)
        {
            if (inactiveCount < 0 || inactiveCount > MaxInactive)
            {
                throw new ArgumentOutOfRangeException(nameof(inactiveCount));
            }

            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _cached?.PrepareCapacity(capacity);

            for (QueueLeaseState<T>? state = _overflow; state is not null; state = state.NextInactive)
            {
                state.PrepareCapacity(capacity);
            }

            while (CountInactive < inactiveCount)
            {
                Restore(new QueueLeaseState<T>(Math.Max(InitialCapacity, capacity)));
            }
        }

        /// <summary>대기 객체만 놓아줍니다. 활성 대여는 유효하며 이후 반환은 현재 MaxInactive 한도에 따라 다시 보관됩니다.</summary>
        public static void Clear()
        {
            _cached = null;
            // 오래된 핸들이 보관 목록 전체를 붙잡지 않도록 연결도 끊는다.
            while (PopOverflow() is not null)
            {
            }

            CountInactive = 0;
        }

        private static QueueLeaseState<T>? PopOverflow()
        {
            QueueLeaseState<T>? state = _overflow;
            if (state is not null)
            {
                _overflow = state.NextInactive;
                state.NextInactive = null;
            }

            return state;
        }

        private static QueueLeaseState<T>? TakeSuitableOverflow(int capacity)
        {
            QueueLeaseState<T>? previous = null;
            for (QueueLeaseState<T>? state = _overflow; state is not null; state = state.NextInactive)
            {
                if (state.CapacityFloor >= capacity)
                {
                    if (previous is null)
                    {
                        _overflow = state.NextInactive;
                    }
                    else
                    {
                        previous.NextInactive = state.NextInactive;
                    }

                    state.NextInactive = null;
                    return state;
                }

                previous = state;
            }

            return null;
        }

        internal static void Restore(QueueLeaseState<T> state)
        {
            // 세대가 순환해 오래된 핸들이 부활하지 않도록 마지막 세대 객체는 폐기한다.
            if (state.Generation == ulong.MaxValue || CountInactive >= MaxInactive)
            {
                return;
            }

            if (_cached is null)
            {
                _cached = state;
            }
            else
            {
                state.NextInactive = _overflow;
                _overflow = state;
            }

            CountInactive++;
        }
    }
}
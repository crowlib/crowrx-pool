using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using CrowRx.Pool.Collections;

internal static class Program
{
    private static int _checks;
    private static long _sink;

    private static void Check(bool condition, string message = "contract")
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        _checks++;
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new Exception($"Expected {typeof(TException).Name}");
    }

    private static void AllInvalid(PooledQueue<int> queue)
    {
        ICollection collection = queue;
        IReadOnlyCollection<int> readOnly = queue;
        Throws<ObjectDisposedException>(() => _ = queue.Count);
        Throws<ObjectDisposedException>(() => queue.Enqueue(1));
        Throws<ObjectDisposedException>(() => queue.Dequeue());
        Throws<ObjectDisposedException>(() => queue.Peek());
        Throws<ObjectDisposedException>(() => queue.TryDequeue(out _));
        Throws<ObjectDisposedException>(() => queue.TryPeek(out _));
        Throws<ObjectDisposedException>(() => queue.Contains(1));
        Throws<ObjectDisposedException>(() => queue.Clear());
        Throws<ObjectDisposedException>(() => queue.CopyTo(new int[1], 0));
        Throws<ObjectDisposedException>(() => queue.CopyTo(null!, -1));
        Throws<ObjectDisposedException>(() => queue.ToArray());
        Throws<ObjectDisposedException>(() => queue.TrimExcess());
        Throws<ObjectDisposedException>(() => queue.GetEnumerator());
        Throws<ObjectDisposedException>(() => ((IEnumerable<int>)queue).GetEnumerator());
        Throws<ObjectDisposedException>(() => ((IEnumerable)queue).GetEnumerator());
        Throws<ObjectDisposedException>(() => _ = readOnly.Count);
        Throws<ObjectDisposedException>(() => _ = collection.Count);
        Throws<ObjectDisposedException>(() => _ = collection.IsSynchronized);
        Throws<ObjectDisposedException>(() => _ = collection.SyncRoot);
        Throws<ObjectDisposedException>(() => collection.CopyTo(new int[1], 0));
        Throws<ObjectDisposedException>(() => collection.CopyTo(null!, -1));
    }

    private static void InvalidEnumerator(PooledQueue<int>.Enumerator enumerator)
    {
        Throws<ObjectDisposedException>(() => enumerator.MoveNext());
        Throws<ObjectDisposedException>(() => _ = enumerator.Current);
        Throws<ObjectDisposedException>(() => enumerator.Reset());
        IEnumerator nonGeneric = enumerator;
        Throws<ObjectDisposedException>(() => nonGeneric.MoveNext());
        Throws<ObjectDisposedException>(() => _ = nonGeneric.Current);
        Throws<ObjectDisposedException>(() => nonGeneric.Reset());
        enumerator.Dispose(); // Cleanup is allowed after lease expiry.
    }

    private static void Lifetime()
    {
        QueuePool<int>.Clear();
        QueuePool<int>.MaxInactive = 32;
        AllInvalid(default);
        default(PooledQueue<int>).Dispose();
        InvalidEnumerator(default);
        var queue = QueuePool<int>.Get();
        queue.Enqueue(10);
        var copy = queue;
        Check(copy.Peek() == 10);
        queue.Dispose();
        Check(QueuePool<int>.CountInactive == 1);
        AllInvalid(queue);
        AllInvalid(copy);
        queue.Dispose();
        ((IDisposable)copy).Dispose();
        Check(QueuePool<int>.CountInactive == 1);
        using var next = QueuePool<int>.Get();
        Check(next.Count == 0);
        next.Enqueue(20);
        queue.Dispose();
        copy.Dispose();
        Check(next.Count == 1 && next.Peek() == 20);
        Check(QueuePool<int>.CountInactive == 0);
        AllInvalid(queue);
        AllInvalid(copy);
    }

    private static void Collection()
    {
        using var queue = QueuePool<int>.Get();
        Check(!queue.TryPeek(out _) && !queue.TryDequeue(out _));
        Throws<InvalidOperationException>(() => queue.Peek());
        Throws<InvalidOperationException>(() => queue.Dequeue());
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);
        Check(queue.Contains(2) && !queue.Contains(9));
        Check(queue.Peek() == 1 && queue.TryPeek(out int first) && first == 1);
        int[] copy = new int[5];
        queue.CopyTo(copy, 1);
        Check(copy.SequenceEqual(new[] { 0, 1, 2, 3, 0 }));
        Check(queue.ToArray().SequenceEqual(new[] { 1, 2, 3 }));
        ICollection collection = queue;
        Check(!collection.IsSynchronized && collection.Count == 3);
        Check(collection.SyncRoot is not Queue<int>);
        Check(ReferenceEquals(collection.SyncRoot, ((ICollection)queue).SyncRoot));
        object[] objects = new object[3];
        collection.CopyTo(objects, 0);
        Check(objects.Cast<int>().SequenceEqual(new[] { 1, 2, 3 }));
        Throws<ArgumentNullException>(() => queue.CopyTo(null!, 0));
        Throws<ArgumentOutOfRangeException>(() => queue.CopyTo(copy, -1));
        Throws<ArgumentException>(() => queue.CopyTo(Array.Empty<int>(), 0));
        Throws<ArgumentException>(() => collection.CopyTo(new int[2, 2], 0));
        Check(((IReadOnlyCollection<int>)queue).Count == 3);
        object boxedQueue = queue;
        Check(boxedQueue is IEnumerable<int> && boxedQueue is IEnumerable && boxedQueue is ICollection);
        Check((object)queue is not ICollection<int> && (object)queue is not Queue<int>);
        Check(queue.Dequeue() == 1);
        Check(queue.TryDequeue(out int value) && value == 2);
        Check(queue.Dequeue() == 3 && queue.Count == 0);
        for (int i = 0; i < 64; i++) queue.Enqueue(i);
        for (int i = 0; i < 32; i++) Check(queue.Dequeue() == i);
        for (int i = 64; i < 96; i++) queue.Enqueue(i);
        Check(queue.ToArray().SequenceEqual(Enumerable.Range(32, 64)));
        queue.TrimExcess();
        queue.Clear();
        Check(queue.Count == 0);
    }

    private static void Enumeration()
    {
        var queue = QueuePool<int>.Get();
        queue.Enqueue(1);
        queue.Enqueue(2);
        var enumerator = queue.GetEnumerator();
        var runtimeEnumerator = new Queue<int>(new[] { 1, 2 }).GetEnumerator();
        Check(CurrentOutcome(() => enumerator.Current) == CurrentOutcome(() => runtimeEnumerator.Current));
        Check(CurrentOutcome(() => ((IEnumerator)enumerator).Current) == CurrentOutcome(() => ((IEnumerator)runtimeEnumerator).Current));
        Check(enumerator.MoveNext() && enumerator.Current == 1);
        enumerator.Reset();
        Check(enumerator.MoveNext() && enumerator.Current == 1);
        Check(enumerator.MoveNext() && enumerator.Current == 2);
        Check(!enumerator.MoveNext());
        while (runtimeEnumerator.MoveNext())
        {
        }

        Check(CurrentOutcome(() => enumerator.Current) == CurrentOutcome(() => runtimeEnumerator.Current));
        Check(CurrentOutcome(() => ((IEnumerator)enumerator).Current) == CurrentOutcome(() => ((IEnumerator)runtimeEnumerator).Current));
        enumerator.Dispose();
        Check(queue.Count == 2); // Enumerator disposal does not return the lease.
        long sum = 0;
        foreach (int item in queue) sum += item;
        Check(sum == 3);
        Check(((IEnumerable<int>)queue).SequenceEqual(new[] { 1, 2 }));
        sum = 0;
        foreach (int item in (IEnumerable)queue) sum += item;
        Check(sum == 3);
        var changed = queue.GetEnumerator();
        queue.Enqueue(3);
        Throws<InvalidOperationException>(() => changed.MoveNext());
        Throws<InvalidOperationException>(() => changed.Reset());
        var cleared = queue.GetEnumerator();
        queue.Clear();
        Throws<InvalidOperationException>(() => cleared.MoveNext());
        queue.Enqueue(4);
        var stale = queue.GetEnumerator();
        Check(stale.MoveNext());
        IEnumerator<int> boxed = ((IEnumerable<int>)queue).GetEnumerator();
        Check(boxed.MoveNext());
        queue.Dispose();
        InvalidEnumerator(stale);
        Throws<ObjectDisposedException>(() => boxed.MoveNext());
        Throws<ObjectDisposedException>(() => _ = boxed.Current);
        Throws<ObjectDisposedException>(() => ((IEnumerator)boxed).Reset());
        using var next = QueuePool<int>.Get();
        next.Enqueue(9);
        InvalidEnumerator(stale);
        Throws<ObjectDisposedException>(() => boxed.MoveNext());
        Throws<ObjectDisposedException>(() => _ = boxed.Current);
        Throws<ObjectDisposedException>(() => ((IEnumerator)boxed).Reset());
        boxed.Dispose();
        Check(next.Peek() == 9);
    }

    private static string CurrentOutcome(Func<object?> getCurrent)
    {
        try
        {
            return "value:" + getCurrent();
        }
        catch (InvalidOperationException)
        {
            return "InvalidOperationException";
        }
    }

    private sealed class SourceException : Exception
    {
    }

    private sealed class FailingSource : IEnumerable<object>
    {
        internal WeakReference? Element;
        internal readonly SourceException Error = new();
        public IEnumerator<object> GetEnumerator() => new FailingEnumerator(this);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class FailingEnumerator : IEnumerator<object>
        {
            private int _index;
            private FailingSource _source;

            public FailingEnumerator(FailingSource source)
            {
                _source = source;
            }

            public object Current
            {
                get
                {
                    object item = new();
                    _source.Element = new WeakReference(item);
                    return item;
                }
            }

            public bool MoveNext()
            {
                if (_index++ == 0) return true;
                throw _source.Error;
            }

            public void Reset() => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PutReference()
    {
        using var queue = QueuePool<object>.Get();
        object item = new();
        queue.Enqueue(item);
        return new WeakReference(item);
    }

    private static void ReferenceCleanupAndSources()
    {
        QueuePool<object>.Clear();
        WeakReference reference = PutReference();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Check(!reference.IsAlive, "Returned queue retained an element");
        var source = new FailingSource();
        try
        {
            QueuePool<object>.Get(source);
            throw new Exception("Missing source error");
        }
        catch (SourceException error)
        {
            Check(ReferenceEquals(error, source.Error));
        }

        Check(QueuePool<object>.CountInactive == 1);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Check(source.Element is not null && !source.Element.IsAlive, "Failed source retained an element");
        using var clean = QueuePool<object>.Get();
        Check(clean.Count == 0);
        using var nullSource = QueuePool<int>.Get((IEnumerable<int>?)null);
        Check(nullSource.Count == 0);
        IEnumerable<int> array = new[] { 1, 2, 3 };
        IEnumerable<int> list = new List<int> { 4, 5, 6 };
        using var a = QueuePool<int>.Get(in array);
        using var b = QueuePool<int>.Get(in list);
        using var c = QueuePool<int>.Get(Enumerable.Range(7, 3));
        Check(a.ToArray().SequenceEqual(array));
        Check(b.ToArray().SequenceEqual(list));
        Check(c.ToArray().SequenceEqual(new[] { 7, 8, 9 }));
    }

    private sealed class ReadOnlySource : IReadOnlyCollection<int>
    {
        private readonly int[] _items;
        private readonly int _count;
        private readonly SourceException? _countError;
        internal int Enumerations;

        internal ReadOnlySource(int[] items, int count, SourceException? countError = null)
        {
            _items = items;
            _count = count;
            _countError = countError;
        }

        public int Count => _countError is null ? _count : throw _countError;

        public IEnumerator<int> GetEnumerator()
        {
            Enumerations++;
            return ((IEnumerable<int>)_items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void ReadOnlySources()
    {
        QueuePool<int>.Clear();
        int[] items = Enumerable.Range(0, 256).ToArray();
        var largerHint = new ReadOnlySource(items, 512);
        using (var queue = QueuePool<int>.Get(largerHint))
        {
            Check(queue.ToArray().SequenceEqual(items));
            Check(largerHint.Enumerations == 1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 256; i < 512; i++) queue.Enqueue(i);
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "Read-only Count did not reserve capacity");
        }

        var smallerHint = new ReadOnlySource(items, 1);
        using (var queue = QueuePool<int>.Get(smallerHint))
        {
            Check(queue.ToArray().SequenceEqual(items), "Count hint truncated enumeration");
            Check(smallerHint.Enumerations == 1);
        }

        var error = new SourceException();
        var failingCount = new ReadOnlySource(items, 256, error);
        int inactive = QueuePool<int>.CountInactive;
        try
        {
            QueuePool<int>.Get(failingCount);
            throw new Exception("Missing Count error");
        }
        catch (SourceException caught)
        {
            Check(ReferenceEquals(caught, error));
        }

        Check(failingCount.Enumerations == 0);
        Check(QueuePool<int>.CountInactive == inactive, "Count failure borrowed a queue");
    }

    private sealed class StageSource : IEnumerable<int>
    {
        internal readonly SourceException Error = new();
        private int _stage;

        public StageSource(int stage)
        {
            _stage = stage;
        }

        public IEnumerator<int> GetEnumerator()
        {
            if (_stage == 0) throw Error;
            return new StageEnumerator(this, _stage);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class StageEnumerator : IEnumerator<int>
        {
            private int _index;
            private StageSource _source;
            private int _stage;

            public int Current => _stage == 2 ? throw _source.Error : 1;
            object IEnumerator.Current => Current;

            public StageEnumerator(StageSource source, int stage)
            {
                _source = source;
                _stage = stage;
            }

            public bool MoveNext()
            {
                if (_stage == 1) throw _source.Error;
                return _index++ == 0;
            }

            public void Reset() => throw new NotSupportedException();

            public void Dispose()
            {
                if (_stage == 3) throw _source.Error;
            }
        }
    }

    private static void SourceFailureStages()
    {
        QueuePool<int>.Clear();
        QueuePool<int>.Warmup(1);
        for (int stage = 0; stage < 4; stage++)
        {
            var source = new StageSource(stage);
            try
            {
                QueuePool<int>.Get(source);
                throw new Exception("Missing stage error");
            }
            catch (SourceException error)
            {
                Check(ReferenceEquals(error, source.Error));
            }

            Check(QueuePool<int>.CountInactive == 1);
            using var next = QueuePool<int>.Get();
            Check(next.Count == 0);
        }
    }

    private static void GenerationExhaustion()
    {
        QueuePool<int>.Clear();
        var first = QueuePool<int>.Get();
        object state = ((ICollection)first).SyncRoot;
        first.Dispose();
        // Test seam only: fast-forward the counter; production never uses reflection.
        state.GetType().GetField("Generation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(state, ulong.MaxValue - 1);
        var last = QueuePool<int>.Get();
        last.Enqueue(3);
        last.Dispose();
        Check(QueuePool<int>.CountInactive == 0);
        using var next = QueuePool<int>.Get();
        Check(!ReferenceEquals(state, ((ICollection)next).SyncRoot));
        last.Dispose();
        AllInvalid(last);
        Check(next.Count == 0);
    }

    private static void PoolPolicy()
    {
        QueuePool<int>.Clear();
        QueuePool<int>.MaxInactive = 2;
        QueuePool<int>.Warmup(2, 128);
        Check(QueuePool<int>.CountInactive == 2);
        var a = QueuePool<int>.Get(128);
        var b = QueuePool<int>.Get(128);
        var c = QueuePool<int>.Get();
        Check(QueuePool<int>.CountInactive == 0);
        a.Enqueue(1);
        QueuePool<int>.Clear();
        Check(a.Peek() == 1 && b.Count == 0);
        a.Dispose();
        b.Dispose();
        c.Dispose();
        Check(QueuePool<int>.CountInactive == 2);
        QueuePool<int>.MaxInactive = 1;
        Check(QueuePool<int>.CountInactive == 1);
        QueuePool<int>.MaxInactive = 0;
        Check(QueuePool<int>.CountInactive == 0);
        using (var discarded = QueuePool<int>.Get()) discarded.Enqueue(1);
        Check(QueuePool<int>.CountInactive == 0);
        Throws<ArgumentOutOfRangeException>(() => QueuePool<int>.MaxInactive = -1);
        Throws<ArgumentOutOfRangeException>(() => QueuePool<int>.Get(-1));
        Throws<ArgumentOutOfRangeException>(() => QueuePool<int>.Warmup(1));
        Throws<ArgumentOutOfRangeException>(() => QueuePool<int>.Warmup(-1));
        Throws<ArgumentOutOfRangeException>(() => QueuePool<int>.Warmup(0, -1));
        QueuePool<int>.MaxInactive = 32;
        QueuePool<int>.Warmup(4, 256);
        Check(QueuePool<int>.CountInactive == 4);
        using var active = QueuePool<int>.Get();
        active.Enqueue(3);
        QueuePool<int>.Warmup(4, 512);
        Check(active.Peek() == 3 && QueuePool<int>.CountInactive == 4);
        QueuePool<int>.Clear();
        Check(active.Peek() == 3 && QueuePool<int>.CountInactive == 0);
    }

    private static void CapacityReuse()
    {
        QueuePool<int>.Clear();
        var small = QueuePool<int>.Get(4);
        var medium = QueuePool<int>.Get(128);
        var large = QueuePool<int>.Get(512);
        object smallState = ((ICollection)small).SyncRoot;
        object mediumState = ((ICollection)medium).SyncRoot;
        object largeState = ((ICollection)large).SyncRoot;
        // Put an undersized queue in the fast slot and another before the large queue.
        small.Dispose();
        large.Dispose();
        medium.Dispose();

        long before = GC.GetAllocatedBytesForCurrentThread();
        var roomy = QueuePool<int>.Get(400);
        var middle = QueuePool<int>.Get(100);
        var tiny = QueuePool<int>.Get(4);
        for (int i = 0; i < 400; i++) roomy.Enqueue(i);
        for (int i = 0; i < 100; i++) middle.Enqueue(i);
        for (int i = 0; i < 4; i++) tiny.Enqueue(i);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, "Suitable pooled queues were replaced or grew");
        Check(ReferenceEquals(largeState, ((ICollection)roomy).SyncRoot));
        Check(ReferenceEquals(mediumState, ((ICollection)middle).SyncRoot));
        Check(ReferenceEquals(smallState, ((ICollection)tiny).SyncRoot));
        Check(QueuePool<int>.CountInactive == 0);
        AllInvalid(small);
        AllInvalid(medium);
        AllInvalid(large);
        tiny.Dispose();
        middle.Dispose();
        roomy.Dispose();

        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            using var a = QueuePool<int>.Get(400);
            using var b = QueuePool<int>.Get(100);
            using var c = QueuePool<int>.Get(4);
            a.Enqueue(i);
            b.Enqueue(i);
            c.Enqueue(i);
        }

        Check(GC.GetAllocatedBytesForCurrentThread() == before, "Mixed-capacity reuse allocated");
        Check(QueuePool<int>.CountInactive == 3);
        QueuePool<int>.Clear();
        Check(QueuePool<int>.CountInactive == 0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LinkInactiveStates(out PooledQueue<object> stale)
    {
        QueuePool<object>.Clear();
        var cached = QueuePool<object>.Get(4);
        var tail = QueuePool<object>.Get(128);
        var head = QueuePool<object>.Get(256);
        var reference = new WeakReference(((ICollection)tail).SyncRoot);
        cached.Dispose();
        tail.Dispose();
        head.Dispose();
        stale = head;
        return reference;
    }

    private static void InactiveReferenceCleanup()
    {
        for (int mode = 0; mode < 2; mode++)
        {
            QueuePool<object>.MaxInactive = 32;
            WeakReference reference = LinkInactiveStates(out var stale);
            if (mode == 0)
            {
                QueuePool<object>.Clear();
            }
            else
            {
                QueuePool<object>.MaxInactive = 1;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Check(!reference.IsAlive, "Stale handle retained discarded inactive states");
            GC.KeepAlive(stale);
        }

        QueuePool<object>.Clear();
        QueuePool<object>.MaxInactive = 32;
    }

    private static void CapacityAndAllocations()
    {
        QueuePool<byte>.Clear();
        QueuePool<byte>.Warmup(3, 512);
        long warmStart = GC.GetAllocatedBytesForCurrentThread();
        var warmA = QueuePool<byte>.Get(512);
        var warmB = QueuePool<byte>.Get(512);
        var warmC = QueuePool<byte>.Get(512);
        for (int i = 0; i < 512; i++)
        {
            warmA.Enqueue(1);
            warmB.Enqueue(1);
            warmC.Enqueue(1);
        }

        warmA.Dispose();
        warmB.Dispose();
        warmC.Dispose();
        Check(GC.GetAllocatedBytesForCurrentThread() == warmStart, "Warmup did not prepare all requested arrays");
        QueuePool<long>.Clear();
        // Reuse a previously small queue. Capacity must be guaranteed by the new request.
        QueuePool<long>.Get(4).Dispose();
        using (var queue = QueuePool<long>.Get(4096))
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4096; i++) queue.Enqueue(i);
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "Capacity request grew the array");
            queue.Clear();
            queue.TrimExcess();
        }

        using (var queue = QueuePool<long>.Get(4096))
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4096; i++) queue.Enqueue(i);
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "Trim invalidated capacity guarantee");
        }

        QueuePool<long>.Clear();
        using (var grown = QueuePool<long>.Get(4))
        {
            for (int i = 0; i < 300; i++) grown.Enqueue(i);
            while (grown.TryDequeue(out _))
            {
            }
        }

        long reuseStart = GC.GetAllocatedBytesForCurrentThread();
        using (var grown = QueuePool<long>.Get(300))
        {
            for (int i = 0; i < 300; i++) grown.Enqueue(i);
        }

        Check(GC.GetAllocatedBytesForCurrentThread() == reuseStart, "Growth floor was lost after draining");

        int[] array = Enumerable.Range(0, 256).ToArray();
        List<int> list = new(array);
        IEnumerable<int> arrayInput = array;
        IEnumerable<int> listInput = list;
        QueuePool<int>.Clear();
        QueuePool<int>.Warmup(4, 256);
        Action repeat = () =>
        {
            using var a = QueuePool<int>.Get(in arrayInput);
            using var b = QueuePool<int>.Get(in listInput);
            using var c = QueuePool<int>.Get(256);
            using var d = QueuePool<int>.Get();
            long sum = 0;
            foreach (int item in a) sum += item;
            foreach (int item in b) sum += item;
            var enumerator = a.GetEnumerator();
            enumerator.Reset();
            enumerator.Dispose();
            a.CopyTo(array, 0);
            c.Enqueue(1);
            c.TryPeek(out _);
            c.TryDequeue(out _);
            _sink = sum;
        };
        for (int i = 0; i < 10000; i++) repeat();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) repeat();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Check(bytes == 0, $"Warmed repeat allocated {bytes} bytes");
        Console.WriteLine($"Warmed arrays/List/direct foreach + 4 concurrent leases: {bytes} B / 10000 iterations");
    }

    public static int Main()
    {
        try
        {
            Lifetime();
            Collection();
            Enumeration();
            ReferenceCleanupAndSources();
            ReadOnlySources();
            SourceFailureStages();
            GenerationExhaustion();
            PoolPolicy();
            CapacityReuse();
            InactiveReferenceCleanup();
            CapacityAndAllocations();
            Console.WriteLine($"PASS: {_checks} contract checks; sink={_sink}; {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
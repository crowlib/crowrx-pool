using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CrowRx.Pool.Collections;

// Diagnostic variants only. Production lifetime checks are never disabled.
internal static class QueueOperationDiagnostics
{
    private const int Batch = 16384;
    private const int Repeats = 256;
    private static long _sink;

    private interface IOperations : IDisposable
    {
        void Enqueue(int item);
        int Dequeue();
        int Count { get; }
        void Clear();
    }

    private readonly struct Actual : IOperations
    {
        private readonly PooledQueue<int> _queue;
        internal Actual(PooledQueue<int> queue) => _queue = queue;
        public void Enqueue(int item) => _queue.Enqueue(item);
        public int Dequeue() => _queue.Dequeue();
        public int Count => _queue.Count;
        public void Clear() => _queue.Clear();
        public void Dispose() => _queue.Dispose();
    }

    private readonly struct Raw : IOperations
    {
        private readonly Queue<int> _queue;
        internal Raw(int capacity) => _queue = new Queue<int>(capacity);
        public void Enqueue(int item) => _queue.Enqueue(item);
        public int Dequeue() => _queue.Dequeue();
        public int Count => _queue.Count;
        public void Clear() => _queue.Clear();

        public void Dispose()
        {
        }
    }

    private sealed class State
    {
        internal readonly Queue<int> Queue = new(Batch);
        internal int Floor = Batch;
        internal ulong Generation = 1;
        internal bool Active = true;

        internal static State Validate(State state, ulong generation)
        {
            if (state is null || !state.Active || state.Generation != generation)
                throw new ObjectDisposedException("Diagnostic lease");
            return state;
        }
    }

    private readonly struct GuardOnly : IOperations
    {
        private readonly State _state;
        private readonly ulong _generation;

        internal GuardOnly(State state)
        {
            _state = state;
            _generation = state.Generation;
        }

        private State Valid => State.Validate(_state, _generation);
        public void Enqueue(int item) => Valid.Queue.Enqueue(item);
        public int Dequeue() => Valid.Queue.Dequeue();
        public int Count => Valid.Queue.Count;
        public void Clear() => Valid.Queue.Clear();

        public void Dispose()
        {
        }
    }

    private readonly struct GuardAndMax : IOperations
    {
        private readonly State _state;
        private readonly ulong _generation;

        internal GuardAndMax(State state)
        {
            _state = state;
            _generation = state.Generation;
        }

        private State Valid => State.Validate(_state, _generation);

        public void Enqueue(int item)
        {
            State state = Valid;
            state.Queue.Enqueue(item);
            state.Floor = Math.Max(state.Floor, state.Queue.Count);
        }

        public int Dequeue() => Valid.Queue.Dequeue();
        public int Count => Valid.Queue.Count;
        public void Clear() => Valid.Queue.Clear();

        public void Dispose()
        {
        }
    }

    private readonly struct GuardAndConditional : IOperations
    {
        private readonly State _state;
        private readonly ulong _generation;

        internal GuardAndConditional(State state)
        {
            _state = state;
            _generation = state.Generation;
        }

        private State Valid => State.Validate(_state, _generation);

        public void Enqueue(int item)
        {
            State state = Valid;
            state.Queue.Enqueue(item);
            int count = state.Queue.Count;
            if (count > state.Floor) state.Floor = count;
        }

        public int Dequeue() => Valid.Queue.Dequeue();
        public int Count => Valid.Queue.Count;
        public void Clear() => Valid.Queue.Clear();

        public void Dispose()
        {
        }
    }

    private static void Sample<T>(ref T queue, string operation, out double ns, out long bytes)
        where T : struct, IOperations
    {
        long elapsed = 0;
        long sum = 0;
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        for (int repeat = 0; repeat < Repeats; repeat++)
        {
            queue.Clear();
            if (operation != "Enqueue")
                for (int i = 0; i < Batch; i++)
                    queue.Enqueue(i);
            long start = Stopwatch.GetTimestamp();
            if (operation == "Enqueue")
                for (int i = 0; i < Batch; i++)
                    queue.Enqueue(i);
            else if (operation == "Dequeue fixed count")
                for (int i = 0; i < Batch; i++)
                    sum += queue.Dequeue();
            else if (operation == "Dequeue with Count condition")
                while (queue.Count > 0)
                    sum += queue.Dequeue();
            elapsed += Stopwatch.GetTimestamp() - start;
        }

        bytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
        ns = elapsed * (1e9 / Stopwatch.Frequency) / (Batch * Repeats);
        _sink = sum;
    }

    private static void Run<T>(string name, T queue) where T : struct, IOperations
    {
        foreach (string operation in new[] { "Enqueue", "Dequeue fixed count", "Dequeue with Count condition" })
        {
            Sample(ref queue, operation, out _, out _);
            var samples = new double[7];
            var bytes = new long[7];
            for (int i = 0; i < 7; i++) Sample(ref queue, operation, out samples[i], out bytes[i]);
            double[] sorted = samples.OrderBy(x => x).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(new { name, operation, batch = Batch, repeats = Repeats, medianNsPerItem = sorted[3], minNsPerItem = sorted[0], maxNsPerItem = sorted[6], samples, allocatedBytes = bytes }));
        }

        queue.Dispose();
    }

    internal static void Run()
    {
        // Reserve the full batch before timing; no timed growth, rent/return or input enumerators.
#if LEGACY
        var actual = QueuePool<int>.Get();
        for (int i = 0; i < Batch; i++) actual.Enqueue(i);
        actual.Clear();
        Run("Legacy DLL", new Actual(actual));
#else
        Run("Final DLL", new Actual(QueuePool<int>.Get(Batch)));
        Run("Guard only, no floor tracking (diagnostic)", new GuardOnly(new State()));
        Run("Guard + Math.Max floor (diagnostic)", new GuardAndMax(new State()));
        Run("Guard + conditional floor (diagnostic)", new GuardAndConditional(new State()));
#endif
        Run("Raw Queue, no lease guard", new Raw(Batch));
        Console.WriteLine($"sink={_sink}");
    }
}

#if !LEGACY
// Direct calls to the actual DLL and BCL Queue; setup is outside the operation timer.
internal static class QueueComparisonBenchmarks
{
    private const int Batch = 16384;
    private const int Repeats = 256;
    private const int Samples = 9;
    private const int CycleSize = 256;
    private const int CycleRepeats = 100000;
    private static long _sink;

    private enum Operation
    {
        Enqueue,
        Dequeue,
        CountDequeue,
        TryDequeue
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double Sample(Queue<int> queue, Operation operation, out long bytes)
    {
        long elapsed = 0;
        long sum = 0;
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        for (int repeat = 0; repeat < Repeats; repeat++)
        {
            queue.Clear();
            if (operation != Operation.Enqueue)
            {
                for (int i = 0; i < Batch; i++) queue.Enqueue(i);
            }

            long start = Stopwatch.GetTimestamp();
            switch (operation)
            {
                case Operation.Enqueue:
                    for (int i = 0; i < Batch; i++) queue.Enqueue(i);
                    break;
                case Operation.Dequeue:
                    for (int i = 0; i < Batch; i++) sum += queue.Dequeue();
                    break;
                case Operation.CountDequeue:
                    while (queue.Count > 0) sum += queue.Dequeue();
                    break;
                case Operation.TryDequeue:
                    while (queue.TryDequeue(out int item)) sum += item;
                    break;
            }

            elapsed += Stopwatch.GetTimestamp() - start;
            if (queue.Count != (operation == Operation.Enqueue ? Batch : 0)) throw new Exception("Queue count mismatch");
        }

        bytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
        if (operation != Operation.Enqueue && sum != (long)Batch * (Batch - 1) / 2 * Repeats) throw new Exception("Queue FIFO checksum mismatch");
        _sink = sum + queue.Count;
        return elapsed * (1e9 / Stopwatch.Frequency) / (Batch * Repeats);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double Sample(PooledQueue<int> queue, Operation operation, out long bytes)
    {
        long elapsed = 0;
        long sum = 0;
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        for (int repeat = 0; repeat < Repeats; repeat++)
        {
            queue.Clear();
            if (operation != Operation.Enqueue)
            {
                for (int i = 0; i < Batch; i++) queue.Enqueue(i);
            }

            long start = Stopwatch.GetTimestamp();
            switch (operation)
            {
                case Operation.Enqueue:
                    for (int i = 0; i < Batch; i++) queue.Enqueue(i);
                    break;
                case Operation.Dequeue:
                    for (int i = 0; i < Batch; i++) sum += queue.Dequeue();
                    break;
                case Operation.CountDequeue:
                    while (queue.Count > 0) sum += queue.Dequeue();
                    break;
                case Operation.TryDequeue:
                    while (queue.TryDequeue(out int item)) sum += item;
                    break;
            }

            elapsed += Stopwatch.GetTimestamp() - start;
            if (queue.Count != (operation == Operation.Enqueue ? Batch : 0)) throw new Exception("Pooled count mismatch");
        }

        bytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
        if (operation != Operation.Enqueue && sum != (long)Batch * (Batch - 1) / 2 * Repeats) throw new Exception("Pooled FIFO checksum mismatch");
        _sink = sum + queue.Count;
        return elapsed * (1e9 / Stopwatch.Frequency) / (Batch * Repeats);
    }

    private static void Print(string kind, string name, double[] samples, double[] bytes)
    {
        double[] sorted = samples.OrderBy(value => value).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { kind, name, medianNs = sorted[Samples / 2], minNs = sorted[0], maxNs = sorted[Samples - 1], samples, bytes }));
    }

    private static void CompareOperation(Operation operation)
    {
        var raw = new Queue<int>(Batch);
        using var pooled = QueuePool<int>.Get(Batch);
        for (int warm = 0; warm < 2; warm++)
        {
            Sample(raw, operation, out _);
            Sample(pooled, operation, out _);
        }

        var rawSamples = new double[Samples];
        var pooledSamples = new double[Samples];
        var rawBytes = new double[Samples];
        var pooledBytes = new double[Samples];
        for (int sample = 0; sample < Samples; sample++)
        {
            if (sample % 2 == 0)
            {
                rawSamples[sample] = Sample(raw, operation, out long rb);
                pooledSamples[sample] = Sample(pooled, operation, out long pb);
                rawBytes[sample] = rb;
                pooledBytes[sample] = pb;
            }
            else
            {
                pooledSamples[sample] = Sample(pooled, operation, out long pb);
                rawSamples[sample] = Sample(raw, operation, out long rb);
                rawBytes[sample] = rb;
                pooledBytes[sample] = pb;
            }
        }

        Print("operation", "Queue/" + operation, rawSamples, rawBytes);
        Print("operation", "PooledQueue/" + operation, pooledSamples, pooledBytes);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long NewQueueCycle()
    {
        var queue = new Queue<int>(CycleSize);
        for (int i = 0; i < CycleSize; i++) queue.Enqueue(i);
        long sum = 0;
        for (int i = 0; i < CycleSize; i++) sum += queue.Dequeue();
        return sum;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ReusedQueueCycle(Queue<int> queue)
    {
        for (int i = 0; i < CycleSize; i++) queue.Enqueue(i);
        long sum = 0;
        for (int i = 0; i < CycleSize; i++) sum += queue.Dequeue();
        return sum;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long PooledQueueCycle()
    {
        using var queue = QueuePool<int>.Get(CycleSize);
        for (int i = 0; i < CycleSize; i++) queue.Enqueue(i);
        long sum = 0;
        for (int i = 0; i < CycleSize; i++) sum += queue.Dequeue();
        return sum;
    }

    private static void CompareCycle(string name, Func<long> cycle)
    {
        for (int warm = 0; warm < 10000; warm++) _sink = cycle();
        var samples = new double[Samples];
        var bytes = new double[Samples];
        for (int sample = 0; sample < Samples; sample++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocation = GC.GetAllocatedBytesForCurrentThread();
            long sum = 0;
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < CycleRepeats; i++) sum += cycle();
            long elapsed = Stopwatch.GetTimestamp() - start;
            bytes[sample] = (double)(GC.GetAllocatedBytesForCurrentThread() - allocation) / CycleRepeats;
            samples[sample] = elapsed * (1e9 / Stopwatch.Frequency) / CycleRepeats;
            if (sum != (long)CycleSize * (CycleSize - 1) / 2 * CycleRepeats) throw new Exception("Cycle FIFO checksum mismatch");
            _sink = sum;
        }

        Print("cycle256", name, samples, bytes);
    }

    internal static void Run()
    {
        string assembly = typeof(PooledQueue<int>).Assembly.Location;
        Type state = typeof(PooledQueue<int>).Assembly.GetType("CrowRx.Pool.Collections.QueueLeaseState`1")!;
        var validation = state.GetMethod("Validate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        string hash = BitConverter.ToString(sha256.ComputeHash(System.IO.File.ReadAllBytes(assembly))).Replace("-", "");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "metadata", assembly, hash,
            validateFlags = validation.MethodImplementationFlags.ToString(),
            exceptionHelperPresent = state.GetMethod("CreateInvalidLeaseException", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static) is not null
                                     || state.GetMethod("ThrowInvalidLease", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static) is not null,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            readyToRun = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"),
            batch = Batch, repeats = Repeats, samples = Samples, cycleSize = CycleSize, cycleRepeats = CycleRepeats
        }));
        CompareOperation(Operation.Enqueue);
        CompareOperation(Operation.Dequeue);
        CompareOperation(Operation.CountDequeue);
        CompareOperation(Operation.TryDequeue);
        QueuePool<int>.Clear();
        QueuePool<int>.Warmup(1, CycleSize);
        var reused = new Queue<int>(CycleSize);
        CompareCycle("Queue/new each cycle", NewQueueCycle);
        CompareCycle("Queue/reused", () => ReusedQueueCycle(reused));
        CompareCycle("PooledQueue/warmed", PooledQueueCycle);
        Console.WriteLine("sink=" + _sink);
    }
}
#endif
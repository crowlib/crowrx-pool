using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CrowRx.Pool.Collections;

internal static class Program
{
    private static readonly int[] Data = Enumerable.Range(0, 256).ToArray();
    private static readonly List<int> List = new(Data);
    private static readonly IEnumerable<int> ArraySource = Data;
    private static readonly IEnumerable<int> ListSource = List;
    private static long _sink;

    private static void Empty()
    {
        using var queue = QueuePool<int>.Get();
    }

    private static void EmptyCount()
    {
        using var queue = QueuePool<int>.Get();
        _sink = queue.Count;
    }

    private static void FillDrain()
    {
        using var queue = QueuePool<int>.Get();
        foreach (int item in Data) queue.Enqueue(item);
        long sum = 0;
        while (queue.Count > 0) sum += queue.Dequeue();
        _sink = sum;
    }

    private static void ArrayInput()
    {
        using var queue = QueuePool<int>.Get(in ArraySource);
        long sum = 0;
        while (queue.Count > 0) sum += queue.Dequeue();
        _sink = sum;
    }

    private static void ListInput()
    {
        using var queue = QueuePool<int>.Get(in ListSource);
        long sum = 0;
        while (queue.Count > 0) sum += queue.Dequeue();
        _sink = sum;
    }

    private static void DirectEnumeration()
    {
        using var queue = QueuePool<int>.Get();
        foreach (int item in Data) queue.Enqueue(item);
        long sum = 0;
        foreach (int item in queue) sum += item;
        _sink = sum;
    }

    private static void InterfaceEnumeration()
    {
        using var queue = QueuePool<int>.Get();
        foreach (int item in Data) queue.Enqueue(item);
        long sum = 0;
        foreach (int item in (IEnumerable<int>)queue) sum += item;
        _sink = sum;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IEnumerable<int> EscapeHandle(PooledQueue<int> queue) => queue;

    private static void ForcedInterfaceEnumeration()
    {
        using var queue = QueuePool<int>.Get();
        foreach (int item in Data) queue.Enqueue(item);
        long sum = 0;
        foreach (int item in EscapeHandle(queue)) sum += item;
        _sink = sum;
    }

    private static void ArrayResult()
    {
        using var queue = QueuePool<int>.Get();
        foreach (int item in Data) queue.Enqueue(item);
        _sink = queue.ToArray().Length;
    }

    private static void Measure(string name, Action action, int iterations = 100000)
    {
        for (int i = 0; i < 10000; i++) action();
        var ns = new double[7];
        var bytes = new double[7];
        for (int sample = 0; sample < 7; sample++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocation = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) action();
            long elapsed = Stopwatch.GetTimestamp() - start;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocation;
            ns[sample] = elapsed * (1e9 / Stopwatch.Frequency) / iterations;
            bytes[sample] = (double)allocated / iterations;
        }

        double[] sorted = ns.OrderBy(value => value).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { name, iterations, warmup = 10000, samples = ns, medianNs = sorted[3], minNs = sorted[0], maxNs = sorted[6], allocatedBytes = bytes }));
    }

    private static void InitialAllocations()
    {
        // JIT each path with an unrelated generic T before measuring a first pool use.
        using (var warm = QueuePool<double>.Get()) warm.Enqueue(1);
        long start = GC.GetAllocatedBytesForCurrentThread();
        using (var queue = QueuePool<long>.Get())
        {
        }

        Console.WriteLine($"firstEmptyRentBytes={GC.GetAllocatedBytesForCurrentThread() - start}");
        start = GC.GetAllocatedBytesForCurrentThread();
        using (var queue = QueuePool<long>.Get())
            for (int i = 0; i < 4096; i++)
                queue.Enqueue(i);
        Console.WriteLine($"first4096GrowthBytes={GC.GetAllocatedBytesForCurrentThread() - start}");
        start = GC.GetAllocatedBytesForCurrentThread();
        using (var queue = QueuePool<long>.Get())
            for (int i = 0; i < 4096; i++)
                queue.Enqueue(i);
        Console.WriteLine($"reused4096Bytes={GC.GetAllocatedBytesForCurrentThread() - start}");
#if !LEGACY
        QueuePool<long>.Clear();
        start = GC.GetAllocatedBytesForCurrentThread();
        using (var queue = QueuePool<long>.Get(4096))
            for (int i = 0; i < 4096; i++)
                queue.Enqueue(i);
        Console.WriteLine($"requested4096Bytes={GC.GetAllocatedBytesForCurrentThread() - start}");
        QueuePool<long>.Clear();
        start = GC.GetAllocatedBytesForCurrentThread();
        QueuePool<long>.Warmup(4, 4096);
        Console.WriteLine($"warmup4x4096Bytes={GC.GetAllocatedBytesForCurrentThread() - start}");
#endif
    }

    public static void Main(string[] args)
    {
#if !LEGACY
        if (args.Contains("--comparison"))
        {
            QueueComparisonBenchmarks.Run();
            return;
        }
#endif

        if (args.Contains("--operations"))
        {
            QueueOperationDiagnostics.Run();
            return;
        }

        if (args.Contains("--boxing-only"))
        {
            Measure("fill/interface foreach 256 forced handle escape", ForcedInterfaceEnumeration);
            return;
        }
#if LEGACY
        const string implementation = "legacy-1.0.4-DLL";
#else
        const string implementation = "final-netstandard2.1-DLL";
#endif
        string assemblyPath = typeof(QueuePool<int>).Assembly.Location;
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        string hash = BitConverter.ToString(sha256.ComputeHash(File.ReadAllBytes(assemblyPath))).Replace("-", "");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            implementation, runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, arch = RuntimeInformation.ProcessArchitecture.ToString(), cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            readyToRun = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"), assembly = assemblyPath, hash
        }));
        InitialAllocations();
        Measure("empty Get+Dispose", Empty, 1000000);
        Measure("empty Get+Count+Dispose", EmptyCount, 1000000);
        Measure("fill/drain 256", FillDrain);
        Measure("IEnumerable array input/drain 256", ArrayInput);
        Measure("IEnumerable List input/drain 256", ListInput);
        Measure("fill/direct foreach 256", DirectEnumeration);
        Measure("fill/interface foreach 256 incl handle conversion", InterfaceEnumeration);
        Measure("fill/interface foreach 256 forced handle escape", ForcedInterfaceEnumeration);
        Measure("fill/ToArray 256", ArrayResult);
        using var lease = QueuePool<int>.Get(in ArraySource);
        IEnumerable<int> preconverted = lease;
        Measure("interface foreach 256 preconverted handle", () =>
        {
            long sum = 0;
            foreach (int item in preconverted) sum += item;
            _sink = sum;
        });
        Console.WriteLine($"sink={_sink}");
    }
}
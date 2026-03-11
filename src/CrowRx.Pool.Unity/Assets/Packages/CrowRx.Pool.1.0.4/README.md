# CrowRx.Pool

A high-performance pooling library for CrowRx, designed for Unity.

## Features

- **Generic Collection Pooling**: Supports `List<T>`, `Dictionary<TKey, T>`, `HashSet<T>`, and `Queue<T>`.
- **StringBuilder Pooling**: Efficient `StringBuilder` reuse to minimize GC allocations.
- **Disposable Pattern**: Simple resource management using `using` blocks for automatic pool return.
- **Unity Optimized**: Specifically designed for Unity 6.0+ environments.

## Usage

### Pooled Collections

All pooled collections inherit from their `System.Collections.Generic` counterparts and implement `IDisposable`.

```csharp
using CrowRx.Pool.Collections;

// Using PooledList
using (var list = ListPool<int>.Get())
{
    list.Add(1);
    list.Add(2);
    // Use the list...
} // Automatically cleared and returned to pool here

// Using PooledDictionary
using (var dict = DictionaryPool<string, int>.Get())
{
    dict["key"] = 100;
}

// Using PooledHashSet
using (var set = HashSetPool<float>.Get())
{
    set.Add(1.5f);
}

// Using PooledQueue
using (var queue = QueuePool<string>.Get())
{
    queue.Enqueue("item");
}
```

### StringBuilder Pool

Reduces memory pressure when performing frequent string operations.

```csharp
using CrowRx.Pool.Text;

using (var pooled = StringBuilderPool.Get())
{
    var sb = pooled.StringBuilder;
    sb.Append("Hello ");
    sb.Append("World!");

    string result = sb.ToString();
    UnityEngine.Debug.Log(result);
} // StringBuilder is cleared and returned to pool
```

## Requirements
- Unity 6.0 or newer
- .NET Standard 2.1 compatible editor

## Installation
Install via NuGet (NuGetForUnity supported).

## Notes
This package is intended for use in Unity Editor only.
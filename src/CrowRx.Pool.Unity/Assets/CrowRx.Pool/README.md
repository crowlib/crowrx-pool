# CrowRx.Pool.Unity

A high-performance object pooling library expansion for CrowRx, specifically designed for Unity.

## Features

- **Unity Object Pooling**: Optimized for `GameObject` and `Component` (`MonoBehaviour`).
- **Automatic Lifecycle Management**: Automatically handles `SetActive(true/false)` and `Instantiate/Destroy` cycles when objects are retrieved or returned.
- **Disposable Pattern**: Simple and safe resource management using `using` blocks for automatic pool return.
- **R3 Integration**: Supports reactive pool management utilizing R3's `ReactiveProperty`.
- **Multi-Source Support**: Capability to manage multiple prefabs within a single pool instance.

## Usage

### Basic Usage (UnityObjectPool)

Standard pooling for `GameObject` or `Component`.

```csharp
using CrowRx.Pool;
using UnityEngine;

public class BulletManager : MonoBehaviour
{
    [SerializeField] private Bullet _bulletPrefab;
    private UnityObjectPool<Bullet> _pool;

    void Start()
    {
        // Create a pool with a prefab source (Max size: 100, Preload: 10)
        _pool = new UnityObjectPool<Bullet>(_bulletPrefab, 100, 10);
    }

    public void Fire(Vector3 position)
    {
        // Using 'using' block ensures the object returns to the pool automatically at the end of the scope
        using (var pooled = _pool.Get())
        {
            var bullet = pooled.Value;
            bullet.transform.position = position;
            bullet.gameObject.SetActive(true);
            
            // Execute logic...
        } // bullet.gameObject.SetActive(false) and return to pool happens here
    }
}
```

### Source Separation (NativeObjectPool)

Useful for custom lifecycle management or when mixing pure C# objects with Unity objects.

```csharp
var source = new UnityObjectPoolSource<MyComponent>(prefab);
var pool = new NativeObjectPool<MyComponent>(source);

using (var pooled = pool.Get())
{
    // Custom logic...
}
```

## Requirements
- Unity 6.0 or newer
- [.NET Standard 2.1](https://docs.microsoft.com/en-us/dotnet/standard/net-standard) compatible environment
- [R3](https://github.com/Cysharp/R3) library (included as dependency)

## Installation

### Prerequisites
Before installing the Unity package, you must install the core **CrowRx.Pool** package via [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity):
1. In Unity, go to **NuGet** > **Manage NuGet Packages**.
2. Search for `CrowRx.Pool` and click **Install**.

### Via Unity Package Manager (UPM)
Once the core package is installed:
1. Open the **Package Manager** in Unity (`Window` > `Package Manager`).
2. Click the **+** button in the top-left corner and select **Add package from git URL...**.
3. Enter the following URL:
   ```text
   https://github.com/crowlib/crowrx-pool.git?path=src/CrowRx.Pool.Unity/Assets/CrowRx.Pool
   ```
4. Click **Add**.

### Manual Installation
Alternatively, you can copy the `src/CrowRx.Pool.Unity/Assets/CrowRx.Pool` folder into your project's `Packages` or `Assets` directory.

## Notes
This package is specifically designed for Unity runtime environments to minimize GC pressure caused by frequent object instantiation and destruction.

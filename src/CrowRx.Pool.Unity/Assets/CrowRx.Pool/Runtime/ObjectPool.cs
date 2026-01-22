// ReSharper disable InconsistentNaming

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;


namespace CrowRx.Pool
{
    using Utility;


    public class ObjectPool<TSource, TInstance> : IObjectPool<TInstance>
        where TInstance : class
    {
        protected const int DefaultMaxPoolSize = 4;


        private static readonly string _typeName = typeof(ObjectPool<TSource, TInstance>).Name;


        protected static void ExceptionLog(Exception e) => Log.Error($"[{_typeName}][Exception] : {e}");


        protected readonly IPoolSource<TSource, TInstance> _poolSource;
        protected readonly Stack<TInstance> _pool;


        public IReadOnlyCollection<TInstance> Pool => _pool;
        public int PooledObjectCount => _pool.Count;


        public ObjectPool(IPoolSource<TSource, TInstance> poolSource) : this(poolSource, DefaultMaxPoolSize, 0)
        {
        }

        public ObjectPool(IPoolSource<TSource, TInstance> poolSource, int threshold) : this(poolSource, threshold, 0)
        {
        }

        public ObjectPool(IPoolSource<TSource, TInstance> poolSource, int threshold, int preloadCount)
        {
            _poolSource = poolSource;
            _pool = new Stack<TInstance>(Math.Max(preloadCount, threshold));

            Preload(preloadCount, threshold);
        }


        public bool Contains(TInstance obj)
        {
            foreach (TInstance target in _pool)
            {
                if (ReferenceEquals(target, obj))
                {
                    return true;
                }
            }

            return false;
        }

        public void ClearPool()
        {
            foreach (TInstance target in _pool)
            {
                _poolSource.ReleaseInstance(target);
            }

            _pool.Clear();

            _poolSource.ReleaseSource();
        }

        public void Restore(TInstance usedObject)
        {
            if (!_poolSource.OnBeforeRestoreToPool(usedObject))
            {
                return;
            }

            _pool.Push(usedObject);

            if (usedObject is IPooledTarget pooledTarget)
            {
                pooledTarget.IsRestored = true;
            }
        }

        public IPooledObject<TInstance> Get(Action<TInstance> onLoad, Action<TInstance> onBeforeGet, Action<IPooledObject<TInstance>> onBeforeGetPool)
        {
            try
            {
                if (!_pool.TryPop(out TInstance instance))
                {
                    instance = CreateInstance();

                    if (instance != null)
                    {
                        onLoad?.Invoke(instance);
                    }
                }

                if (instance is IPooledTarget pooledTarget)
                {
                    pooledTarget.IsRestored = false;
                }

                onBeforeGet?.Invoke(instance);

                IPooledObject<TInstance> pooled = _poolSource.CreatePooledObject(this, instance);

                onBeforeGetPool?.Invoke(pooled);

                return pooled;
            }
            catch (Exception e)
            {
                ExceptionLog(e);

                return null;
            }
        }

        public IPooledObject<TInstance> Get(Action<TInstance> onLoad, Action<IPooledObject<TInstance>> onBeforeGetPool) => Get(onLoad, null, onBeforeGetPool);
        public IPooledObject<TInstance> Get(Action<TInstance> onLoad, Action<TInstance> onBeforeGet) => Get(onLoad, onBeforeGet, null);
        public IPooledObject<TInstance> Get(Action<TInstance> onBeforeGet) => Get(null, onBeforeGet);
        public IPooledObject<TInstance> Get() => Get(null);

        public TInstance GetReferenceInPool(Action<TInstance> onBeforeGet) => Get(onBeforeGet)?.Target;

        public TInstance GetReferenceInPool() => GetReferenceInPool(null);

        public void Preload(int preloadCount, int threshold)
        {
            int requireCount = preloadCount - PooledObjectCount;
            if (requireCount <= 0)
            {
                return;
            }

            int createCount = Math.Min(requireCount, threshold);

            try
            {
                for (int i = 0; i < createCount; ++i)
                {
                    TInstance instance = CreateInstance();

                    if (instance != null)
                    {
                        Restore(instance);
                    }
                }
            }
            catch (Exception e)
            {
                ExceptionLog(e);
            }
        }

        public async UniTask PreloadAsync(int preloadCount, int threshold, CancellationToken cancellationToken) => await PreloadCore(preloadCount, threshold, ExceptionLog, cancellationToken);

        protected TInstance CreateInstance() => _poolSource.CreateInstance();

        protected async UniTask PreloadCore(int preloadCount, int threshold, Action<Exception> onError, CancellationToken cancellationToken)
        {
            while (PooledObjectCount < preloadCount && !cancellationToken.IsCancellationRequested)
            {
                int requireCount = preloadCount - PooledObjectCount;
                if (requireCount <= 0)
                {
                    break;
                }

                for (int i = 0, createCount = Math.Min(requireCount, threshold); i < createCount; i++)
                {
                    try
                    {
                        TInstance instance = CreateInstance();
                        if (instance != null)
                        {
                            Restore(instance);
                        }
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke(ex);

                        return;
                    }
                }

                await UniTask.Yield(); // next frame.
            }
        }
    }
}
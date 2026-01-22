using System;
using System.Collections.Generic;
using R3;


namespace CrowRx.Pool
{
    public class ObjectPoolMultiSource<TKey, TSource, TInstance>
        where TInstance : class
    {
        protected readonly Dictionary<TKey, ReactiveProperty<ObjectPool<TSource, TInstance>>> _pool = new();


        public IDictionary<TKey, ReactiveProperty<ObjectPool<TSource, TInstance>>> Pool => _pool;
        public int SourceCount => _pool?.Count ?? 0;

        public IReadOnlyCollection<TKey> Keys => _pool?.Keys ?? null;
        public IReadOnlyCollection<ReactiveProperty<ObjectPool<TSource, TInstance>>> Pools => _pool?.Values ?? null;


        public bool IsContainsSource(TKey key) => _pool.ContainsKey(key);

        public ReactiveProperty<ObjectPool<TSource, TInstance>> AddToInstantiateSource(TKey key, IPoolSource<TSource, TInstance> source) => AddToInstantiateSource(key, source, 0);

        public ReactiveProperty<ObjectPool<TSource, TInstance>> AddToInstantiateSource(TKey key, IPoolSource<TSource, TInstance> source, int preloadCount)
        {
            if (_pool.TryGetValue(key, out ReactiveProperty<ObjectPool<TSource, TInstance>> pool))
            {
                return pool;
            }

            pool = new ReactiveProperty<ObjectPool<TSource, TInstance>>(new ObjectPool<TSource, TInstance>(source, preloadCount, preloadCount));

            _pool.Add(key, pool);

            return pool;
        }

        public ReactiveProperty<ObjectPool<TSource, TInstance>> Preload(TKey key, IPoolSource<TSource, TInstance> source, int preloadCount)
        {
            ReactiveProperty<ObjectPool<TSource, TInstance>> pool = AddToInstantiateSource(key, source, preloadCount);

            pool.Value.Preload(preloadCount, preloadCount);

            return pool;
        }

        public ReactiveProperty<ObjectPool<TSource, TInstance>> Preload(TKey key, int preloadCount)
        {
            if (!_pool.TryGetValue(key, out ReactiveProperty<ObjectPool<TSource, TInstance>> pool))
            {
                return null;
            }

            pool.Value.Preload(preloadCount, preloadCount);

            return pool;
        }

        public void RemoveFromInstantiateSource(TKey key)
        {
            if (!IsContainsSource(key))
            {
                return;
            }

            _pool[key].Value = null;

            _pool.Remove(key);
        }

        public ReactiveProperty<ObjectPool<TSource, TInstance>> GetReactiveObjectPool(TKey key) => _pool.GetValueOrDefault(key);

        public IPooledObject<TInstance> GetPooledObject(TKey key, Action<TInstance> onLoad, Action<TInstance> onBeforeGet, Action<IPooledObject<TInstance>> onBeforeGetPool) => IsContainsSource(key) ? _pool[key].Value.Get(onLoad, onBeforeGet, onBeforeGetPool) : null;
        public IPooledObject<TInstance> GetPooledObject(TKey key, Action<TInstance> onLoad, Action<IPooledObject<TInstance>> onBeforeGetPool) => GetPooledObject(key, onLoad, null, onBeforeGetPool);
        public IPooledObject<TInstance> GetPooledObject(TKey key, Action<TInstance> onLoad, Action<TInstance> onBeforeGet) => GetPooledObject(key, onLoad, onBeforeGet, null);
        public IPooledObject<TInstance> GetPooledObject(TKey key, Action<TInstance> onBeforeGet) => GetPooledObject(key, null, onBeforeGet);
        public IPooledObject<TInstance> GetPooledObject(TKey key) => GetPooledObject(key, null, onBeforeGet: null);

        public TInstance GetReferenceInPool(TKey key, Action<TInstance> onBeforeGet) => GetPooledObject(key, onBeforeGet)?.Target;
        public TInstance GetReferenceInPool(TKey key) => GetReferenceInPool(key, null);

        public void ClearAllPool()
        {
            foreach (ReactiveProperty<ObjectPool<TSource, TInstance>> pool in _pool.Values)
            {
                pool.Value.ClearPool();
            }

            _pool.Clear();
        }
    }
}
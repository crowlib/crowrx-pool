using R3;


namespace CrowRx.Pool
{
    public class NativeObjectPoolMultiSource<TKey, T> : ObjectPoolMultiSource<TKey, T, T>
        where T : class
    {
        public ReactiveProperty<ObjectPool<T, T>> AddToInstantiateSource(TKey key, T source) => AddToInstantiateSource(key, source, 0);

        public ReactiveProperty<ObjectPool<T, T>> AddToInstantiateSource(TKey key, T source, int preloadCount)
        {
            if (_pool.TryGetValue(key, out ReactiveProperty<ObjectPool<T, T>> pool))
            {
                return pool;
            }

            pool =
                new ReactiveProperty<ObjectPool<T, T>>(
                    new ObjectPool<T, T>(
                        new NativeObjectPoolSource<T>(source),
                        preloadCount,
                        preloadCount));

            _pool.Add(key, pool);

            return pool;
        }

        public ReactiveProperty<ObjectPool<T, T>> Preload(TKey key, T source, int preloadCount)
        {
            ReactiveProperty<ObjectPool<T, T>> pool = AddToInstantiateSource(key, source, preloadCount);

            pool.Value.Preload(preloadCount, preloadCount);

            return pool;
        }
    }
}
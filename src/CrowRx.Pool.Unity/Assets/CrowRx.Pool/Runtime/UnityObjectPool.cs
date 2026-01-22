namespace CrowRx.Pool
{
    public class UnityObjectPool<T> : ObjectPool<T, T>
        where T : UnityEngine.Object
    {
        public UnityObjectPool(T source) : this(source, DefaultMaxPoolSize)
        {
        }

        public UnityObjectPool(T source, int threshold) : this(source, threshold, 0)
        {
        }

        public UnityObjectPool(T source, int threshold, int preloadCount)
            : base(new UnityObjectPoolSource<T>(source), threshold, preloadCount)
        {
        }
    }
}
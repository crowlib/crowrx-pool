namespace CrowRx.Pool
{
    public class NativeObjectPool<T> : ObjectPool<T, T>
        where T : class
    {
        public NativeObjectPool(T source) : this(source, DefaultMaxPoolSize)
        {
        }

        public NativeObjectPool(T source, int threshold) : this(source, threshold, 0)
        {
        }

        public NativeObjectPool(T source, int threshold, int preloadCount)
            : base(new NativeObjectPoolSource<T>(source), threshold, preloadCount)
        {
        }
    }
}
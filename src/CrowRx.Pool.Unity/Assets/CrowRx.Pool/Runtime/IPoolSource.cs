namespace CrowRx.Pool
{
    public interface IPoolSource<out TSourceKey, TInstance>
        where TInstance : class
    {
        TSourceKey Key { get; }

        TInstance CreateInstance();

        IPooledObject<TInstance> CreatePooledObject(IObjectPool<TInstance> pool, TInstance instance);

        bool OnBeforeRestoreToPool(TInstance usedObject);

        void ReleaseInstance(TInstance instance);

        void ReleaseSource();
    }
}
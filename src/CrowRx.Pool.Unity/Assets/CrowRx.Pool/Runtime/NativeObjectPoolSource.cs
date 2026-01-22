using System;


namespace CrowRx.Pool
{
    public class NativeObjectPoolSource<T> : IPoolSource<T, T>
        where T : class
    {
        public T Key { get; private set; }


        public NativeObjectPoolSource(T source)
        {
            Key = source;
        }


        public T CreateInstance() => Key is ICloneable cloneable ? (T)cloneable.Clone() : Activator.CreateInstance<T>();

        public IPooledObject<T> CreatePooledObject(IObjectPool<T> pool, T instance) => new PooledObject<T>(pool, instance);

        public bool OnBeforeRestoreToPool(T usedObject) => true;

        public void ReleaseInstance(T instance)
        {
        }

        public void ReleaseSource()
        {
        }
    }
}
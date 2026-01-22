using System;
using R3;


namespace CrowRx.Pool
{
    public interface IPooledObject : IDisposable
    {
        bool IsRestored { get; }
        
        void RestoreToPool();
    }
    
    public interface IPooledObject<T> : IPooledObject
        where T : class
    {
        Observable<T> ObservableOnRestore { get; }

        T Target { get; }
        IObjectPool<T> Pool { get; }
    }
}
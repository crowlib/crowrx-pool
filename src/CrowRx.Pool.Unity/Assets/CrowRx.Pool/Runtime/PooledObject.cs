using System;
using R3;


namespace CrowRx.Pool
{
    public class PooledObject<T> : IPooledObject<T>, IPooledTarget
        where T : class
    {
        private readonly WeakReference<T> _objectWeakRef;
        private readonly WeakReference<IObjectPool<T>> _objectPoolWeakRef;

        private readonly Subject<T> _subjectOnRestore;

        private bool _isDisposed;
        private IDisposable _disposableOnObjectPool;


        public bool IsRestored { get; set; }
        public Observable<T> ObservableOnRestore => _subjectOnRestore.Publish().RefCount();

        public T Target => _objectWeakRef.TryGetTarget(out T target) ? target : null;
        public IObjectPool<T> Pool => _objectPoolWeakRef.TryGetTarget(out IObjectPool<T> objectPool) ? objectPool : null;


        public PooledObject(IObjectPool<T> objectPool, T pooledObject)
        {
            _objectPoolWeakRef = new WeakReference<IObjectPool<T>>(objectPool);
            _objectWeakRef = new WeakReference<T>(pooledObject);

            _subjectOnRestore = new Subject<T>();
        }

        public PooledObject(ReactiveProperty<IObjectPool<T>> objectPoolReactiveProperty, T pooledObject)
            : this(objectPoolReactiveProperty.Value, pooledObject)
        {
            _disposableOnObjectPool = objectPoolReactiveProperty.Subscribe(OnChangeObjectPool);

            return;

            void OnChangeObjectPool(IObjectPool<T> objectPool) => _objectPoolWeakRef.SetTarget(objectPool);
        }

        public PooledObject(PooledObject<T> rhs)
            : this(rhs.Pool, rhs.Target)
        {
        }


        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            _disposableOnObjectPool?.Dispose();
            _disposableOnObjectPool = null;

            RestoreToPool();
        }

        public void RestoreToPool()
        {
            if (IsRestored)
            {
                return;
            }

            IsRestored = true;

            T target = Target;
            if (target == null)
            {
                return;
            }

            _subjectOnRestore.OnNext(target);

            RestoreToPoolInternal(target);

            _subjectOnRestore.Dispose();
        }

        protected virtual void RestoreToPoolInternal(T target) => Pool?.Restore(target);
    }
}
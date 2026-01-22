using UnityEngine;
using R3;


namespace CrowRx.Pool
{
    public class UnityPooledObject<T> : PooledObject<T>
        where T : Object
    {
        public UnityPooledObject(IObjectPool<T> objectPool, T pooledObject)
            : base(objectPool, pooledObject)
        {
        }

        public UnityPooledObject(ReactiveProperty<IObjectPool<T>> objectPoolReactiveProperty, T pooledObject)
            : base(objectPoolReactiveProperty, pooledObject)
        {
        }

        public UnityPooledObject(PooledObject<T> rhs)
            : base(rhs)
        {
        }


        protected override void RestoreToPoolInternal(T target)
        {
            IObjectPool<T> objectPool = Pool;
            if (objectPool is null)
            {
                if (target is Component component)
                {
                    Object.Destroy(component.gameObject);
                }
                else if (target)
                {
                    Object.Destroy(target);
                }
            }
            else if (target)
            {
                switch (target)
                {
                    case Component component:
                        component.gameObject.SetActive(false);
                        break;

                    case GameObject gameObject:
                        gameObject.SetActive(false);
                        break;
                }

                objectPool.Restore(target);
            }
        }
    }
}
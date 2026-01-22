using System;
using UnityEngine;


namespace CrowRx.Pool
{
    public class UnityObjectPoolSource<T> : IPoolSource<T, T>
        where T : UnityEngine.Object
    {
        public T Key { get; private set; }


        public UnityObjectPoolSource(T source)
        {
            Key = source;
        }


        public T CreateInstance()
        {
            if (!Key)
            {
                throw new Exception("source is null");
            }

            if (UnityEngine.Object.Instantiate(Key) is { } ins)
            {
                return ins;
            }

            throw new Exception($"Fail to Instantiate from {Key.name}");
        }

        public IPooledObject<T> CreatePooledObject(IObjectPool<T> pool, T instance) => new UnityPooledObject<T>(pool, instance);

        public bool OnBeforeRestoreToPool(T usedObject)
        {
            if (!usedObject)
            {
                return false;
            }

            switch (usedObject)
            {
                case Component component when component.gameObject:
                    component.gameObject.SetActive(false);
                    break;

                case GameObject go:
                    go.SetActive(false);
                    break;
            }

            return true;
        }

        public void ReleaseInstance(T instance)
        {
            if (!instance)
            {
                return;
            }

            if (instance is Component component)
            {
                UnityEngine.Object.Destroy(component.gameObject);
            }
            else
            {
                UnityEngine.Object.Destroy(instance);
            }
        }

        public void ReleaseSource()
        {
        }
    }
}
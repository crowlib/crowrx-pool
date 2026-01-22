using System.Threading;
using Cysharp.Threading.Tasks;


namespace CrowRx.Pool
{
    using Tasks;


    public static class IPooledObjectExtension
    {
        public static void RestorePoolAfter<T>(this IPooledObject<T> pooledObject, float lifeTime, CancellationToken cancellationToken = default) where T : class
        {
            if (lifeTime <= 0f || pooledObject is null)
            {
                return;
            }

            UniTask.WaitForSeconds(lifeTime, false, PlayerLoopTiming.Update, cancellationToken)
                .ContinueWithAnyway(() => pooledObject?.RestoreToPool())
                .Forget();
        }
    }
}
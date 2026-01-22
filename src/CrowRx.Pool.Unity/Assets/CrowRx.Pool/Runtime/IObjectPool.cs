namespace CrowRx.Pool
{
    public interface IObjectPool<in T>
        where T : class
    {
        void Restore(T usedObject);
    }
}
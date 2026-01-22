namespace CrowRx.Pool
{
    public interface IPooledTarget
    {
        bool IsRestored { get; set; }
    }
}
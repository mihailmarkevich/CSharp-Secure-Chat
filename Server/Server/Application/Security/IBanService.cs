namespace Server.Application.Security
{
    public interface IBanService
    {
        bool IsBanned(string ip, out TimeSpan? remaining);
        DateTimeOffset Ban(string ip, TimeSpan duration);
    }
}

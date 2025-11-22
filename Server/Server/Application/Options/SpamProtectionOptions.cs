namespace Server.Application.Options
{
    public class SpamProtectionOptions
    {
        /// <summary>
        /// Ban duration in seconds when spam is detected.
        /// </summary>
        public int BanDurationSeconds { get; set; } = 10;

        /// <summary>
        /// Maximum number of active connections allowed per IP.
        /// </summary>
        public int MaxConnectionsPerIp { get; set; } = 20;
    }

}

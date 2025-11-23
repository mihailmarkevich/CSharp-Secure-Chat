namespace Server.Web.Contracts
{
    public sealed class BanNotification
    {
        public string Message { get; set; } = string.Empty;
        public int? RetryAfterSeconds { get; set; }
    }

}

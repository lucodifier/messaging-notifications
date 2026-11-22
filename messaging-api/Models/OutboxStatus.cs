namespace Messaging.Api.Models;

public static class OutboxStatus
{
    public const string Pending = "PENDING";
    public const string Sent = "SENT";
    public const string Failed = "FAILED";
    public const string Dead = "DEAD";

    // Publicada sem resultado do worker: não é republicada
    public const string Unknown = "UNKNOWN";

    public static readonly IReadOnlyList<string> All = new[] { Pending, Sent, Failed, Dead, Unknown };

    public static bool IsValid(string status) =>
        All.Contains(status, StringComparer.OrdinalIgnoreCase);
}

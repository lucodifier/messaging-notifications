using System.Text.Json;

namespace messaging.worker.Models;

public class NotificationMessage
{
    public string? CorrelationId { get; set; }
    public required string System { get; set; }
    public required string SubjectCode { get; set; }
    public required string NotificationType { get; set; }
    public required List<string> Recipients { get; set; }
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

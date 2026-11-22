namespace Messaging.Api.Models;

public class OutboxMessage
{
    public required string Id { get; set; }
    public required string CorrelationId { get; set; }
    public required string System { get; set; }
    public required string SubjectCode { get; set; }
    public required string NotificationType { get; set; }
    public required List<string> Recipients { get; set; }
    public string? Metadata { get; set; }
    public string Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime? LockedUntil { get; set; }
    public bool? IsClient { get; set; }
    public string? Url { get; set; }

    // O retry não reenvia para estes
    public List<string> DeliveredRecipients { get; set; } = new();

    // Sistema normalizado + Idempotency-Key, em colunas separadas
    public string? IdempotencySystem { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

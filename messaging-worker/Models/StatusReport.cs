namespace messaging.worker.Models;

public class StatusReport
{
    public required string CorrelationId { get; set; }
    public required string Status { get; set; }
    public string? Error { get; set; }

    // Em FAILED: quem já recebeu antes da falha
    public List<string>? DeliveredRecipients { get; set; }
    public string? Provider { get; set; }
    public DateTime FinishedAt { get; set; }
}

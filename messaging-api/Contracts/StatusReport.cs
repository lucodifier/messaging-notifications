using System.Text.Json.Serialization;

namespace Messaging.Api.Contracts;

public class StatusReport
{
    [JsonPropertyName("correlationId")]
    public required string CorrelationId { get; set; }

    [JsonPropertyName("status")]
    public required string Status { get; set; }

    // Em FAILED: quem já recebeu antes da falha
    [JsonPropertyName("deliveredRecipients")]
    public List<string>? DeliveredRecipients { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("finishedAt")]
    public DateTime? FinishedAt { get; set; }
}

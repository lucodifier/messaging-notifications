using System.Text.Json.Serialization;

namespace Messaging.Api.Contracts;

public class CreateMessageResponse
{
    [JsonPropertyName("correlationId")]
    public required string CorrelationId { get; set; }

    [JsonPropertyName("status")]
    public required string Status { get; set; }

    [JsonPropertyName("notificationType")]
    public required string NotificationType { get; set; }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Messaging.Api.Models;

namespace Messaging.Api.Contracts;

public class MessageStatusResponse
{
    [JsonPropertyName("correlationId")]
    public required string CorrelationId { get; set; }

    [JsonPropertyName("system")]
    public required string System { get; set; }

    [JsonPropertyName("subjectCode")]
    public required string SubjectCode { get; set; }

    [JsonPropertyName("notificationType")]
    public required string NotificationType { get; set; }

    [JsonPropertyName("recipients")]
    public required List<string> Recipients { get; set; }

    [JsonPropertyName("status")]
    public required string Status { get; set; }

    [JsonPropertyName("attempts")]
    public int Attempts { get; set; }

    [JsonPropertyName("lastError")]
    public string? LastError { get; set; }

    [JsonPropertyName("isClient")]
    public bool? IsClient { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Metadata { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    public static MessageStatusResponse From(OutboxMessage msg, bool includeMetadata)
    {
        JsonElement? metadata = null;
        if (includeMetadata && !string.IsNullOrEmpty(msg.Metadata))
            metadata = JsonSerializer.Deserialize<JsonElement>(msg.Metadata);

        return new MessageStatusResponse
        {
            CorrelationId = msg.CorrelationId,
            System = msg.System,
            SubjectCode = msg.SubjectCode,
            NotificationType = msg.NotificationType,
            Recipients = msg.Recipients,
            Status = msg.Status,
            Attempts = msg.Attempts,
            LastError = msg.LastError,
            IsClient = msg.IsClient,
            Url = msg.Url,
            Metadata = metadata,
            CreatedAt = msg.CreatedAt,
            UpdatedAt = msg.UpdatedAt
        };
    }
}

using System.Text.Json.Serialization;

namespace Messaging.Api.Contracts;

public class MessageListResponse
{
    [JsonPropertyName("items")]
    public required List<MessageStatusResponse> Items { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

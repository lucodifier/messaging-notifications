using System.Text.Json.Serialization;

namespace Messaging.Api.Contracts;
    public class CreateMessageRequest
    {
        [JsonPropertyName("system")]
        public required string System { get; set; }

        [JsonPropertyName("subjectCode")]
        public required string SubjectCode { get; set; }

        [JsonPropertyName("notificationType")]
        public required string NotificationType { get; set; }

        [JsonPropertyName("recipients")]
        public required List<string> Recipients { get; set; }

        [JsonPropertyName("metadata")]
        public required Dictionary<string, object> Metadata { get; set; }
    }

using Messaging.Api.Models;

namespace Messaging.Api.Interfaces;

public interface IOutboxRepository
{
    // false se a Idempotency-Key já existe
    Task<bool> TryInsertAsync(OutboxMessage msg);
    Task<OutboxMessage?> GetByCorrelationIdAsync(string correlationId);
    Task<OutboxMessage?> GetByIdempotencyKeyAsync(string system, string idempotencyKey);
    Task<bool> UpdateStatusAsync(string correlationId, string status, string? lastError = null);
    Task AddDeliveredRecipientsAsync(string correlationId, IEnumerable<string> recipients);
    Task IncrementAttemptsAsync(string correlationId);
    // pendingTimeoutSeconds: PENDING nunca publicada; unknownTimeoutSeconds: PENDING já publicada sem report
    Task<List<OutboxMessage>> ClaimRetryCandidatesAsync(int maxAttempts, int pendingTimeoutSeconds, int unknownTimeoutSeconds, int lockSeconds, int limit);
    Task<int> CountByStatusAsync(string status);
    Task ReleaseLockAsync(string correlationId);
    Task<(List<OutboxMessage> Items, int Total)> ListAsync(
        string? status, string? system, string? notificationType,
        DateTime? from, DateTime? to, int page, int pageSize);
}

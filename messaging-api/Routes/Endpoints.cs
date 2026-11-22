using Messaging.Api.Contracts;
using Messaging.Api.Interfaces;
using Messaging.Api.Models;
using Messaging.Api.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Messaging.Api.Routes;

public static class Endpoints
{
    public static void MapEndpoints(this IEndpointRouteBuilder app)
    {
        // Endpoint de mensagens 🚀
        app.MapPost("/api/message", async (
            [FromBody] CreateMessageRequest request,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            IServiceBusSender busSender,
            IOutboxRepository outbox,
            ILogger<Program> logger) =>
        {
            // Validação 1: Recipients obrigatórios
            if (request.Recipients == null || !request.Recipients.Any())
            {
                logger.LogWarning("❌ Requisição inválida: lista de destinatários vazia.");
                return Results.BadRequest(new { error = "Recipients cannot be empty." });
            }

            // Validação 2: System obrigatório
            if (string.IsNullOrWhiteSpace(request.System))
            {
                logger.LogWarning("❌ Requisição inválida: campo 'System' não informado.");
                return Results.BadRequest(new { error = "System is required." });
            }

            // Validação 3: NotificationType válido
            if (!Enum.TryParse<NotificationType>(request.NotificationType, true, out var notificationType))
            {
                logger.LogWarning("❌ Tipo de notificação inválido recebido: {type}", request.NotificationType);
                return Results.BadRequest(new
                {
                    error = $"Invalid notification type '{request.NotificationType}'. Allowed values: Email, Sms, WhatsApp."
                });
            }

            // Idempotency-Key repetida (por sistema) devolve a mensagem já criada
            string? idempotencySystem = null;
            string? trimmedIdempotencyKey = null;
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                if (idempotencyKey.Length > 200)
                    return Results.BadRequest(new { error = "Idempotency-Key must have at most 200 characters." });

                idempotencySystem = request.System.Trim().ToLowerInvariant();
                trimmedIdempotencyKey = idempotencyKey.Trim();

                var existing = await outbox.GetByIdempotencyKeyAsync(idempotencySystem, trimmedIdempotencyKey);
                if (existing is not null)
                    return AcceptedExisting(existing, logger);
            }

            var correlationId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            var metadataJson = System.Text.Json.JsonSerializer.Serialize(request.Metadata);

            // Outbox: persistir com PENDING antes de publicar
            var outboxMessage = new OutboxMessage
            {
                Id = Guid.NewGuid().ToString(),
                CorrelationId = correlationId,
                System = request.System,
                SubjectCode = request.SubjectCode,
                NotificationType = notificationType.ToString(),
                Recipients = request.Recipients,
                Metadata = metadataJson,
                Status = OutboxStatus.Pending,
                Attempts = 0,
                IsClient = TryGetMetadataBool(request.Metadata, "isClient"),
                Url = TryGetMetadataString(request.Metadata, "url"),
                IdempotencySystem = idempotencySystem,
                IdempotencyKey = trimmedIdempotencyKey,
                CreatedAt = now,
                UpdatedAt = now
            };

            try
            {
                if (!await outbox.TryInsertAsync(outboxMessage))
                {
                    // Corrida com outro POST de mesma chave
                    var existing = await outbox.GetByIdempotencyKeyAsync(idempotencySystem!, trimmedIdempotencyKey!);
                    if (existing is not null)
                        return AcceptedExisting(existing, logger);

                    logger.LogError("❌ Conflito de Idempotency-Key sem mensagem existente: {System}/{Key}", idempotencySystem, trimmedIdempotencyKey);
                    return Results.StatusCode(500);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ Erro ao persistir mensagem no Outbox. Nada foi publicado.");
                return Results.StatusCode(500);
            }

            // Publicar na fila com correlationId
            var envelope = new QueueEnvelope
            {
                CorrelationId = correlationId,
                System = request.System,
                SubjectCode = request.SubjectCode,
                NotificationType = request.NotificationType,
                Recipients = request.Recipients,
                Metadata = request.Metadata
            };

            try
            {
                // Incrementa ANTES de publicar: se o increment falhasse após o publish,
                // a mensagem ficaria PENDING com attempts=0 e seria republicada em duplicidade
                await outbox.IncrementAttemptsAsync(correlationId);
                await busSender.SendMessageAsync(System.Text.Json.JsonSerializer.Serialize(envelope));
                logger.LogInformation("✅ Mensagem {CorrelationId} publicada na fila com sucesso.", correlationId);
            }
            catch (Exception ex)
            {
                // Publicação não ocorreu: marca FAILED para o retry republicar.
                // Se ficasse PENDING com attempts>=1, o retry a trataria como "já publicada" e marcaria UNKNOWN sem reenviar.
                logger.LogWarning(ex, "⚠️ Falha ao publicar {CorrelationId} na fila. Marcando como FAILED para reenvio.", correlationId);
                try
                {
                    await outbox.UpdateStatusAsync(correlationId, OutboxStatus.Failed, $"publish: {ex.Message}");
                }
                catch (Exception updateEx)
                {
                    logger.LogError(updateEx, "❌ Falha ao marcar {CorrelationId} como FAILED após erro de publicação. A mensagem pode acabar como UNKNOWN sem reenvio.", correlationId);
                }
            }

            return Results.Accepted($"/api/message/{correlationId}", new CreateMessageResponse
            {
                CorrelationId = correlationId,
                Status = OutboxStatus.Pending,
                NotificationType = notificationType.ToString()
            });
        })
        .WithName("CreateMessage")
        .WithTags("Messages");

        // Consulta de status por Correlation ID (auditoria) 🔎
        app.MapGet("/api/message/{correlationId}", async (
            string correlationId,
            IOutboxRepository outbox) =>
        {
            var msg = await outbox.GetByCorrelationIdAsync(correlationId);
            return msg is null
                ? Results.NotFound(new { error = "Message not found." })
                : Results.Ok(MessageStatusResponse.From(msg, includeMetadata: true));
        })
        .WithName("GetMessageStatus")
        .WithTags("Messages");

        // Listagem para auditoria com filtros e paginação 🔎
        app.MapGet("/api/messages", async (
            IOutboxRepository outbox,
            string? status,
            string? system,
            string? notificationType,
            DateTime? from,
            DateTime? to,
            int page = 1,
            int pageSize = 50) =>
        {
            if (!string.IsNullOrWhiteSpace(status) && !OutboxStatus.IsValid(status))
            {
                return Results.BadRequest(new
                {
                    error = $"Invalid status '{status}'. Allowed values: {string.Join(", ", OutboxStatus.All)}."
                });
            }

            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var (items, total) = await outbox.ListAsync(status, system, notificationType, from, to, page, pageSize);

            return Results.Ok(new MessageListResponse
            {
                Items = items.Select(m => MessageStatusResponse.From(m, includeMetadata: false)).ToList(),
                Page = page,
                PageSize = pageSize,
                Total = total
            });
        })
        .WithName("ListMessages")
        .WithTags("Messages");

        // Liveness: nao executa check nenhum, so confirma que o processo responde.
        // Separado da readiness para que uma queda do broker nao marque todas as
        // replicas como unhealthy ao mesmo tempo.
        app.MapHealthChecks("/api/health", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";

                var json = JsonConvert.SerializeObject(new
                {
                    status = report.Status.ToString(),
                    message = "🚀 Running boy!!",
                    time = DateTime.UtcNow
                }, new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    Formatting = Formatting.None
                });

                await context.Response.WriteAsync(json);
            }
        })
        .WithName("HealthCheck")
        .WithTags("Health");

        // Readiness: e este que o HEALTHCHECK da imagem e o portao de deploy usam.
        // Sem broker a API nao consegue publicar, entao aceitar requisicao seria
        // perder mensagem.
        app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";

                var json = JsonConvert.SerializeObject(new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
                    time = DateTime.UtcNow
                }, new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    Formatting = Formatting.None
                });

                await context.Response.WriteAsync(json);
            }
        })
        .WithName("HealthCheckReady")
        .WithTags("Health");
    }

    private static IResult AcceptedExisting(OutboxMessage existing, ILogger logger)
    {
        logger.LogInformation("♻️ Idempotency-Key repetida. Devolvendo mensagem existente {CorrelationId}.", existing.CorrelationId);
        return Results.Accepted($"/api/message/{existing.CorrelationId}", new CreateMessageResponse
        {
            CorrelationId = existing.CorrelationId,
            Status = existing.Status,
            NotificationType = existing.NotificationType
        });
    }

    private static bool? TryGetMetadataBool(Dictionary<string, object>? metadata, string key)
    {
        var raw = TryGetMetadataString(metadata, key);
        return bool.TryParse(raw, out var value) ? value : null;
    }

    private static string? TryGetMetadataString(Dictionary<string, object>? metadata, string key)
    {
        if (metadata is null)
            return null;

        var entry = metadata.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
        return entry.Key is null ? null : entry.Value?.ToString();
    }
}

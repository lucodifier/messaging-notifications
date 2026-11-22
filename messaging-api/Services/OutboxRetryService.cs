using System.Text.Json;
using Messaging.Api.Contracts;
using Messaging.Api.Interfaces;
using Messaging.Api.Models;

namespace Messaging.Api.Services;

public class OutboxRetryService : BackgroundService
{
    private readonly ILogger<OutboxRetryService> _logger;
    private readonly IOutboxRepository _outbox;
    private readonly IServiceProvider _serviceProvider;

    private readonly int _retryIntervalSeconds;
    private readonly int _maxAttempts;
    private readonly int _pendingTimeoutSeconds;
    private readonly int _unknownTimeoutSeconds;
    private readonly int _lockSeconds;
    private readonly int _batchSize;

    public OutboxRetryService(
        ILogger<OutboxRetryService> logger,
        IConfiguration configuration,
        IOutboxRepository outbox,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _outbox = outbox;
        _serviceProvider = serviceProvider;

        _retryIntervalSeconds = configuration.GetValue("Outbox:RetryIntervalSeconds", 120);
        _maxAttempts = configuration.GetValue("Outbox:MaxAttempts", 5);
        // PENDING nunca publicada (attempts = 0): tempo até o retry publicar
        _pendingTimeoutSeconds = configuration.GetValue("Outbox:PendingTimeoutSeconds", 600);
        // PENDING já publicada sem report: tempo até virar UNKNOWN. Deve cobrir o pior
        // backlog/parada esperado do worker, senão UNKNOWN mistura "fila atrasada" com "report perdido"
        _unknownTimeoutSeconds = configuration.GetValue("Outbox:UnknownTimeoutSeconds", 3600);
        _lockSeconds = configuration.GetValue("Outbox:LockSeconds", 300);
        _batchSize = configuration.GetValue("Outbox:RetryBatchSize", 100);

        if (_unknownTimeoutSeconds < _pendingTimeoutSeconds)
        {
            _logger.LogWarning(
                "⚠️ Outbox:UnknownTimeoutSeconds ({Unknown}s) menor que Outbox:PendingTimeoutSeconds ({Pending}s). Usando {Pending}s.",
                _unknownTimeoutSeconds, _pendingTimeoutSeconds, _pendingTimeoutSeconds);
            _unknownTimeoutSeconds = _pendingTimeoutSeconds;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "🔁 OutboxRetryService iniciado. Intervalo: {Interval}s, MaxAttempts: {Max}, PendingTimeout: {Timeout}s, UnknownTimeout: {UnknownTimeout}s",
            _retryIntervalSeconds, _maxAttempts, _pendingTimeoutSeconds, _unknownTimeoutSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_retryIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessCycleAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erro no ciclo de retry do Outbox. Próximo ciclo continua normalmente.");
            }
        }
    }

    private async Task ProcessCycleAsync()
    {
        // Claim atômico: seleção + lock em um único UPDATE, sem janela para duplo processamento
        var candidates = await _outbox.ClaimRetryCandidatesAsync(_maxAttempts, _pendingTimeoutSeconds, _unknownTimeoutSeconds, _lockSeconds, _batchSize);
        if (candidates.Count == 0)
            return;

        _logger.LogInformation("🔁 Ciclo de retry: {Count} mensagem(ns) revendicada(s)", candidates.Count);

        var markedUnknown = 0;

        try
        {
            foreach (var msg in candidates)
            {
                if (await ProcessCandidateAsync(msg))
                    markedUnknown++;
            }
        }
        finally
        {
            if (markedUnknown > 0)
                await ReportUnknownAsync(markedUnknown);
        }
    }

    // Nada reprocessa UNKNOWN: este log é o sinal para alerta (filtrar por OutboxUnknownAlert)
    private async Task ReportUnknownAsync(int markedUnknown)
    {
        try
        {
            var totalUnknown = await _outbox.CountByStatusAsync(OutboxStatus.Unknown);
            _logger.LogWarning(
                "🚨 OutboxUnknownAlert: {NewUnknown} mensagem(ns) marcada(s) como UNKNOWN neste ciclo (sem report do worker há mais de {UnknownTimeout}s). Total UNKNOWN: {TotalUnknown}. Verificar GET /api/messages?status=UNKNOWN.",
                markedUnknown, _unknownTimeoutSeconds, totalUnknown);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 OutboxUnknownAlert: {NewUnknown} mensagem(ns) marcada(s) como UNKNOWN neste ciclo. Falha ao contar o total.", markedUnknown);
        }
    }

    // Retorna true se a mensagem foi marcada como UNKNOWN
    private async Task<bool> ProcessCandidateAsync(OutboxMessage msg)
    {
        try
        {
            // Já publicada: republicar pode duplicar o envio
            if (msg.Status == OutboxStatus.Pending && msg.Attempts >= 1)
            {
                var marked = await _outbox.UpdateStatusAsync(msg.CorrelationId, OutboxStatus.Unknown, msg.LastError);
                if (marked)
                    _logger.LogWarning(
                        "❓ Mensagem {CorrelationId} publicada sem resultado do worker há mais de {UnknownTimeout}s (última atualização {UpdatedAt:o}). Marcada como UNKNOWN, sem reenvio.",
                        msg.CorrelationId, _unknownTimeoutSeconds, msg.UpdatedAt);
                return marked;
            }

            if (msg.Attempts >= _maxAttempts)
            {
                await _outbox.UpdateStatusAsync(msg.CorrelationId, OutboxStatus.Dead, msg.LastError);
                _logger.LogWarning("☠️ Mensagem esgotou tentativas e foi marcada como DEAD: {CorrelationId}", msg.CorrelationId);
                return false;
            }

            var pendingRecipients = msg.Recipients
                .Except(msg.DeliveredRecipients, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pendingRecipients.Count == 0)
            {
                await _outbox.UpdateStatusAsync(msg.CorrelationId, OutboxStatus.Sent);
                _logger.LogInformation("✅ Todos os destinatários de {CorrelationId} já receberam. Marcada como SENT.", msg.CorrelationId);
                return false;
            }

            var envelope = new QueueEnvelope
            {
                CorrelationId = msg.CorrelationId,
                System = msg.System,
                SubjectCode = msg.SubjectCode,
                NotificationType = msg.NotificationType,
                Recipients = pendingRecipients,
                Metadata = string.IsNullOrEmpty(msg.Metadata)
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, object>>(msg.Metadata)
            };

            // Incrementa ANTES de publicar (mesma razão do endpoint: evitar republicação sem contagem)
            await _outbox.IncrementAttemptsAsync(msg.CorrelationId);

            var busSender = _serviceProvider.GetRequiredService<IServiceBusSender>();
            await busSender.SendMessageAsync(JsonSerializer.Serialize(envelope));

            await _outbox.UpdateStatusAsync(msg.CorrelationId, OutboxStatus.Pending, msg.LastError);

            _logger.LogInformation("🔁 Reenvio {CorrelationId}, tentativa {Attempts}/{Max}",
                msg.CorrelationId, msg.Attempts + 1, _maxAttempts);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Falha ao reenviar mensagem {CorrelationId}. Mantendo status atual.", msg.CorrelationId);
            return false;
        }
        finally
        {
            await _outbox.ReleaseLockAsync(msg.CorrelationId);
        }
    }
}

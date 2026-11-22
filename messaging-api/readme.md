# Messaging API

API minimalista em **.NET 8** que recebe pedidos de notificação (Email, SMS, WhatsApp), responde na hora com `202 Accepted` e delega o envio a um worker através de uma fila **RabbitMQ**.

Faz par com o [messaging-worker](../messaging-worker), que consome a fila e dispara a mensagem pelo provedor certo.

```text
cliente ──POST /api/message──▶ messaging-api ──▶ [messages-queue] ──▶ messaging-worker ──▶ Email / SMS / WhatsApp
                                    ▲                                        │
                                    └──────── [messages-status-queue] ◀──────┘
                                         (SENT / FAILED por correlationId)
```

## O que tem de interessante

- **Transactional Outbox (SQLite)**: toda mensagem é gravada antes de ir para a fila; nada se perde se o broker cair.
- **Retry em background** com claim atômico, limite de tentativas e estado `DEAD`.
- **Sem envio duplicado**: mensagem já publicada e sem resposta vira `UNKNOWN` em vez de ser reenviada; reenvios parciais só vão para quem ainda não recebeu.
- **Idempotência** por header `Idempotency-Key` (escopo por sistema).
- **Auditoria**: consulta por `correlationId` e listagem paginada com filtros.
- **Health checks** separados: liveness (`/api/health`) e readiness (`/api/health/ready`, que confere a conexão com o RabbitMQ).

## Como rodar

### Docker (recomendado)

```bash
cp .env.example .env
docker compose up -d --build
```

- Swagger: http://localhost:6006/swagger
- RabbitMQ: http://localhost:15672 (usuário `messaging`, senha = `RabbitMQ__pass` do `.env`)

O compose sobe a API e o RabbitMQ. Para ver a mensagem ser entregue, suba também o [messaging-worker](../messaging-worker).

### Local (`dotnet run`)

Precisa do .NET 8 SDK e de um RabbitMQ acessível (`docker compose up -d rabbitmq` resolve).

```bash
cp appsettings.example.json appsettings.json
dotnet run --launch-profile http
```

- Swagger: http://localhost:5122/swagger

## Configuração

Mesmas chaves nos dois formatos: `Secao:Chave` no `appsettings.json` e `Secao__Chave` em variável de ambiente (`.env`).

| Chave | Padrão | Descrição |
|-------|--------|-----------|
| `RabbitMQ:Url` | — | URL AMQP. O trecho `{pass}` é trocado por `RabbitMQ:pass` (com URL-encode) |
| `RabbitMQ:pass` | — | Senha do broker |
| `RabbitMQ:QueueName` | `messages-queue` | Fila consumida pelo worker |
| `RabbitMQ:StatusQueueName` | `messages-status-queue` | Fila em que o worker devolve o resultado |
| `Outbox:DbPath` | `data/outbox.db` | Arquivo SQLite do Outbox |
| `Outbox:RetryIntervalSeconds` | 120 | Intervalo entre ciclos de retry |
| `Outbox:MaxAttempts` | 5 | Tentativas de publicação antes de `DEAD` |
| `Outbox:PendingTimeoutSeconds` | 600 | Espera para publicar uma mensagem que nunca foi publicada |
| `Outbox:UnknownTimeoutSeconds` | 3600 | Espera pelo resultado do worker antes de `UNKNOWN`. Deve ser maior que o pior atraso esperado do worker |
| `Outbox:LockSeconds` | 300 | Lock de uma mensagem durante o retry |
| `Outbox:RetryBatchSize` | 100 | Mensagens por ciclo |
| `Loki:Url` | vazio | Opcional. Se preenchido, os logs também vão para um Grafana Loki |

## Endpoints

| Verbo | Rota | Descrição |
|:-----:|------|-----------|
| POST | `/api/message` | Cria uma notificação. Retorna `202` com o `correlationId` |
| GET | `/api/message/{correlationId}` | Status e detalhes de uma mensagem |
| GET | `/api/messages` | Listagem paginada. Filtros: `status`, `system`, `notificationType`, `from`, `to`, `page`, `pageSize` |
| GET | `/api/health` | Liveness |
| GET | `/api/health/ready` | Readiness (conexão com o RabbitMQ) |

Exemplos prontos para executar em [`messaging.api.http`](messaging.api.http).

### Corpo do POST

```json
{
  "system": "demo-app",
  "subjectCode": "welcome-email",
  "notificationType": "Email",
  "recipients": ["user@example.com"],
  "metadata": {
    "subject": "Bem-vindo!",
    "template": "<h1>Olá, {{name}}!</h1><p>Sua conta foi criada com sucesso.</p>",
    "name": "Maria"
  }
}
```

| Campo | Descrição |
|-------|-----------|
| `system` | Quem está enviando. O worker usa para escolher o provedor de email |
| `subjectCode` | Código livre do assunto, para auditoria |
| `notificationType` | `Email`, `Sms` ou `WhatsApp` |
| `recipients` | Emails ou telefones, conforme o tipo |
| `metadata` | Dados do envio (abaixo) |

| Tipo | `metadata` |
|------|------------|
| Email | `subject`, `template` (HTML/texto com `{{campo}}` ou nome de um template do provedor). Os demais campos viram variáveis do template |
| Sms | `message` |
| WhatsApp | `message` |

Header opcional `Idempotency-Key`: repetir a mesma chave para o mesmo `system` devolve a mensagem já criada em vez de criar outra.

## Status das mensagens

| Status | Significado | O retry faz |
|--------|-------------|-------------|
| `PENDING` (attempts = 0) | Gravada, ainda não publicada | Publica depois de `PendingTimeoutSeconds` |
| `PENDING` (attempts ≥ 1) | Publicada, aguardando o worker | Marca `UNKNOWN` depois de `UnknownTimeoutSeconds`, **sem reenviar** |
| `FAILED` | Falha ao publicar ou falha reportada pelo worker | Republica para quem ainda não recebeu |
| `DEAD` | Esgotou `MaxAttempts` | Nada (terminal) |
| `SENT` | Entregue | Nada (terminal) |
| `UNKNOWN` | Publicada, sem resultado no prazo | Nada. Exige verificação manual |

Um resultado que chega atrasado corrige `UNKNOWN` para `SENT`/`FAILED`.

Logs que merecem alerta: `OutboxUnknownAlert` (mensagens marcadas como `UNKNOWN` no ciclo) e `OutboxLateReport` (resultado tardio — indica `UnknownTimeoutSeconds` curto).

## Estrutura

```text
Routes/      Endpoints da Minimal API
Contracts/   Requests, responses e envelope da fila
Services/    Publicação no RabbitMQ, Outbox, retry e consumo de status
Data/        Criação/migração do SQLite
Health/      Estado do broker e health checks
```

## Stack

.NET 8 Minimal API · RabbitMQ · SQLite · Serilog · Swagger · Docker

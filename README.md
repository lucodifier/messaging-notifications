# Messaging Notifications

🌐 **[Página do projeto](https://lucodifier.github.io/messaging-notifications/)**

Serviço de notificações assíncronas em **.NET 8** com **RabbitMQ**: uma API recebe o pedido e responde na hora; um worker consome a fila e envia por **Email**, **SMS** ou **WhatsApp**, conforme o tipo e os parâmetros da mensagem.

```text
cliente ──POST /api/message──▶ messaging-api ──▶ [messages-queue] ──▶ messaging-worker ──▶ Email / SMS / WhatsApp
                                    ▲                                        │
                                    └──────── [messages-status-queue] ◀──────┘
                                         (SENT / FAILED por correlationId)
```

| Projeto | O que faz |
|---------|-----------|
| [messaging-api](messaging-api) | Minimal API. Valida, grava no Outbox (SQLite), publica na fila e expõe status/auditoria |
| [messaging-worker](messaging-worker) | Worker Service. Consome a fila, escolhe o provedor, envia e devolve o resultado |

## Rodando tudo com Docker

```bash
cd messaging-api
cp .env.example .env
docker compose up -d --build      # API + RabbitMQ

cd ../messaging-worker
cp .env.example .env
docker compose up -d --build      # worker + Mailpit
```

Com os valores de exemplo nada sai para provedores reais: os emails caem no Mailpit e SMS/WhatsApp são simulados no log.

- Swagger: http://localhost:6006/swagger
- Mailpit (emails enviados): http://localhost:8025
- RabbitMQ: http://localhost:15672 (`messaging` / `messaging123`)

## Testando

```bash
curl -X POST http://localhost:6006/api/message \
  -H "Content-Type: application/json" \
  -d '{
    "system": "demo-app",
    "subjectCode": "welcome-email",
    "notificationType": "Email",
    "recipients": ["user@example.com"],
    "metadata": {
      "subject": "Bem-vindo!",
      "template": "<h1>Olá, {{name}}!</h1><p>Sua conta foi criada.</p>",
      "name": "Maria"
    }
  }'
```

A resposta traz um `correlationId`. Consulte o resultado em `GET /api/message/{correlationId}` e veja o email no Mailpit.

Para rodar sem Docker (`dotnet run`) e para os detalhes de configuração, veja o README de cada projeto.

## Destaques técnicos

- Transactional Outbox com retry, limite de tentativas e estado terminal `DEAD`
- Idempotência por `Idempotency-Key`
- Sem envio duplicado: reenvio só para destinatários ainda não entregues; sem resposta vira `UNKNOWN` em vez de reenviar
- Resultado do worker confirmado com publisher confirms
- Health checks de liveness e readiness acompanhando a conexão com o broker
- Provedores de envio plugáveis e desligáveis por configuração

## Stack

.NET 8 · RabbitMQ · SQLite · MailKit · Serilog · xUnit · Docker

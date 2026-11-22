# Messaging Worker

Worker em **.NET 8** que consome uma fila **RabbitMQ** e dispara cada notificação pelo canal indicado na mensagem: **Email**, **SMS** ou **WhatsApp**. Ao terminar, devolve o resultado (`SENT`/`FAILED`) em uma fila de status.

Faz par com a [messaging-api](../messaging-api), que recebe os pedidos via HTTP e publica na fila.

```text
[messages-queue] ──▶ RabbitMqMessageProcessorService
                         │  notificationType
                         ├─ email    ─▶ EmailHandler ─▶ EmailSenderResolver
                         │                                ├─ system em Smtp:UseForSystems ─▶ SmtpEmailSender (SMTP)
                         │                                └─ demais                       ─▶ ElasticEmailSender (API)
                         ├─ sms      ─▶ SmsHandler      ─▶ ComteleSmsSender (API)
                         └─ whatsapp ─▶ WhatsAppHandler ─▶ TwilioWhatsAppSender (API)
                         │
                         └─▶ StatusPublisher ──▶ [messages-status-queue]
```

## O que tem de interessante

- **Roteamento por tipo e por origem**: o `notificationType` escolhe o canal e o `system` escolhe o provedor de email.
- **Resultado confirmado**: o status é publicado com publisher confirms e retry; a API só marca `SENT` com a resposta do worker.
- **Falha parcial sem duplicar**: em caso de erro, informa quem já recebeu para o reenvio pular esses destinatários.
- **Provedores desligáveis**: com `Enabled=false` o envio é só simulado no log — dá para rodar tudo sem conta em provedor nenhum.
- **Health endpoint** próprio (`/health` e `/health/live`) que acompanha a conexão com o broker, inclusive reconexões.

## Como rodar

### Docker (recomendado)

```bash
cp .env.example .env

# com a messaging-api já no ar (o compose dela publica o RabbitMQ na porta 5672)
docker compose up -d --build

# ou sozinho, com um RabbitMQ próprio
docker compose --profile broker up -d --build
```

- Mailpit (caixa de entrada fake): http://localhost:8025
- RabbitMQ: http://localhost:15672 (usuário `messaging`, senha = `RabbitMQ__pass` do `.env`)

Com o `.env` de exemplo nada sai para fora: todo email cai no Mailpit e SMS/WhatsApp são simulados no log.

### Local (`dotnet run`)

Precisa do .NET 8 SDK, de um RabbitMQ e de um SMTP acessíveis (`docker compose --profile broker up -d rabbitmq mailpit`).

```bash
cp appsettings.example.json appsettings.json
dotnet run --project messaging.worker.csproj
```

### Testando sem a API

Publique direto na fila `messages-queue` (pela tela do RabbitMQ ou com `dotnet test`, que publica uma mensagem de exemplo):

```json
{
  "correlationId": "demo-001",
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

## Configuração

Mesmas chaves nos dois formatos: `Secao:Chave` no `appsettings.json` e `Secao__Chave` em variável de ambiente (`.env`).

| Chave | Descrição |
|-------|-----------|
| `RabbitMQ:Url` | URL AMQP. O trecho `{pass}` é trocado por `RabbitMQ:pass` (com URL-encode) |
| `RabbitMQ:pass` | Senha do broker |
| `RabbitMQ:QueueName` | Fila de entrada (padrão `messages-queue`) |
| `RabbitMQ:StatusQueueName` | Fila de resultado (padrão `messages-status-queue`) |
| `HEALTH_PORT` | Porta do health endpoint (padrão `8080`) |
| `Smtp:Host` / `Port` / `User` / `Pass` | Servidor SMTP. Sem usuário e senha, conecta sem autenticar |
| `Smtp:From` / `FromName` | Remetente dos emails via SMTP |
| `Smtp:UseForSystems` | Sistemas que enviam por SMTP, separados por vírgula. `*` = todos |
| `ElasticEmail:Enabled` / `ApiUrl` / `ApiKey` / `FromEmail` / `FromName` | Envio de email pela API do Elastic Email |
| `Comtele:Enabled` / `ApiKey` / `SenderId` | Envio de SMS pela Comtele |
| `Twilio:Enabled` / `AccountSid` / `AuthToken` / `From` | Envio de WhatsApp pela Twilio |

### Como o email escolhe o provedor

O campo `system` da mensagem é comparado com `Smtp:UseForSystems`:

- listado (ou `*`) → `SmtpEmailSender`
- não listado → `ElasticEmailSender`

```json
"Smtp": { "UseForSystems": "demo-app, backoffice" }
```

## O que cada tipo usa do `metadata`

| Tipo | Campos |
|------|--------|
| Email | `subject`, `template` (HTML/texto com `{{campo}}` ou nome de um template do provedor). Os demais campos viram variáveis do template |
| Sms | `message` |
| WhatsApp | `message` |

## Estrutura

```text
Services/           Consumo da fila, publicação de status e senders dos provedores
Services/Handlers/  Um handler por canal
Interfaces/         Contratos dos senders
Models/             Mensagem da fila e report de status
Health/             Estado do broker e endpoint de health
messaging.worker.Tests/  Teste de integração que publica na fila
```

## Provedores

- [Elastic Email](https://elasticemail.com/developers/api-documentation/rest-api) — email
- [Comtele](https://docs.comtele.com.br/) — SMS
- [Twilio](https://www.twilio.com/docs/whatsapp) — WhatsApp
- [Mailpit](https://mailpit.axllent.org/) — SMTP de teste

## Stack

.NET 8 Worker Service · RabbitMQ · MailKit · Serilog · xUnit · Docker

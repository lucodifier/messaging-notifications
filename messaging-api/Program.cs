using Messaging.Api.Data;
using Messaging.Api.Interfaces;
using Messaging.Api.Routes;
using Messaging.Api.Services;
using Serilog;
using Serilog.Sinks.Grafana.Loki;
using Messaging.Api.Health;

var isLocal = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Local";

var configurationBuilder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddEnvironmentVariables();

if (isLocal)
{
    configurationBuilder
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
}

var configuration = configurationBuilder.Build();
var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "development";
var job = configuration["Loki:Application"] ?? "messaging-api";
// Loki é opcional: sem Loki:Url configurado, os logs vão apenas para o console.
var lokiUrl = configuration["Loki:Url"];

var loggerConfiguration = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .Enrich.WithProperty("app", job)
    .Enrich.WithProperty("env", env)
    .Enrich.FromLogContext()
    .WriteTo.Console();

if (!string.IsNullOrWhiteSpace(lokiUrl))
{
    loggerConfiguration.WriteTo.GrafanaLoki(lokiUrl,
        labels: new[]
        {
            new LokiLabel { Key = "job", Value = job },
            new LokiLabel { Key = "env", Value = env }
        },
        queueLimit: 1000);
}

Log.Logger = loggerConfiguration.CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // 🔧 Substitui logger default por Serilog
    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(Log.Logger);

    builder.Services.AddSingleton<ILoggerService, LoggerService>();


    builder.Services.AddSingleton<IServiceBusSender, RabbitMqSender>();

    // Outbox (SQLite) — padrão Transactional Outbox
    var outboxDbPath = builder.Configuration["Outbox:DbPath"] ?? "data/outbox.db";
    OutboxDbInitializer.Initialize(outboxDbPath);
    builder.Services.AddSingleton<IOutboxRepository, OutboxRepository>();
    builder.Services.AddHostedService<StatusConsumerService>();
    builder.Services.AddHostedService<OutboxRetryService>();

    // Swagger
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // Cors
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        });
    });

    // Health Checks
    // Sem check registrado, AddHealthChecks() responde Healthy sempre — o endpoint
    // existia mas nao verificava nada.
    builder.Services.AddSingleton<BrokerHealthState>();
    builder.Services
        .AddHealthChecks()
        .AddCheck<BrokerHealthCheck>("rabbitmq", tags: new[] { "ready" });

    // Abre a conexão com o broker no startup. Sem isto o readiness fica em 503 até
    // a primeira mensagem, e o portão de deploy reverte todo deploy.
    builder.Services.AddHostedService<BrokerConnectionStarter>();

    var app = builder.Build();

    // Middlewares
    app.UseCors("AllowAll");

    app.UseSwagger();
    app.UseSwaggerUI();

    // Endpoints
    app.MapEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "❌ Erro fatal ao iniciar a API");
}
finally
{
    Log.CloseAndFlush();
}

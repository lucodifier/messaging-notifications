using messaging.worker.Health;
using messaging.worker.Interfaces;
using messaging.worker.Services;
using messaging.worker.Services.Handlers;
using Serilog;
using Serilog.Events;



var configurationBuilder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddEnvironmentVariables();

//Só carrega arquivos JSON em ambiente local
//var isLocal = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Local";
//if (isLocal)
//{
//    configurationBuilder
//        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
//        .AddJsonFile($"appsettings.Development.json", optional: true, reloadOnChange: true);
//}

var configuration = configurationBuilder.Build();
var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "dev";
var applicationName = configuration["Application:Name"] ?? "messaging-worker";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Extensions.Http", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.WithProperty("app", applicationName)
    .Enrich.WithProperty("env", env)
    .Enrich.WithProperty("version", "1.0.0")
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();


try
{
    var builder = Host.CreateApplicationBuilder(args);

    // 🔥 Configuração de logging integrada com Serilog
    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog();

    // 🔥 Configurar graceful shutdown direto no Host
    builder.Services.Configure<HostOptions>(opts =>
    {
        opts.ShutdownTimeout = TimeSpan.FromSeconds(30);
    });

    // 🚀 Seus serviços injetados
    builder.Services.AddHttpClient();


    // Health: estado do broker + endpoint em porta interna.
    builder.Services.AddSingleton<BrokerHealthState>();
    builder.Services.AddHostedService<HealthEndpointService>();

    // Worker Services
    builder.Services.AddHostedService<RabbitMqMessageProcessorService>();

    // Status report (padrão Outbox — resultado de envio vai para fila de status)
    builder.Services.AddSingleton<IStatusPublisher, StatusPublisher>();

    // Handlers
    builder.Services.AddScoped<EmailHandler>();
    builder.Services.AddScoped<SmsHandler>();
    builder.Services.AddScoped<WhatsAppHandler>();

    // Senders
    builder.Services.AddSingleton<ElasticEmailSender>();
    builder.Services.AddSingleton<SmtpEmailSender>();
    builder.Services.AddSingleton<IEmailSenderResolver, EmailSenderResolver>();
    builder.Services.AddSingleton<ISmsSender, ComteleSmsSender>();
    builder.Services.AddScoped<IWhatsAppSender, TwilioWhatsAppSender>();

    var app = builder.Build();
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "❌ Erro fatal ao iniciar o messaging-worker");
}
finally
{
    Log.CloseAndFlush();
}

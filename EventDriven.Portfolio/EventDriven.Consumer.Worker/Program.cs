using EventDriven.Consumer.Worker;
using EventDriven.Contracts;
using EventDriven.Publisher.Api.Data;
using EventDriven.Publisher.Api.Entities;
using EventDriven.ServiceDefaults;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics.Metrics;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults(); // 👈 Activa OpenTelemetry, Health Checks y métricas automáticamente

// 1. Crear y registrar un Meter personalizado para la aplicación 📈
var myMeter = new Meter("EventDriven.Worker", "1.0.0");
builder.Services.AddSingleton(myMeter);

// 2. Crear un contador específico a partir del Meter
var processedMessagesCounter = myMeter.CreateCounter<long>(
    name: "total_processed_events",
    unit: "messages",
    description: "Total number of events processed by the consumer");

builder.Services.AddSingleton<WorkerMetrics>();

// Registrar el contador para poder inyectarlo en tus clases
builder.Services.AddSingleton(processedMessagesCounter);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "worker-service"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(TelemetryDiagnostics.Source.Name) // Tu propia fuente
            .AddSource("MassTransit")                    // Escucha nativamente a MassTransit
            .AddConsoleExporter();                       // Muestra en consola
                                                         //.AddOtlpExporter(options =>
                                                         //    {
                                                         //        // Puerto gRPC OTLP por defecto del Aspire Dashboard
                                                         //        options.Endpoint = new Uri("http://localhost:4317");
                                                         //    });
    }).WithMetrics(metrics =>
    {
        metrics
        .AddMeter("EventDriven.Worker"); // 🏷️ Pasa el nombre exacto de tu Meter aquí
        //.AddOtlpExporter(options =>
        //{
        //    // Puerto gRPC OTLP por defecto del Aspire Dashboard
        //    options.Endpoint = new Uri("http://localhost:4317");
        //});
    });

//builder.Services.AddHostedService<Worker_WithOut_MassTransit>();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? builder.Configuration.GetConnectionString("sqlserver");

    options.UseSqlServer(connectionString);
});

// Configurar MassTransit 🚌
builder.Services.AddMassTransit(x =>
{
    // 2. Configurar la integración con Entity Framework (Outbox & Saga Repository) 📝
    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UseSqlServer();
        o.UseBusOutbox();
    });

    // 3. Registrar la Máquina de Estados (Saga)
    x.AddSagaStateMachine<OrderStateMachine, OrderState>()
        .EntityFrameworkRepository(r =>
        {
            r.ExistingDbContext<AppDbContext>();
            r.UseSqlServer();
        });

    // 4. Configurar Middleware global para consumidores/sagas
    x.AddConfigureEndpointsCallback((context, name, cfg) =>
    {
        cfg.UseEntityFrameworkOutbox<AppDbContext>(context);
    });

    // 2. Configurar el transporte con RabbitMQ
    x.UsingRabbitMq((context, cfg) =>
    {
        var connectionString = builder.Configuration.GetConnectionString("messaging");

        if (!string.IsNullOrEmpty(connectionString))
        {
            // Aspire pasa el URI completo (amqp://guest:guest@localhost:puerto_dinamico)
            cfg.Host(new Uri(connectionString));
        }
        else
        {
            cfg.Host("localhost", "/", h =>
            {
                h.Username("guest");
                h.Password("guest");
            });
        }

        cfg.UseMessageRetry(r =>
        {
            // Reintentar solo si es una excepción de base de datos o timeout
            r.Handle<SqlException>();
            r.Handle<TimeoutException>();

            // Ignorar si es un error de validación (irá directo a la cola _error sin reintentar)
            r.Ignore<ArgumentException>();

            r.Exponential(
                retryLimit: 5,
                minInterval: TimeSpan.FromSeconds(2),
                maxInterval: TimeSpan.FromSeconds(30),
                intervalDelta: TimeSpan.FromSeconds(3)
            );
        });

        // Configura automáticamente las colas (endpoints) según los consumidores registrados
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
await host.RunAsync();
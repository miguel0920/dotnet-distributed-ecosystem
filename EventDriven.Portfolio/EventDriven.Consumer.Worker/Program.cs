using EventDriven.Consumer.Worker;
using EventDriven.Contracts;
using MassTransit;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics.Metrics;

var builder = Host.CreateApplicationBuilder(args);

// 1. Crear y registrar un Meter personalizado para la aplicación 📈
var myMeter = new Meter("EventDriven.Worker", "1.0.0");
builder.Services.AddSingleton(myMeter);

// 2. Crear un contador específico a partir del Meter
var processedMessagesCounter = myMeter.CreateCounter<long>(
    name: "total_processed_events",
    unit: "messages",
    description: "Total number of events processed by the consumer");

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
    });

//builder.Services.AddHostedService<Worker_WithOut_MassTransit>();

// Configurar MassTransit 🚌
builder.Services.AddMassTransit(x =>
{
    // 1. Registrar nuestro consumidor
    x.AddConsumer<OrderCreatedConsumer>();

    // 2. Configurar el transporte con RabbitMQ
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        // Configura automáticamente las colas (endpoints) según los consumidores registrados
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
await host.RunAsync();
using EventDriven.Consumer.Worker;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);
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
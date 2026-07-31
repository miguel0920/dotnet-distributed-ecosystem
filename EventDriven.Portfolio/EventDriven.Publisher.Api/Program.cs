using EventDriven.Contracts;
using EventDriven.Publisher.Api.Data;
using EventDriven.Publisher.Api.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
    {
        Indented = true // Formato legible en la terminal
    };
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "api-service"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(TelemetryDiagnostics.Source.Name) // 👈 Escucha nuestra fuente "EventDriven.Telemetry"
            .AddAspNetCoreInstrumentation()              // Captura automáticamente las peticiones HTTP de la API
            .AddSource("MassTransit")                    // Escucha nativamente a MassTransit
            .AddConsoleExporter();                       // Imprime la traza estructurada en la terminal
    });

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddOpenApi(options =>
{
    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        // Aquí puedes enriquecer los esquemas o respuestas de forma segura
        // interactuando con el nuevo modelo de objetos de la versión 3.x
        return Task.CompletedTask;
    });
});

// 1. Configurar MassTransit 🚌
builder.Services.AddMassTransit(x =>
{
    // 1. Habilitar la integración del Outbox con Entity Framework 📝
    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UseSqlServer();
        o.UseBusOutbox(); // Redirige el IPublishEndpoint al Outbox
    });

    // Indicar que usaremos RabbitMQ como nuestro transporte de mensajes
    x.UsingRabbitMq((context, cfg) =>
    {
        // Configurar la conexión al servidor de RabbitMQ
        cfg.Host("localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapPost("/orders", async (ILogger<Program> _logger, AppDbContext dbContext, IPublishEndpoint publishEndpoint) =>
{
    // 🔍 1. Iniciamos el Span raíz de la operación en la API
    using var activity = TelemetryDiagnostics.Source.StartActivity("CreateOrderEndpoint");

    var order = new Order { Id = Guid.NewGuid(), CustomerId = "CUST-1", TotalAmount = 100.00m, CreatedAtUtc = DateTime.UtcNow };

    // 🏷️ 2. Añadimos etiquetas informativas a la traza
    activity?.SetTag("order.id", order.Id);
    activity?.SetTag("order.customer_id", order.CustomerId);

    dbContext.Orders.Add(order);

    var @event = new OrderCreatedEvent(order.Id, order.CustomerId, order.TotalAmount, order.CreatedAtUtc);

    // ⚠️ Importante: Esto YA NO envía el mensaje a RabbitMQ inmediatamente.
    // Lo escribe temporalmente en la tabla de Outbox del DbContext.
    await publishEndpoint.Publish(@event);

    // 🔒 AQUÍ SE GUARDA TODO O NADA EN UNA SOLA TRANSACCIÓN
    await dbContext.SaveChangesAsync();

    _logger.LogInformation("Mensaje procesado correctamente. Orden ID: {OrderId}, Estado: {Status}", order.Id, "Completado");

    // 🟢 3. Marcamos el estado del Span como exitoso
    activity?.SetStatus(ActivityStatusCode.Ok);

    return Results.Ok();
});

// Without MassTransit

//app.MapPost("/orders", async () =>
//{
//    // 1. Crear el objeto con la fábrica de conexiones 🔌
//    var factory = new ConnectionFactory { HostName = "localhost" };
//    using var connection = await factory.CreateConnectionAsync();
//    using var channel = await connection.CreateChannelAsync();

//    // 2. Declarar la cola donde enviaremos el mensaje 📥
//    await channel.QueueDeclareAsync(
//        queue: "orders-queue",
//        durable: true,
//        exclusive: false,
//        autoDelete: false,
//        arguments: null);

//    // 3. Crear el evento e inmutable 📦
//    var @event = new OrderCreatedEvent(
//        OrderId: Guid.NewGuid(),
//        CustomerId: "CUST-1234",
//        TotalAmount: 99.99m,
//        CreatedAtUtc: DateTime.UtcNow
//    );

//    // 4. Serializar a JSON y convertir a Bytes ⚡
//    var json = JsonSerializer.Serialize(@event);
//    var body = Encoding.UTF8.GetBytes(json);

//    // 5. Publicar el mensaje en la cola 📤
//    await channel.BasicPublishAsync(
//        exchange: string.Empty,
//        routingKey: "orders-queue",
//        body: body);

//    return Results.Ok(new { Message = "Evento publicado exitosamente", Event = @event });
//});

await app.RunAsync();
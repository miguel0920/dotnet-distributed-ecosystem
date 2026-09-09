using EventDriven.Contracts;
using EventDriven.Publisher.Api.Data;
using EventDriven.Publisher.Api.Entities;
using EventDriven.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(); // 👈 Activa OpenTelemetry, Health Checks y métricas automáticamente

var apiMeter = new Meter("EventDriven.Publisher.Api", "1.0.0");
builder.Services.AddSingleton(apiMeter);

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

// 💳 Configuración de un HttpClient resiliente para servicios externos
builder.Services.AddHttpClient("PaymentService", client =>
{
    client.BaseAddress = new Uri("https://api.pasareladepagos.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
})
.AddStandardResilienceHandler(options =>
{
    // 1. Personalizar reintentos
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.Delay = TimeSpan.FromSeconds(2);
    options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;

    // 2. Personalizar Cortacircuitos (Circuit Breaker)
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    options.CircuitBreaker.FailureRatio = 0.5; // Abrir circuito si el 50% falla

    // 3. Tiempo límite por intento individual
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
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
                                                         //.AddOtlpExporter(options =>
                                                         //    {
                                                         //        // Puerto gRPC OTLP por defecto del Aspire Dashboard
                                                         //        options.Endpoint = new Uri("http://localhost:4317");
                                                         //    });
    })
    .WithMetrics(metrics =>
    {
        metrics
        .AddMeter("EventDriven.Publisher.Api") // 🏷️ Pasa el nombre exacto de tu Meter aquí
        .AddAspNetCoreInstrumentation(); // Opcional: captura métricas nativas de peticiones HTTP en la API
        //.AddOtlpExporter(options =>
        //{
        //    // Puerto gRPC OTLP por defecto del Aspire Dashboard
        //    options.Endpoint = new Uri("http://localhost:4317");
        //});
    });

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,                   // 🔄 Intentar hasta 5 veces
            maxRetryDelay: TimeSpan.FromSeconds(20), // ⏱️ Tiempo máximo de espera entre reintentos
            errorNumbersToAdd: null)
    ));

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

    // 2. Habilitar la deduplicación en los consumidores
    x.AddConfigureEndpointsCallback((context, name, cfg) =>
    {
        cfg.UseEntityFrameworkOutbox<AppDbContext>(context);
    });

    // Indicar que usaremos RabbitMQ como nuestro transporte de mensajes
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

    // Enviar el evento original
    await publishEndpoint.Publish(@event, context => context.MessageId = order.Id);

    // 🧪 SIMULACIÓN DE DUPLICADO: Publicar inmediatamente el mismo evento con el mismo MessageId
    await publishEndpoint.Publish(@event, context => context.MessageId = order.Id);

    // 🔒 AQUÍ SE GUARDA TODO O NADA EN UNA SOLA TRANSACCIÓN
    await dbContext.SaveChangesAsync();

    _logger.LogInformation("Mensaje procesado correctamente. Orden ID: {OrderId}, Estado: {Status}", order.Id, "Completado");

    // 🟢 3. Marcamos el estado del Span como exitoso
    activity?.SetStatus(ActivityStatusCode.Ok);

    return Results.Ok();
});

app.MapPost("/orders/checkout", async (IHttpClientFactory clientFactory, IPublishEndpoint publishEndpoint, AppDbContext dbContext) =>
{
    var client = clientFactory.CreateClient("PaymentService");

    try
    {
        // 💳 Polly interceptará esta llamada aplicando Retry y Circuit Breaker si falla
        var response = await client.GetAsync("/process-payment");
        response.EnsureSuccessStatusCode();
    }
    catch (Polly.Timeout.TimeoutRejectedException)
    {
        // Captura específica cuando Polly cancela por exceder el tiempo de espera
        return Results.Problem(
            statusCode: StatusCodes.Status504GatewayTimeout,
            detail: "El servicio de pagos tardó demasiado en responder.");
    }
    catch (Polly.CircuitBreaker.BrokenCircuitException)
    {
        // Captura específica si el circuito se abrió por demasiados fallos seguidos
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            detail: "El servicio de pagos está temporalmente fuera de servicio (Circuit Breaker Abierto).");
    }

    // Continuar con la lógica normal de tu orden...
    return Results.Ok(new { Status = "Pago exitoso y orden en camino" });
});

app.MapPost("/saga/orders", async (IPublishEndpoint publishEndpoint) =>
{
    var orderId = Guid.NewGuid();
    var @event = new OrderSubmittedEvent(orderId, "CUST-99", 250.50m, DateTime.UtcNow);

    await publishEndpoint.Publish(@event);

    return Results.Ok(new { OrderId = orderId, Status = "OrderSubmittedEvent publicado" });
});

app.MapPost("/saga/orders/{id:guid}/pay", async (Guid id, bool isSuccessful, IPublishEndpoint publishEndpoint) =>
{
    if (isSuccessful)
    {
        await publishEndpoint.Publish(new PaymentCompletedEvent(id, DateTime.UtcNow));
        return Results.Ok(new { OrderId = id, Status = "PaymentCompletedEvent publicado" });
    }
    else
    {
        await publishEndpoint.Publish(new PaymentFailedEvent(id, "Fondos insuficientes"));
        return Results.Ok(new { OrderId = id, Status = "PaymentFailedEvent publicado" });
    }
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

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}

await app.RunAsync();
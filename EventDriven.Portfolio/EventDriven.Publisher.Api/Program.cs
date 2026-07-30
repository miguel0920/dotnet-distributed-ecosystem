using EventDriven.Contracts;
using EventDriven.Publisher.Api.Data;
using EventDriven.Publisher.Api.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

app.MapPost("/orders", async (AppDbContext dbContext, IPublishEndpoint publishEndpoint) =>
{
    var order = new Order { Id = Guid.NewGuid(), CustomerId = "CUST-1", TotalAmount = 100.00m, CreatedAtUtc = DateTime.UtcNow };
    dbContext.Orders.Add(order);

    var @event = new OrderCreatedEvent(order.Id, order.CustomerId, order.TotalAmount, order.CreatedAtUtc);

    // ⚠️ Importante: Esto YA NO envía el mensaje a RabbitMQ inmediatamente.
    // Lo escribe temporalmente en la tabla de Outbox del DbContext.
    await publishEndpoint.Publish(@event);

    // 🔒 AQUÍ SE GUARDA TODO O NADA EN UNA SOLA TRANSACCIÓN
    await dbContext.SaveChangesAsync();

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
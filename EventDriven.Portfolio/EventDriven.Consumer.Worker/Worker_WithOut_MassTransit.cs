using System.Text;
using System.Text.Json;
using EventDriven.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EventDriven.Consumer.Worker;

public class Worker_WithOut_MassTransit(ILogger<Worker_WithOut_MassTransit> logger) : BackgroundService
{
    private readonly ILogger<Worker_WithOut_MassTransit> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. Conectarse a RabbitMQ 🔌
        var factory = new ConnectionFactory { HostName = "localhost" };
        using var connection = await factory.CreateConnectionAsync(stoppingToken);
        using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // 2. Asegurar que la cola existe 📥
        await channel.QueueDeclareAsync(
            queue: "orders-queue",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Esperando mensajes en la cola 'orders-queue'...");

        // 3. Crear el consumidor asíncrono 🎧
        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (model, ea) =>
        {
            // Paso A: Obtener el arreglo de bytes 📥
            var body = ea.Body.ToArray();

            // Paso B: Convertir bytes a texto JSON 📄
            var json = Encoding.UTF8.GetString(body);

            // Paso C: Deserializar el JSON al objeto de C# 📦
            var @event = JsonSerializer.Deserialize<OrderCreatedEvent>(json);

            if (@event != null)
            {
                // Paso D: Procesar la información ⚙️
                _logger.LogInformation(
                    "✅ [Evento Recibido]: Orden {OrderId} del cliente {CustomerId} por un total de ${Amount}",
                    @event.OrderId,
                    @event.CustomerId,
                    @event.TotalAmount);
            }

            // Confirmar a RabbitMQ que el mensaje fue procesado correctamente 👍
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        };

        // 4. Empezar a escuchar la cola 🚀
        await channel.BasicConsumeAsync(
            queue: "orders-queue",
            autoAck: false, // Control manual de confirmación
            consumer: consumer,
            cancellationToken: stoppingToken);

        // Mantener el Worker ejecutándose
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
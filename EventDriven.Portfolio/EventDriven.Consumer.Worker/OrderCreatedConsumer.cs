using EventDriven.Contracts;
using MassTransit;
using System;
using System.Collections.Generic;
using System.Text;

namespace EventDriven.Consumer.Worker
{
    public class OrderCreatedConsumer(ILogger<OrderCreatedConsumer> logger) : IConsumer<OrderCreatedEvent>
    {
        private readonly ILogger<OrderCreatedConsumer> _logger = logger;

        public Task Consume(ConsumeContext<OrderCreatedEvent> context)
        {
            // El objeto des-serializado ya viene listo dentro de context.Message 📦
            var @event = context.Message;

            _logger.LogInformation(
                "✅ [MassTransit Consumer]: Orden recibida {OrderId} para el cliente {CustomerId} por un monto de ${Amount}",
                @event.OrderId,
                @event.CustomerId,
                @event.TotalAmount);

            // Retornamos una tarea completada.
            // Si no lanzamos ninguna excepción, MassTransit confirma automáticamente el mensaje (BasicAck) a RabbitMQ 👍
            return Task.CompletedTask;
        }
    }
}
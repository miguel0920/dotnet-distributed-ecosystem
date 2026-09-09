using EventDriven.Contracts;
using MassTransit;
using System.Diagnostics;

namespace EventDriven.Consumer.Worker
{
    public class OrderCreatedConsumer(ILogger<OrderCreatedConsumer> _logger, WorkerMetrics metrics) : IConsumer<OrderCreatedEvent>
    {
        public Task Consume(ConsumeContext<OrderCreatedEvent> context)
        {
            // El objeto des-serializado ya viene listo dentro de context.Message 📦
            var @event = context.Message;

            //Console.WriteLine($"[Worker] Recibido evento para la orden: {@event.OrderId}. Simulando error...");
            //throw new TimeoutException("🔥 Error simulado en el procesamiento de la orden.");

            // 🔍 Iniciamos la traza (Span) para medir esta operación específica
            using var activity = TelemetryDiagnostics.Source.StartActivity("ProcessOrderCreatedEvent");

            // 🏷️ Agregamos metadata (Tags) útil para depurar
            activity?.SetTag("order.id", @event.OrderId);
            activity?.SetTag("order.customer_id", @event.CustomerId);
            activity?.SetTag("order.amount", @event.TotalAmount);

            _logger.LogInformation(
                "✅ [MassTransit Consumer]: Orden recibida {OrderId} para el cliente {CustomerId} por un monto de ${Amount}",
                @event.OrderId,
                @event.CustomerId,
                @event.TotalAmount);

            // Retornamos una tarea completada.
            // Si no lanzamos ninguna excepción, MassTransit confirma automáticamente el mensaje (BasicAck) a RabbitMQ 👍

            metrics.IncrementProcessedEvents(nameof(OrderCreatedEvent), "exitoso");

            // 🟢 Marcamos el estado de la traza como OK
            activity?.SetStatus(ActivityStatusCode.Ok);

            return Task.CompletedTask;
        }
    }
}
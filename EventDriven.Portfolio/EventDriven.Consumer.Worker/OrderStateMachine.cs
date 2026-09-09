using EventDriven.Contracts;
using EventDriven.Publisher.Api.Entities;
using MassTransit;

namespace EventDriven.Consumer.Worker
{
    public class OrderStateMachine : MassTransitStateMachine<OrderState>
    {
        // 1. Declaración de Estados posibles
        public State Submitted { get; private set; } = null!;
        public State PaymentPending { get; private set; } = null!;

        // 2. Declaración de Eventos que desencadenan transiciones
        public Event<OrderSubmittedEvent> OrderSubmitted { get; private set; } = null!;
        public Event<PaymentCompletedEvent> PaymentCompleted { get; private set; } = null!;
        public Event<PaymentFailedEvent> PaymentFailed { get; private set; } = null!;

        public OrderStateMachine()
        {
            // Guardar el nombre del estado actual en la propiedad CurrentState
            InstanceState(x => x.CurrentState);

            // Correlacionar eventos usando el OrderId como CorrelationId
            Event(() => OrderSubmitted, x => x.CorrelateById(m => m.Message.OrderId));
            Event(() => PaymentCompleted, x => x.CorrelateById(m => m.Message.OrderId));
            Event(() => PaymentFailed, x => x.CorrelateById(m => m.Message.OrderId));

            // 🔄 Flujo 1: Inicio de la Saga cuando se crea una orden
            Initially(
                When(OrderSubmitted)
                    .Then(context =>
                    {
                        context.Saga.CustomerId = context.Message.CustomerId;
                        context.Saga.TotalAmount = context.Message.TotalAmount;
                        context.Saga.CreatedAtUtc = context.Message.CreatedAtUtc;
                        context.Saga.UpdatedAtUtc = DateTime.UtcNow;
                    })
                    .TransitionTo(Submitted)
                    .Publish(context => new OrderCreatedEvent(
                        context.Saga.CorrelationId,
                        context.Saga.CustomerId,
                        context.Saga.TotalAmount,
                        context.Saga.CreatedAtUtc))
            );

            // 🔄 Flujo 2: Transición cuando el pago es exitoso
            During(Submitted,
                When(PaymentCompleted)
                    .Then(context => context.Saga.UpdatedAtUtc = DateTime.UtcNow)
                    .Finalize() // Finaliza y puede eliminar la instancia si se configura
            );

            // 🔄 Flujo 3: Compensación cuando el pago falla
            During(Submitted,
                When(PaymentFailed)
                    .Then(context =>
                    {
                        context.Saga.UpdatedAtUtc = DateTime.UtcNow;
                        // Lógica compensatoria: Cancelación de la orden
                    })
                    .Finalize()
            );

            // Eliminar la instancia de la base de datos una vez que entra en estado Final
            SetCompletedWhenFinalized();
        }
    }
}
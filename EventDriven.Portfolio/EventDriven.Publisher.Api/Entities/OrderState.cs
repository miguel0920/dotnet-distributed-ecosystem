using MassTransit;

namespace EventDriven.Publisher.Api.Entities;

public class OrderState : SagaStateMachineInstance
{
    // 🔑 Identificador único de la correlación (Mismo ID que OrderId)
    public Guid CorrelationId { get; set; }

    // Estado actual de la máquina de estados (ej. "Submitted", "Completed")
    public string CurrentState { get; set; } = null!;

    // Datos del proceso de negocio que queremos conservar
    public string CustomerId { get; set; } = null!;
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // Requerido para Concurrencia Optimista (RowVersion en SQL Server)
    public byte[] RowVersion { get; set; } = null!;
}
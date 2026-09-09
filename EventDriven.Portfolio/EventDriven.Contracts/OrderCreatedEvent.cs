namespace EventDriven.Contracts;

public record OrderCreatedEvent(
    Guid OrderId,
    string CustomerId,
    decimal TotalAmount,
    DateTime CreatedAtUtc
);

public record OrderSubmittedEvent(Guid OrderId, string CustomerId, decimal TotalAmount, DateTime CreatedAtUtc);

public record PaymentCompletedEvent(Guid OrderId, DateTime PaymentDate);

public record PaymentFailedEvent(Guid OrderId, string Reason);
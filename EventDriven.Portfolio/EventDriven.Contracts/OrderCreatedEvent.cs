namespace EventDriven.Contracts;

public record OrderCreatedEvent(
    Guid OrderId,
    string CustomerId,
    decimal TotalAmount,
    DateTime CreatedAtUtc
);
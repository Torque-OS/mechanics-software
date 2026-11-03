using MechanicsSoftware.Domain.ValueObjects;

namespace MechanicsSoftware.Domain.Entities;

public sealed class ServiceOrderStatusHistory : Entity<Guid>
{
    public Guid ServiceOrderId { get; private set; }
    public ServiceOrderStatus.Status Status { get; private set; }
    public DateTime EnteredAt { get; private set; }

    private ServiceOrderStatusHistory() { }

    public static ServiceOrderStatusHistory Create(
        Guid serviceOrderId,
        ServiceOrderStatus.Status status,
        DateTime enteredAt) => new()
        {
            Id = Guid.NewGuid(),
            ServiceOrderId = serviceOrderId,
            Status = status,
            EnteredAt = enteredAt
        };

}
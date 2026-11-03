using MechanicsSoftware.Domain.Exceptions;

namespace MechanicsSoftware.Domain.ValueObjects;

public sealed class ServiceOrderStatus(ServiceOrderStatus.Status value) : ValueObject
{
    public Status Value => value;
    public enum Status
    {
        Received,
        InDiagnosis,
        AwaitingApproval,
        AwaitingPayment,
        InExecution,
        Completed,
        Delivered,
        Cancelled
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    // AwaitingApproval/AwaitingPayment/InExecution are entered on events from the Billing and
    // Execution services (BudgetGenerated, BudgetApproved, PaymentConfirmed) — wired in F4-22.
    // Cancelled is reachable from InDiagnosis through InExecution as a saga compensation
    // (BudgetFailed, BudgetRejected, PaymentFailed, ExecutionFailed) — wired in F4-23.
    private static readonly HashSet<(Status From, Status To)> _validTransitions =
    [
        (Status.Received,         Status.InDiagnosis),
        (Status.InDiagnosis,      Status.AwaitingApproval),
        (Status.InDiagnosis,      Status.Cancelled),
        (Status.AwaitingApproval, Status.AwaitingPayment),
        (Status.AwaitingApproval, Status.Cancelled),
        (Status.AwaitingPayment,  Status.InExecution),
        (Status.AwaitingPayment,  Status.Cancelled),
        (Status.InExecution,      Status.Completed),
        (Status.InExecution,      Status.Cancelled),
        (Status.Completed,        Status.Delivered),
    ];

    public ServiceOrderStatus TransitionTo(Status newStatus)
    {
        if (!_validTransitions.Contains((value, newStatus)))
            throw new InvalidStatusTransitionException(this, new ServiceOrderStatus(newStatus));

        return new ServiceOrderStatus(newStatus);
    }

    public override string ToString() => Value switch
    {
        Status.Received => "RECEIVED",
        Status.InDiagnosis => "IN_DIAGNOSIS",
        Status.AwaitingApproval => "AWAITING_APPROVAL",
        Status.AwaitingPayment => "AWAITING_PAYMENT",
        Status.InExecution => "IN_EXECUTION",
        Status.Completed => "COMPLETED",
        Status.Delivered => "DELIVERED",
        Status.Cancelled => "CANCELLED",
        _ => Value.ToString()
    };

    public bool Is(Status s) => Value == s;

    public static ServiceOrderStatus CreateReceived() => new(Status.Received);
}

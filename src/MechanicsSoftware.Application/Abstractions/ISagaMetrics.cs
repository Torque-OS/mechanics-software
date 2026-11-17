namespace MechanicsSoftware.Application.Abstractions;

/// <summary>Saga observability (F4-37): message flow between the services and compensations.</summary>
public interface ISagaMetrics
{
    void MessageConsumed(string messageType);

    void MessagePublished(string messageType);

    void ConsumeFaulted(string messageType);

    /// <summary>Cancelled orders, by the saga stage (status) the order was in when it was cancelled.</summary>
    void SetCompensationsByStage(string stage, long count);
}

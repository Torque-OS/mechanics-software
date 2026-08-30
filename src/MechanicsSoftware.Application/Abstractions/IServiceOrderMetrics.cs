namespace MechanicsSoftware.Application.Abstractions;

public interface IServiceOrderMetrics
{
    void OrderOpened();

    void OrderCompleted();
}
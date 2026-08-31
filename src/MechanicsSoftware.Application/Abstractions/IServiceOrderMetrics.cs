namespace MechanicsSoftware.Application.Abstractions;

public interface IServiceOrderMetrics
{
    void OrderOpened();

    void OrderCompleted();

    void SetOrderTotals(long opened, long completed);

    void SetAverageExecutionTime(double averageHours, int orderCount);
}
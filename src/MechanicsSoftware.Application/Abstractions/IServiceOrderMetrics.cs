namespace MechanicsSoftware.Application.Abstractions;

public interface IServiceOrderMetrics
{
    void OrderOpened();

    void OrderCompleted();

    void OrderStatusChanged(string status);

    void SetOrderTotals(long opened, long completed);

    void SetAverageExecutionTime(double averageHours, int orderCount);

    void ObserveExecutionDurationByStatus(string status, double durationHours);

    void SetOrderTotalByStatus(string status, long count);

    void SetAverageExecutionDurationByStatus(string status, double averageDurationHours);

    void RecordHttpError(string method, string path, int statusCode);

    void RecordHttpLatency(double durationMs, string method, string path, int statusCode);
}
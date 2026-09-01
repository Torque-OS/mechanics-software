namespace MechanicsSoftware.Application.Abstractions;

public interface IServiceOrderMetrics
{
    void OrderOpened();

    void OrderCompleted();

    void OrderStatusChanged(string status);

    void SetOrderTotals(long opened, long completed);

    void SetAverageExecutionTime(double averageHours, int orderCount);

    void SetAverageExecutionTimeByStatus(string status, double averageHours);

    void RecordHttpError(string method, string path, int statusCode);

    void RecordHttpLatency(double durationMs, string method, string path, int statusCode);
}
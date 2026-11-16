using MechanicsSoftware.Application.Abstractions;
using Prometheus;

namespace MechanicsSoftware.API.Metrics;

public sealed class PrometheusSagaMetrics : ISagaMetrics
{
    private static readonly Counter Consumed = global::Prometheus.Metrics.CreateCounter(
        "mechanics_saga_messages_consumed_total",
        "Saga messages consumed by the OS Service, by message type.",
        new CounterConfiguration { LabelNames = ["message"] });

    private static readonly Counter Published = global::Prometheus.Metrics.CreateCounter(
        "mechanics_saga_messages_published_total",
        "Saga messages published by the OS Service, by message type.",
        new CounterConfiguration { LabelNames = ["message"] });

    private static readonly Counter ConsumeFaults = global::Prometheus.Metrics.CreateCounter(
        "mechanics_saga_consume_faults_total",
        "Saga messages whose consumer threw, by message type.",
        new CounterConfiguration { LabelNames = ["message"] });

    private static readonly Gauge CompensationsByStage = global::Prometheus.Metrics.CreateGauge(
        "mechanics_saga_compensations_by_stage",
        "Cancelled service orders by the saga stage they were cancelled from.",
        new GaugeConfiguration { LabelNames = ["stage"] });

    public void MessageConsumed(string messageType) => Consumed.WithLabels(messageType).Inc();

    public void MessagePublished(string messageType) => Published.WithLabels(messageType).Inc();

    public void ConsumeFaulted(string messageType) => ConsumeFaults.WithLabels(messageType).Inc();

    public void SetCompensationsByStage(string stage, long count) => CompensationsByStage.WithLabels(stage).Set(count);
}

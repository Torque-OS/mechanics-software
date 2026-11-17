# Dashboard Datadog — fluxo da saga e falhas (F4-37)

O dashboard fica versionado em [`observability/datadog/saga-dashboard.json`](../../observability/datadog/saga-dashboard.json). Para criar ou atualizar no Datadog:

```bash
DD_API_KEY=... DD_APP_KEY=... scripts/datadog-import-dashboard.sh                      # cria e imprime o id
DD_API_KEY=... DD_APP_KEY=... DASHBOARD_ID=<id> scripts/datadog-import-dashboard.sh    # atualiza o mesmo
```

`DD_APP_KEY` precisa do escopo `dashboards_write`. `DD_SITE` tem como padrão `datadoghq.com`. Edite o dashboard **no JSON** e reimporte: mudanças feitas só na UI se perdem na próxima importação.

## Seções

| Seção | Widget | Métrica (Datadog) | Origem |
|---|---|---|---|
| Volume de OS | Abertas hoje / total, concluídas hoje | `mechanics.mechanics_service_orders_opened_today`, `..._opened_total`, `..._completed_today` | Banco do OS Service, atualizado a cada minuto |
| | OS por etapa da saga | `mechanics.mechanics_service_orders_current_by_status` por `status` | Idem, inclui `AWAITING_PAYMENT` |
| Tempo por etapa | Tempo médio em cada etapa | `mechanics.mechanics_service_orders_average_execution_duration_hours_by_status` por `status` | `service_order_status_history`; a etapa atual conta até agora |
| Compensações | OS canceladas | `..._current_by_status{status:cancelled}` | Banco |
| | Compensações por etapa | `mechanics.mechanics_saga_compensations_by_stage` por `stage` | Etapa em que a OS estava ao ser cancelada |
| | Comandos de compensação | `mechanics.mechanics_saga_messages_published.count{message:cancelbudget}` | `MassTransitEventPublisher` |
| Fluxo de mensagens | Eventos consumidos / mensagens publicadas | `mechanics.mechanics_saga_messages_consumed.count`, `..._published.count` por `message` | `SagaMetricsObserver` / `MassTransitEventPublisher` |
| Falhas e DLQ | Mensagens nas DLQs | `rabbitmq.rabbitmq_queue_messages{queue:*_error}` por `queue` | RabbitMQ (`rabbitmq_prometheus`, métricas por fila) |
| | Falhas de consumo | `mechanics.mechanics_saga_consume_faults.count` por `message` | `SagaMetricsObserver` |
| | Erros HTTP | `mechanics.mechanics_http_errors.count` por `status_code` | Middleware HTTP |

**Etapas de compensação** (`stage`): `AWAITING_APPROVAL` = orçamento recusado; `AWAITING_PAYMENT` = pagamento falhou; `IN_EXECUTION` = execução falhou (estorno + `CancelBudget`).

## Como as métricas chegam ao Datadog

- O OS Service expõe `/metrics` (prometheus-net). O Datadog Agent lê via a anotação `ad.datadoghq.com/api.checks` em `k8s/deployment-api.yaml`, com namespace `mechanics`. A lista `metrics` dessa anotação é um filtro: métrica nova precisa entrar nela (as da saga entram por `mechanics_saga_.*`).
- O check OpenMetrics (V2) do Datadog prefixa o namespace e envia counters Prometheus (`*_total`) como `<nome sem _total>.count`. Gauges mantêm o nome.
- Métricas calculadas do banco têm o mesmo valor em todas as réplicas, por isso o dashboard usa `max:`. Counters são por réplica e usam `sum:`.
- As métricas por fila do RabbitMQ (necessárias para a DLQ) exigem `prometheus.return_per_object_metrics = true`, configurado em `mechanics-infra-k8s/modules/rabbitmq`.

## Fora do escopo

Alertas (monitores) para DLQ, falhas e latência são do F4-35 (#326).

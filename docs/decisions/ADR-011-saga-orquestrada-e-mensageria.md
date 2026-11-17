# ADR-011: Saga Orquestrada e Mensageria (RabbitMQ + MassTransit)

**Status:** Accepted
**Date:** 2026-09-20
**Autores:** Time Torque-OS

---

## Contexto

O fluxo crítico de uma OS atravessa três serviços: abrir OS → gerar orçamento → aguardar aprovação → pagamento → execução → finalização. Falha em qualquer etapa exige **rollback por compensação**, sem transação distribuída.

## Decisão

### 1. Saga **orquestrada**, com orquestrador no OS Service

Uma *state machine* (MassTransit Saga State Machine, correlação por `ServiceOrderId`) no OS Service comanda Billing e Execution por mensagens e reage aos eventos de resposta. O estado da saga é persistido no PostgreSQL do OS Service.

**Alternativa descartada — coreografia:** cada serviço reage a eventos dos outros sem coordenador. Mais desacoplada, porém o fluxo e as compensações ficam distribuídos e difíceis de rastrear, testar e demonstrar.

**Por que orquestração:**
- O fluxo é sequencial e de negócio único; um ponto conhece o passo atual e quais compensações executar, em ordem inversa.
- Estado da saga consultável e observável (um trace/log por OS).
- O OS Service já é dono do ciclo de vida da OS, então não é criado um quarto serviço.
- Trade-off aceito: o OS Service concentra a coordenação (ponto de atenção; mitigado por persistência do estado, retries e DLQ).

### 2. Passos e compensações

| # | Passo | Serviço | Sucesso | Falha / compensação |
|---|---|---|---|---|
| 1 | Abrir OS | OS | `ServiceOrderOpened` | — |
| 2 | Gerar orçamento | Billing | `BudgetGenerated` | `BudgetFailed` → cancelar OS |
| 3 | Aprovação do cliente | Billing | `BudgetApproved` | `BudgetRejected` → cancelar OS |
| 4 | Pagamento (Mercado Pago) | Billing | `PaymentConfirmed` | `PaymentFailed` → cancelar orçamento, cancelar OS |
| 5 | Execução | Execution | `ExecutionCompleted` | `ExecutionFailed` → **estornar pagamento**, cancelar orçamento, cancelar OS |
| 6 | Finalizar OS | OS | `ServiceOrder = COMPLETED` | — |

Compensações são comandos idempotentes: `CancelBudget`, `RefundPayment`, `CancelExecution`.

> **Atualização (2026-11-10, F4-17 e F4-22/F4-23):** não existe comando `RefundPayment` separado.
> O OS Service envia um único `CancelBudget`, e o Billing Service cancela o QR se o pagamento ainda
> não foi recebido ou estorna no Mercado Pago se já foi, respondendo com `BudgetCancelled`.
> No passo 5, `ExecutionFailed` leva a OS a `CANCELLED` e envia `CancelBudget`, que cobre o estorno.
> Detalhes no README do `mechanics-billing-service`, seção "Cancellation and refund".

### 3. Mensageria: **RabbitMQ** + **MassTransit**

- RabbitMQ no cluster EKS (e em `docker-compose` localmente).
- MassTransit fornece: state machine de saga, *retry* com backoff, *outbox* transacional, filas de erro (DLQ) e propagação de `CorrelationId`/trace context.
- Consumidores idempotentes (chave: `MessageId`); publicação via *outbox* para não perder eventos entre banco e broker.
- REST síncrono apenas onde necessário (consultas), com timeout e resiliência; **nunca acesso direto ao banco de outro serviço**. Regras e primeiro caso na [ADR-013](ADR-013-rest-sincrono-entre-servicos.md).

> **Atualização (F4-24):** os três serviços usam a mesma política de *retry* em todos os consumidores: 5 novas tentativas com backoff exponencial (cerca de 16 s no total) para falhas transitórias; violações de regra de negócio (`DomainException` e derivadas) e entidades inexistentes (`NotFoundException`) não são repetidas e vão direto para a fila `_error` (DLQ). Combinado com o *inbox* (deduplicação por `MessageId`) e o *outbox*, nenhuma mensagem é perdida nem processada duas vezes.
> Não há *redelivery* agendado: ele exige o plugin `rabbitmq_delayed_message_exchange`, que a imagem oficial do RabbitMQ não traz. Mensagens na DLQ são reprocessadas movendo-as de volta à fila original.
>
> **Atualização (RequestBudget):** o passo 2 é iniciado pelo `POST /api/service-orders/{id}/request-budget` (staff) no OS Service, que envia `RequestBudget` com o snapshot dos itens. O pagamento é iniciado pelo cliente direto no Billing (`POST /api/payments`), por isso não existe um comando `RequestPayment`.

**Descartados:** SQS/SNS (gerenciado, mas sem saga nativa e mais difícil de rodar/demonstrar localmente) e Kafka (pesado para 3 serviços).

> Licenciamento: fixar **MassTransit v8** (Apache-2.0); a v9 é comercial. Validar antes de adicionar a dependência.

## Consequências

- Todas as OS têm um `SagaState` rastreável; falhas terminam em `CANCELLED` consistente.
- Necessário testar cada compensação (BDD cobre caminho feliz + uma falha com estorno).
- Broker vira dependência crítica: provisionar com Terraform/manifestos e monitorar filas e DLQ no Datadog.

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

### 3. Mensageria: **RabbitMQ** + **MassTransit**

- RabbitMQ no cluster EKS (e em `docker-compose` localmente).
- MassTransit fornece: state machine de saga, *retry* com backoff, *outbox* transacional, filas de erro (DLQ) e propagação de `CorrelationId`/trace context.
- Consumidores idempotentes (chave: `MessageId`); publicação via *outbox* para não perder eventos entre banco e broker.
- REST síncrono apenas onde necessário (consultas), com timeout e resiliência; **nunca acesso direto ao banco de outro serviço**.

**Descartados:** SQS/SNS (gerenciado, mas sem saga nativa e mais difícil de rodar/demonstrar localmente) e Kafka (pesado para 3 serviços).

> Licenciamento: fixar **MassTransit v8** (Apache-2.0); a v9 é comercial. Validar antes de adicionar a dependência.

## Consequências

- Todas as OS têm um `SagaState` rastreável; falhas terminam em `CANCELLED` consistente.
- Necessário testar cada compensação (BDD cobre caminho feliz + uma falha com estorno).
- Broker vira dependência crítica: provisionar com Terraform/manifestos e monitorar filas e DLQ no Datadog.

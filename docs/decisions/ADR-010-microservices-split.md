# ADR-010: Divisão em Microsserviços (OS, Billing, Execution)

**Status:** Accepted
**Date:** 2026-09-20
**Autores:** Time Torque-OS

---

## Contexto

A oficina passou a operar em múltiplas filiais. O monólito (Domain / Application / Infrastructure / API) concentra ordens de serviço, orçamento, estoque e execução. A Fase 4 exige no mínimo **3 microsserviços independentes**, cada um com repositório, infraestrutura e banco próprios, e **nenhum serviço pode acessar o banco de outro**.

## Decisão

| Serviço | Repositório | Responsabilidade |
|---|---|---|
| **OS Service** | `mechanics-software` (este repo, refatorado) | Clientes, veículos, catálogo de serviços, peças/estoque, ciclo de vida da OS (abertura, status, histórico), autenticação por CPF e **orquestrador da Saga** |
| **Billing Service** | `mechanics-billing-service` | Geração/envio de orçamento, aprovação/rejeição, pagamento via Mercado Pago, estorno |
| **Execution Service** | `mechanics-execution-service` | Fila de execução, acompanhamento de diagnóstico e reparo, finalização |

Regras:

- `Budget` deixa de ser entidade filha de `ServiceOrder` e passa a ser agregado do **Billing**. O OS Service guarda apenas uma referência (`BudgetId`, `BudgetTotal`) atualizada por eventos.
- Identificadores entre serviços são apenas IDs (`ServiceOrderId`, `CustomerId`); nenhum serviço faz `JOIN` ou consulta ao banco de outro.
- Dados de outros contextos necessários a um serviço chegam por **snapshot no evento** (ex.: itens da OS no pedido de orçamento).
- Infraestrutura compartilhada (VPC, EKS, API Gateway, Lambda de autenticação da Fase 3) é reaproveitada; **bancos e filas/exchanges são por serviço**.

## Justificativa

- Os limites seguem os *bounded contexts* já documentados (Service Orders, Budget/Billing, Execution).
- Billing tem ciclo de mudança próprio (integração Mercado Pago, webhooks) e requisitos de consistência distintos.
- Execution é um contexto de fila/produção com carga e escala diferentes do atendimento.
- Manter o OS Service neste repositório preserva o histórico e a base de código da Fase 3.

## Consequências

- Consistência entre serviços passa a ser **eventual**, coordenada por Saga (ADR-011).
- Mais pipelines, manifestos e repositórios para manter (um CI/CD por serviço).
- Necessário versionar contratos de eventos (ver `docs/architecture/architecture-fase4.md`).

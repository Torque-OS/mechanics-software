# Tech Challenge — Fase 4
## FIAP POS Tech · 15SOAT · Turma 2026

**Grupo: TorqueOS**
| Nome | RM |
|---|---|
| Allan | RM373714 |
| Daniel | RM370852 |
| Diogo | RM371224 |
| Lucas | RM371615 |
| Joelma Renata | RM371593 |

---

## 1. Repositórios (Microsserviços e Infraestrutura)

| Repositório | Propósito | Link |
|---|---|---|
| **mechanics-software** | **OS Service** + Orquestrador da Saga (ASP.NET Core 8, PostgreSQL 16 + MongoDB) | https://github.com/Torque-OS/mechanics-software |
| **mechanics-billing-service** | **Billing Service** — Orçamentos + Pagamentos Mercado Pago + Compensação/Estorno (ASP.NET Core 8, PostgreSQL 16 + MongoDB) | https://github.com/Torque-OS/mechanics-billing-service |
| **mechanics-execution-service** | **Execution Service** — Fila de Execução, Diagnóstico e Reparo (ASP.NET Core 8, PostgreSQL 16 + MongoDB) | https://github.com/Torque-OS/mechanics-execution-service |
| **mechanics-lambda** | **Auth Serverless** — Autenticação CPF + Custom JWT Authorizer (Node.js 20) | https://github.com/Torque-OS/mechanics-lambda |
| **mechanics-infra-k8s** | **IaC Cluster & Mensageria** — Terraform VPC + EKS + API Gateway + RabbitMQ + Datadog | https://github.com/Torque-OS/mechanics-infra-k8s |
| **mechanics-infra-db** | **IaC Bancos de Dados** — Terraform AWS RDS PostgreSQL 16 + MongoDB Atlas M0 por serviço | https://github.com/Torque-OS/mechanics-infra-db |

---

## 2. Vídeo de Demonstração (Até 15 min)

> **Roteiro detalhado:** [`docs/project/video-script-fase4.md`](./project/video-script-fase4.md)
> **Link do Vídeo:** *(Ainda falta inserir link do Video, gravação programada para 26/11/2026)*

O vídeo demonstra:
- Divisão em 3 microsserviços independentes com bancos SQL (PostgreSQL) e NoSQL (MongoDB) isolados por serviço
- Fluxo completo de uma Ordem de Serviço passando pelos 3 microsserviços via RabbitMQ + MassTransit Outbox/Inbox
- Integração com Mercado Pago (geração de QR dinâmico e confirmação via webhook assinado)
- Execução do Saga Pattern Orquestrado no caminho feliz e em cenário de falha com rollback/compensação (`CancelBudget` + estorno no Mercado Pago + liberação de estoque)
- Pipelines CI/CD independentes com testes unitários (cobertura ≥ 80%), BDD (Reqnroll/Gherkin) e SonarCloud Quality Gate
- Monitoramento, logs estruturados com `CorrelationId` e tracing distribuído (OpenTelemetry) no Datadog

---

## 3. Diagrama Geral da Arquitetura Final e Diagramas de Sequência da Saga

Toda a documentação arquitetural e os diagramas de sequência (fluxo feliz e compensação/rollback) da Fase 4 estão versionados em:
- [Arquitetura Fase 4 e Diagramas de Sequência da Saga (`docs/architecture/architecture-fase4.md`)](./architecture/architecture-fase4.md)
- [ADR-010 — Divisão em Microsserviços (OS, Billing e Execution)](./decisions/ADR-010-microservices-split.md)
- [ADR-011 — Saga Orquestrada e Mensageria com RabbitMQ + MassTransit](./decisions/ADR-011-saga-orquestrada-e-mensageria.md)
- [ADR-012 — Estratégia de Banco de Dados Polyglot (PostgreSQL + MongoDB) por Serviço](./decisions/ADR-012-banco-por-servico.md)
- [Justificativa Detalhada de Bancos de Dados (`docs/database/database-justification.md`)](./database/database-justification.md)

---

## 4. Justificativa da Estratégia de Saga Escolhida (Saga Orquestrada)

Conforme documentado no **ADR-011**, optamos pela **Saga Orquestrada** tendo o **OS Service** como orquestrador central, utilizando **RabbitMQ** como broker e **MassTransit v8** com **EF Core Transactional Outbox & Inbox**:
1. **Ponto único de decisão sobre o ciclo de vida da OS:** A Ordem de Serviço (`ServiceOrder`) é a máquina de estados central do domínio (`RECEIVED → IN_DIAGNOSIS → AWAITING_APPROVAL → AWAITING_PAYMENT → IN_EXECUTION → COMPLETED → DELIVERED`, ou `CANCELLED`). Centralizar a decisão no OS Service evita acoplamento cíclico entre Billing e Execution.
2. **Garantia de entrega e idempotência (Outbox + Inbox):** Todos os eventos e comandos (`ServiceOrderOpened`, `BudgetGenerated`, `BudgetApproved`, `PaymentConfirmed`, `EnqueueExecution`, `ExecutionCompleted`, `PaymentFailed`, `ExecutionFailed`, `CancelBudget`, `BudgetCancelled`) são gravados na tabela `outbox_message` na mesma transação ACID da mudança de estado e deduplicados no consumidor via tabela `inbox_state` (`MessageId`) e guardas de idempotência de domínio.
3. **Compensação determinística (Rollback):**
   - Falha na geração ou rejeição do orçamento (`BudgetFailed` / `BudgetRejected`) → OS vai para `CANCELLED` e libera peças reservadas no estoque.
   - Falha no pagamento (`PaymentFailed`) → OS vai para `CANCELLED`, libera peças reservadas e envia comando `CancelBudget` ao Billing Service.
   - Falha na execução (`ExecutionFailed`) → OS vai para `CANCELLED`, libera peças reservadas e envia comando `CancelBudget` ao Billing Service, que estorna o pagamento confirmado no Mercado Pago (`POST /v1/orders/{id}/refund` com chave de idempotência `refund-<paymentId>`) e publica `BudgetCancelled`.

---

## 5. Justificativa da Divisão dos Microsserviços e Bancos de Dados (SQL + NoSQL)

Conforme **ADR-010** e **ADR-012**, cada microsserviço possui **1 banco relacional (PostgreSQL 16)** e **1 banco orientado a documentos (MongoDB 8.0)** totalmente isolados (nenhum serviço acessa o banco de outro):

| Microsserviço | Responsabilidade (Bounded Context) | Uso do PostgreSQL (ACID + Outbox/Inbox) | Uso do MongoDB (NoSQL) |
|---|---|---|---|
| **OS Service** (`mechanics-software`) | Clientes, Veículos, Catálogo/Estoque de Peças e Serviços, Ciclo de Vida da OS e Orquestração da Saga | `service_orders`, `customers`, `vehicles`, `parts`, `services`, `outbox_message`, `inbox_state` | `status_history` — Timeline append-only de auditoria das transições de status da OS |
| **Billing Service** (`mechanics-billing-service`) | Geração/Decisão de Orçamentos, Cobrança QR Dinâmico no Mercado Pago, Webhook e Estornos | `budgets`, `budget_items`, `payments`, `outbox_message`, `inbox_state` | `mercadopago_notifications` (payloads brutos de webhooks) e `payment_events` (snapshots de auditoria do provedor) |
| **Execution Service** (`mechanics-execution-service`) | Fila de Execução, Status de Diagnóstico/Reparo e Notas Técnicas | `execution_orders`, `outbox_message`, `inbox_state` | `execution_queue` (projeção de leitura ordenada por prioridade/chegada) e `execution_notes` (notas técnicas de diagnóstico e reparo) |

---

## 6. Qualidade de Código, Cobertura de Testes (≥ 80%), BDD e CI/CD

| Microsserviço | Cobertura de Testes Unitários | Testes BDD (Gherkin / Reqnroll) | CI/CD & SonarCloud | Collection Postman |
|---|---|---|---|---|
| **OS Service** (`mechanics-software`) | **99,16% Line / 94,69% Branch** (Gate `>= 80%`) | `tests/MechanicsSoftware.BddTests` (`ServiceOrderSaga.feature` — 3 cenários) | `coverage.yml` + `deploy.yml` | `MechanicsSoftware.postman_collection.json` |
| **Billing Service** (`mechanics-billing-service`) | **> 90% Line** (127 testes, Gate `>= 80%`) | Coberto pela Saga BDD | `ci.yml` + `deploy.yml` | `MechanicsBillingService.postman_collection.json` |
| **Execution Service** (`mechanics-execution-service`) | **92,3%+ Line** (Gate `>= 80%`) | Coberto pela Saga BDD | `ci.yml` + `deploy.yml` | `MechanicsExecutionService.postman_collection.json` |

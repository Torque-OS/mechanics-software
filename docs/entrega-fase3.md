# Tech Challenge — Fase 3
## FIAP POS Tech · 15SOAT · Turma 2025

**Grupo:**
| Nome | RM |
|---|---|
| Allan | RM373714 |
| Daniel | RM370852 |
| Diogo | RM371224 |
| Lucas | RM371615 |
| Joelma Renata | RM371593 |

---

## 1. Repositórios

| Repositório | Propósito | Link |
|---|---|---|
| **mechanics-software** | API principal — ASP.NET Core 8 rodando no EKS | https://github.com/Torque-OS/mechanics-software |
| **mechanics-lambda** | Function Serverless — autenticação CPF + JWT (Node.js 20) | https://github.com/Torque-OS/mechanics-lambda |
| **mechanics-infra-k8s** | Terraform — VPC + EKS + API Gateway | https://github.com/Torque-OS/mechanics-infra-k8s |
| **mechanics-infra-db** | Terraform — RDS PostgreSQL 16 gerenciado | https://github.com/Torque-OS/mechanics-infra-db |

---

## 2. Vídeo de Demonstração

> **Link:** _(a ser preenchido após gravação — F3-34)_

O vídeo demonstra:
- Autenticação com CPF via Lambda
- Execução da pipeline CI/CD
- Deploy automatizado no EKS
- Consumo das APIs protegidas pelo API Gateway
- Dashboard Datadog com métricas ao vivo
- Logs estruturados e traces em execução

---

## 3. Documentação da Arquitetura

Toda a documentação está versionada no repositório `mechanics-software`, em `docs/`.

### 3.1 Diagrama de Componentes

Visão cloud completa da Fase 3 — AWS, APIs, banco e monitoramento (Datadog):

https://github.com/Torque-OS/mechanics-software/blob/main/docs/architecture/component-diagram-fase3.md

### 3.2 Diagramas de Sequência

| Fluxo | Link |
|---|---|
| Autenticação via CPF (token issuance + authorized request) | https://github.com/Torque-OS/mechanics-software/blob/main/docs/architecture/sequence-cpf-auth.md |
| Abertura de Ordem de Serviço | https://github.com/Torque-OS/mechanics-software/blob/main/docs/architecture/sequence-service-order-opening.md |

### 3.3 RFC — Request for Comments

| Documento | Decisões técnicas cobertas |
|---|---|
| [RFC-001 — Fase 3: Cloud, banco e autenticação](https://github.com/Torque-OS/mechanics-software/blob/main/docs/rfc/RFC-001-fase3-cloud-db-auth.md) | Escolha da AWS, RDS vs K8s-hosted PostgreSQL, cadeia de autenticação CPF (Lambda → API GW → EKS + GatewayKey) |

### 3.4 ADRs — Architecture Decision Records

| ADR | Decisão |
|---|---|
| [ADR-007 — Padrão de comunicação](https://github.com/Torque-OS/mechanics-software/blob/main/docs/decisions/ADR-007-communication-pattern.md) | HTTP API v2 vs REST API v1, VPC Link vs integração pública, NLB vs ALB |
| [ADR-008 — Lambda Authorizer](https://github.com/Torque-OS/mechanics-software/blob/main/docs/decisions/ADR-008-lambda-authorizer.md) | REQUEST type, `isAuthorized` simples, cache TTL 300 s, alternativas descartadas |
| [ADR-009 — HPA Autoscaling](https://github.com/Torque-OS/mechanics-software/blob/main/docs/decisions/ADR-009-hpa-autoscaling.md) | CPU-based, min 2 / max 10 réplicas, averageUtilization 70%, VPA vs KEDA |

### 3.5 Justificativa do Banco de Dados + Diagrama ER

Justificativa formal da escolha do PostgreSQL 16 (RDS vs K8s-hosted), diagrama ER completo com todos os 10 relacionamentos, decisões de modelagem (valores monetários em centavos, snapshots de preço, flag `active` em customers):

https://github.com/Torque-OS/mechanics-software/blob/main/docs/database/database-justification.md

---

## 4. Monitoramento e Observabilidade

**Ferramenta:** Datadog

| Item | Link / Detalhes |
|---|---|
| **Dashboard público Datadog** | https://p.datadoghq.com/sb/e9eb9cda-9e7e-11f1-b0cf-de421ab27ba0-357d7f8569bef16ef90ad5ddd32dbd9c |
| Agente no K8s | DaemonSet via Helm (`mechanics-infra-k8s`) |
| Métricas expostas | `/metrics` (Prometheus) — volume diário de OS, tempo médio por status, erros |
| Logs estruturados | JSON com `correlationId` em todas as requisições (`RequestLoggingMiddleware`) |
| Alertas | Configurados para falhas no processamento de ordens de serviço |

---

## 5. Swagger / Postman

| Recurso | Link |
|---|---|
| Swagger UI (local) | `http://localhost:8080/swagger` (após `docker compose up --build`) |
| Postman Collection | https://github.com/Torque-OS/mechanics-software/blob/main/MechanicsSoftware.postman_collection.json |

---

## 6. Confirmação — usuário `soat-architecture`

O usuário `soat-architecture` foi adicionado como colaborador (read) em todos os 4 repositórios:

| Repositório | Status |
|---|---|
| mechanics-software | ✅ Adicionado |
| mechanics-lambda | ✅ Adicionado |
| mechanics-infra-k8s | ✅ Adicionado |
| mechanics-infra-db | ✅ Adicionado |

---

## 7. Infraestrutura implementada

| Componente | Tecnologia | Repo |
|---|---|---|
| API Gateway | AWS API Gateway HTTP API v2 | mechanics-infra-k8s |
| Autenticação serverless | AWS Lambda (Node.js 20) | mechanics-lambda |
| Banco gerenciado | AWS RDS PostgreSQL 16 (db.t3.micro) | mechanics-infra-db |
| Cluster Kubernetes | AWS EKS 1.35 (t3.small, 1–3 nós) | mechanics-infra-k8s |
| Escalabilidade | HPA min 2 / max 10 pods, CPU 70% | mechanics-software |
| IaC | Terraform ~>1.7 | mechanics-infra-k8s / mechanics-infra-db |
| Branch protection | Branch main protegida — PRs obrigatórios | todos os repos |
| CI/CD | GitHub Actions | todos os repos |

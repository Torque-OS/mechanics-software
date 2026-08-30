<p align="center">
  <img src="docs/logo.png" alt="Torque-OS" width="220"/>
</p>

# Mechanics Software

Backend system for a mechanic shop — built as the Tech Challenge for FIAP POS Tech (15SOAT), covering Phase 1 and Phase 2.

## Overview

A RESTful API that manages the full lifecycle of service orders, customers, vehicles, parts, and inventory for a medium-sized auto repair shop. On every service order status change, the customer receives an automatic email notification.

**Architecture:** Clean Architecture + DDD Domain  
**Stack:** C# 12 · ASP.NET Core 8 · PostgreSQL 16 · Entity Framework Core 8  
**Infra:** Docker · Kubernetes (AWS EKS) · Terraform  
**Docs:** [`/docs`](./docs)

---

## Fase 2

Fase 2 expands Fase 1 with **Clean Architecture**, **automated email notifications**, **Kubernetes on AWS EKS**, **Terraform IaC**, and a **GitHub Actions CI/CD pipeline** — turning the original monolith into a production-ready, scalable system.

### Architecture

```
GitHub Actions (CI/CD)
        │
        ├─ coverage.yml ──→ unit tests · 80% coverage gate · GitHub Pages report
        │
        └─ deploy.yml ──→ docker build · push GHCR · EF migrations · kubectl apply
                                                              │
                                                              ▼
                                              AWS EKS Cluster  (Terraform-provisioned VPC + EKS)
                                    ┌─────────────────────────────────────────────┐
                                    │  Namespace: mechanics-software              │
                                    │                                             │
                                    │  ┌─────────────────────┐                   │
                                    │  │  API Deployment     │◄── HPA            │
                                    │  │  ASP.NET Core 8     │    (auto-scale)   │
                                    │  │  N replicas         │                   │
                                    │  └──────────┬──────────┘                   │
                                    │             │ ClusterIP                    │
                                    │             ▼                              │
                                    │  ┌─────────────────────┐                   │
                                    │  │  PostgreSQL 16      │◄── PVC            │
                                    │  └─────────────────────┘    (persistent)   │
                                    │                                             │
                                    │  LoadBalancer ──────────────────► Internet │
                                    └─────────────────────────────────────────────┘
```

### Deploy flow

```
git push → main
    │
    ├── coverage.yml  →  dotnet test  →  enforce 80% line coverage  →  publish HTML report
    │
    └── deploy.yml    →  docker build  →  push to GHCR
                              │
                              └─→  kubectl apply (initContainer runs EF migrations first)
```

### Demo video

[Watch on YouTube](https://youtu.be/vqERT_zrLpo)

---

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://www.docker.com/products/docker-desktop) + Docker Compose

### Run with Docker (recommended)

```bash
docker compose up --build
```

API available at: `http://localhost:8080`  
Swagger UI: `http://localhost:8080/swagger`

> Migrations and the default admin user are applied automatically on startup.

### Run locally (step by step)

**1. Start the database**

```bash
docker compose up db -d
```

PostgreSQL will be available at `localhost:5435`.

**2. Run the API**

```bash
dotnet run --project src/MechanicsSoftware.API/MechanicsSoftware.API.csproj
```

Swagger UI: `http://localhost:5066/swagger`

On first startup the application automatically applies all pending migrations and creates a default admin user.

---

## Authentication

### Default admin credentials

| Field | Value |
|---|---|
| Email | `admin@mechanics.local` |
| Password | `Admin@123` |

Override with `SEED_ADMIN_EMAIL` and `SEED_ADMIN_PASSWORD` environment variables.

### Getting a token

Call `POST /api/auth/login` with the credentials above:

```json
{
  "email": "admin@mechanics.local",
  "password": "Admin@123"
}
```

Copy the `token` from the response.

### Using the token in Swagger

1. Open `http://localhost:5066/swagger`
2. Click the **Authorize** button (🔒)
3. Paste the token value and click **Authorize**

All protected endpoints will now work.

---

## Environment Variables

| Variable | Required | Description | Default |
|---|---|---|---|
| `JWT_SECRET` | **Yes** (production) | Secret key for JWT signing (min 32 chars) | pre-configured in `appsettings.Development.json` |
| `DATABASE_URL` | No | PostgreSQL connection string | see `appsettings.Development.json` |
| `JWT_EXPIRATION_MINUTES` | No | Token expiration in minutes | `60` |
| `BCRYPT_SALT_ROUNDS` | No | BCrypt cost factor | `12` |
| `SEED_ADMIN_EMAIL` | No | Default admin email | `admin@mechanics.local` |
| `SEED_ADMIN_PASSWORD` | No | Default admin password | `Admin@123` |
| `SMTP_HOST` | **Yes** (production) | SMTP server hostname | — |
| `SMTP_PORT` | **Yes** (production) | SMTP server port (e.g. `587`) | — |
| `SMTP_USER` | **Yes** (production) | SMTP username | — |
| `SMTP_PASS` | **Yes** (production) | SMTP password | — |
| `SMTP_FROM` | **Yes** (production) | Sender address for notification emails | — |

---

## Email Notifications

Every service order status change triggers an automatic email to the customer. The email is sent by `SmtpEmailNotifier` (Infrastructure layer), which implements the `IEmailNotifier` abstraction defined in the Application layer.

Notifications are sent on the following transitions:

| Transition | Handler |
|---|---|
| RECEIVED → IN_DIAGNOSIS | `StartDiagnosisHandler` |
| IN_DIAGNOSIS → AWAITING_APPROVAL | `SendBudgetHandler` |
| AWAITING_APPROVAL → IN_EXECUTION | `ApproveServiceOrderHandler` |
| AWAITING_APPROVAL → CANCELLED | `RejectServiceOrderHandler` |
| IN_EXECUTION → COMPLETED | `CompleteServiceOrderHandler` |
| COMPLETED → DELIVERED | `DeliverServiceOrderHandler` |

---

## Database Migrations

The project uses a local `dotnet-ef` tool pinned in `.config/dotnet-tools.json`. Run `dotnet tool restore` once to install it, then use `dotnet dotnet-ef` instead of `dotnet ef`.

### Apply existing migrations

```bash
dotnet dotnet-ef database update \
  --project src/MechanicsSoftware.Infrastructure/MechanicsSoftware.Infrastructure.csproj \
  --startup-project src/MechanicsSoftware.API/MechanicsSoftware.API.csproj
```

### Add a new migration

```bash
dotnet dotnet-ef migrations add <MigrationName> \
  --project src/MechanicsSoftware.Infrastructure/MechanicsSoftware.Infrastructure.csproj \
  --startup-project src/MechanicsSoftware.API/MechanicsSoftware.API.csproj \
  --output-dir Persistence/Migrations
```

### Remove the last migration (if not yet applied)

```bash
dotnet dotnet-ef migrations remove \
  --project src/MechanicsSoftware.Infrastructure/MechanicsSoftware.Infrastructure.csproj \
  --startup-project src/MechanicsSoftware.API/MechanicsSoftware.API.csproj
```

---

## API

### Public endpoints (no authentication)

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/auth/login` | Authenticate as staff and receive a JWT |
| `GET` | `/health` | Liveness/readiness probe |

### Protected endpoints (JWT required)

A valid token is accepted whether it was issued by `POST /api/auth/login` here or by
`POST /auth` on the API Gateway, which authenticates customers by CPF through
[mechanics-lambda](https://github.com/Torque-OS/mechanics-lambda). Both sides sign with
the same secret and algorithm.

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/service-orders/{id}/status` | Check service order status |
| `POST` | `/api/service-orders/{id}/budget-decision` | Approve or reject the budget |

| Resource | Endpoints |
|---|---|
| Customers | `GET/POST /api/customers` · `GET/PUT/DELETE /api/customers/{id}` |
| Vehicles | `GET/POST /api/vehicles` · `GET/PUT/DELETE /api/vehicles/{id}` |
| Parts | `GET/POST /api/parts` · `GET/PUT/DELETE /api/parts/{id}` · `PATCH /api/parts/{id}/stock` |
| Services | `GET/POST /api/services` · `GET/PUT/DELETE /api/services/{id}` |
| Service Orders | `GET/POST /api/service-orders` · full lifecycle via action endpoints |

Full documentation and request/response schemas available at `/swagger` when running.

### Usage examples

#### 1. Authenticate

```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "admin@mechanics.local",
  "password": "Admin@123"
}
```

```json
{ "token": "<JWT>" }
```

Use the token as `Authorization: Bearer <JWT>` on all protected requests.

#### 2. Create a customer

```http
POST /api/customers
Authorization: Bearer <JWT>
Content-Type: application/json

{
  "name": "João da Silva",
  "documentValue": "123.456.789-09",
  "personType": "INDIVIDUAL",
  "email": "joao@example.com",
  "phone": "(11) 99999-0001"
}
```

```json
{ "id": "3fa85f64-...", "name": "João da Silva", ... }
```

#### 3. Open a service order

```http
POST /api/service-orders
Authorization: Bearer <JWT>
Content-Type: application/json

{
  "customerId": "<customer-id>",
  "vehicleId": "<vehicle-id>"
}
```

```json
{ "id": "<order-id>", "status": "RECEIVED", ... }
```

#### 4. Progress through the lifecycle

```http
# Move to diagnosis
POST /api/service-orders/{id}/start-diagnosis

# Add a service and a part
POST /api/service-orders/{id}/services
{ "serviceId": "<id>", "quantity": 1 }

POST /api/service-orders/{id}/parts
{ "partId": "<id>", "quantity": 2 }

# Generate and send budget to customer (triggers email)
POST /api/service-orders/{id}/budget
POST /api/service-orders/{id}/send-budget

# Customer approves or rejects
POST /api/service-orders/{id}/budget-decision
{ "decision": "approve" }   # or "reject"

# Complete and deliver
POST /api/service-orders/{id}/start-execution
POST /api/service-orders/{id}/complete
POST /api/service-orders/{id}/deliver
```

#### 5. Check status (JWT required)

```http
GET /api/service-orders/{id}/status
```

```json
{ "status": "IN_EXECUTION" }
```

---

## Service Order Lifecycle

```
RECEIVED → IN_DIAGNOSIS → AWAITING_APPROVAL → IN_EXECUTION → COMPLETED → DELIVERED
                                    ↓
                                CANCELLED
```

---

## Project Structure

```
src/
  MechanicsSoftware.Domain/          # Entities, value objects, domain rules (innermost layer)
  MechanicsSoftware.Application/     # Use cases, command/query handlers, abstractions (IEmailNotifier, IAppDbContext)
  MechanicsSoftware.Infrastructure/  # EF Core, JWT, BCrypt, SmtpEmailNotifier
  MechanicsSoftware.API/             # Controllers, middleware, Swagger, DI composition root

tests/
  MechanicsSoftware.UnitTests/       # Domain + Application + Infrastructure unit tests
  MechanicsSoftware.IntegrationTests/ # Full HTTP integration tests (WebApplicationFactory)

k8s/                                 # Kubernetes manifests (AWS EKS)
infra/                               # Terraform IaC (VPC + EKS cluster)
```

See [`docs/architecture/overview.md`](./docs/architecture/overview.md) for full details.

---

## Kubernetes

Manifests live in `k8s/` and target an AWS EKS cluster provisioned by Terraform.

| File | Purpose |
|---|---|
| `namespace.yaml` | Dedicated namespace |
| `configmap.yaml` | Non-secret configuration |
| `secret.yaml` | JWT secret, DB credentials, SMTP credentials |
| `deployment-api.yaml` | API deployment (runs EF migrations via initContainer) |
| `service-api.yaml` | LoadBalancer service for the API |
| `hpa.yaml` | Horizontal Pod Autoscaler |

### Apply manually

```bash
kubectl apply -f k8s/
```

---

## Terraform

Infrastructure is defined in `infra/` using the `terraform-aws-modules/eks/aws` and `terraform-aws-modules/vpc/aws` modules.

```bash
cd infra
terraform init
terraform plan
terraform apply
```

See [`infra/README.md`](./infra/README.md) for variable descriptions and prerequisites.

---

## CI/CD Pipeline

Two GitHub Actions workflows run on every push/PR to `main`:

| Workflow | File | What it does |
|---|---|---|
| Coverage Report | `.github/workflows/coverage.yml` | Builds, runs unit tests, enforces 80% line coverage, publishes HTML report to GitHub Pages |
| Deploy | `.github/workflows/deploy.yml` | Builds & pushes Docker image to GHCR, runs EF Core migrations via initContainer, applies K8s manifests to EKS |

Coverage report: [rnataoliveira.github.io/mechanics-software](https://rnataoliveira.github.io/mechanics-software/)

---

## Running Tests

```bash
# Unit tests
dotnet test tests/MechanicsSoftware.UnitTests

# Unit tests with coverage report
dotnet test tests/MechanicsSoftware.UnitTests \
  --collect:"XPlat Code Coverage" \
  --settings coverlet.runsettings \
  --results-directory ./coverage-results

# Integration tests (requires Docker — starts PostgreSQL automatically)
dotnet test tests/MechanicsSoftware.IntegrationTests
```

---

## Documentation

| Document | Path |
|---|---|
| Architecture Overview | [`docs/architecture/overview.md`](./docs/architecture/overview.md) |
| Event Storming | [`docs/domain/event-storming.md`](./docs/domain/event-storming.md) |
| Ubiquitous Language | [`docs/domain/ubiquitous-language.md`](./docs/domain/ubiquitous-language.md) |
| Aggregates & Entities | [`docs/domain/aggregates-and-entities.md`](./docs/domain/aggregates-and-entities.md) |
| Bounded Contexts | [`docs/domain/bounded-contexts.md`](./docs/domain/bounded-contexts.md) |
| ADR-001 Tech Stack | [`docs/decisions/ADR-001-tech-stack.md`](./docs/decisions/ADR-001-tech-stack.md) |
| ADR-002 Architecture | [`docs/decisions/ADR-002-architecture.md`](./docs/decisions/ADR-002-architecture.md) |
| ADR-003 Database | [`docs/decisions/ADR-003-database.md`](./docs/decisions/ADR-003-database.md) |
| ADR-004 Application Layer | [`docs/decisions/ADR-004-application-layer-conventions.md`](./docs/decisions/ADR-004-application-layer-conventions.md) |
| ADR-005 Clean Architecture Migration | [`docs/decisions/ADR-005-clean-architecture-migration.md`](./docs/decisions/ADR-005-clean-architecture-migration.md) |
| ADR-006 Database Migration Strategy | [`docs/decisions/ADR-006-database-migration-strategy.md`](./docs/decisions/ADR-006-database-migration-strategy.md) |
| Component Diagram — Fase 3 Cloud | [`docs/architecture/component-diagram-fase3.md`](./docs/architecture/component-diagram-fase3.md) |

---

## Contributing

See [`CONTRIBUTING.md`](./CONTRIBUTING.md) for project conventions, commit message format, and branch naming rules.

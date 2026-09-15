# Fase 3 — Demo Video Recording Guide

**Target length:** ≤ 15 minutes  
**Recommended tools:** OBS Studio or Loom (screen + webcam)

---

## Pre-recording checklist

Do all of this **before** hitting record.

- [ ] AWS infrastructure is running:
  - API Gateway HTTP API v2 URL available (e.g. `https://<id>.execute-api.us-east-1.amazonaws.com`)
  - EKS cluster healthy — `kubectl get pods -n mechanics-software` shows ≥ 2 Running
  - RDS reachable from EKS (confirmed by a successful POST earlier)
  - Both Lambda functions deployed: `mechanics-lambda` and `mechanics-lambda-authorizer`
- [ ] Postman open with `MechanicsSoftware.postman_collection.json` imported
  - `baseUrl` variable set to the **API Gateway URL** (not localhost)
  - Do **not** pre-run auth — you'll do it live on camera
- [ ] Browser tabs pre-opened and logged in:
  - GitHub Actions → `mechanics-lambda` — recent successful run
  - GitHub Actions → `mechanics-infra-k8s` — recent successful run
  - GitHub Actions → `mechanics-software` — recent successful deploy run
  - [Datadog public dashboard](https://p.datadoghq.com/sb/e9eb9cda-9e7e-11f1-b0cf-de421ab27ba0-357d7f8569bef16ef90ad5ddd32dbd9c)
  - Component diagram PNG (`docs/images/component.png`)
- [ ] Terminal ready with AWS CLI + kubectl configured for the cluster
- [ ] A customer with a valid CPF already in the database (created via migration seed or earlier POST)
- [ ] Font size ≥ 14 px in VS Code, terminal, and browser
- [ ] Resolution: 1920×1080 minimum

---

## Step-by-step script

---

### Step 1 — Introduction (1 min)

**Show:** webcam + component diagram (`docs/images/component.png`)

**Cover:**
- Group: TorqueOS · FIAP POS Tech · 15SOAT
- What Fase 3 added over Fase 2: 4 separate repositories, serverless CPF authentication via AWS Lambda, managed RDS PostgreSQL (replacing K8s-hosted DB), API Gateway as the single entry point, HPA with min 2 pods, Datadog observability

**Key point to say:**  
*"In Fase 3 we moved from a single repo to a 4-repo structure — each with its own CI/CD pipeline and Terraform state — and added a complete serverless auth layer in front of everything."*

---

### Step 2 — 4-repo architecture walkthrough (1–2 min)

**Show:** GitHub → Torque-OS organization — open each repo briefly

| Repo | What it does |
|------|-------------|
| `mechanics-lambda` | Node.js 20 Lambda — CPF validation + JWT signing (`handler.js`) + JWT verification (`authorizer.js`) |
| `mechanics-infra-k8s` | Terraform — VPC, EKS cluster, API Gateway, VPC Link |
| `mechanics-infra-db` | Terraform — RDS PostgreSQL 16 (db.t3.micro), subnet group, security group |
| `mechanics-software` | ASP.NET Core 8 API — Kubernetes manifests, HPA, initContainer migrations |

**Show briefly in `mechanics-lambda`:** `src/handler.js` — highlight the CPF validation + DB query + JWT signing block (10–15 lines).

**Key point to say:**  
*"The Lambda function issues the JWT. A separate Lambda Authorizer — same zip, different entry point — validates it at the API Gateway layer before any request reaches the EKS cluster."*

---

### Step 3 — CPF authentication — token issuance (2 min)

**Show:** Postman → Auth folder

Run the request live:

```
POST https://<api-gw-url>/auth
Body: { "cpf": "12345678901" }
```

Expected response:
```json
{ "token": "eyJ..." }
```

**Pause and explain:**
1. Request hit API Gateway — no authorizer on this route
2. API Gateway invoked `mechanics-lambda` (handler.js) — not EKS
3. Lambda validated CPF format + check digits in Node.js
4. Lambda queried `customers` table in RDS: `SELECT id, document AS cpf, active FROM customers WHERE document = $1`
5. Customer found and active → signed HS256 JWT → returned token

**Open `src/handler.js`** and show the 3 steps inline in the code.

---

### Step 4 — Protected route via Lambda Authorizer (1–2 min)

**Show:** Postman — paste the token and run a GET request

```
GET https://<api-gw-url>/service-orders
Authorization: Bearer <token>
```

**While the response loads, explain:**
1. API Gateway invoked `mechanics-lambda-authorizer` (authorizer.js) with the token
2. Authorizer verified JWT signature, `iss`, `aud`, `exp` — no DB call
3. Returned `{ isAuthorized: true }` — result cached 300 seconds per token
4. API Gateway injected `X-Gateway-Key` header → forwarded through VPC Link → NLB → EKS pod
5. `GatewayKeyMiddleware` validated the key with `FixedTimeEquals` (constant-time — no timing attacks)
6. `JwtBearer` re-validated the token → authorization policy checked the role → handler ran

Show response: 200 with service orders list.

**Key point to say:**  
*"Two layers of JWT validation — once at the gateway in Lambda, once inside the cluster in ASP.NET. Even if the VPC Link were somehow accessed directly, the Gateway Key check blocks it."*

---

### Step 5 — API lifecycle demo (2–3 min)

**Show:** Postman — run in sequence, keep response panel visible

Token from Step 3 is already set. Run:

| # | Request | Expected status |
|---|---------|-----------------|
| 1 | Create Customer | 201 |
| 2 | Create Vehicle | 201 |
| 3 | Create Service Order | 201 |
| 4 | Start Diagnosis | 200 — `IN_DIAGNOSIS` |
| 5 | Add Service Item | 200 |
| 6 | Generate Budget | 200 |
| 7 | Send Budget to Customer | 200 — `AWAITING_APPROVAL` |
| 8 | Approve Budget | 200 — `IN_EXECUTION` |
| 9 | Complete Service Order | 200 — `COMPLETED` |

Keep it brisk — the full lifecycle was covered in Fase 2. The goal here is to show the stack working end-to-end through API Gateway → Lambda Authorizer → EKS → RDS.

---

### Step 6 — CI/CD pipeline — 3 repos (2 min)

**Show:** GitHub Actions — open 3 tabs

**`mechanics-lambda` pipeline:**  
Click a recent successful run. Walk through steps:
1. `npm install` + `npm test`
2. Zip `src/` directory
3. `aws lambda update-function-code` — both functions from same zip

**`mechanics-infra-k8s` pipeline:**  
Click a recent run. Walk through:
1. `terraform init` → `terraform plan` → `terraform apply`
2. Provisions/updates: VPC, EKS node group, API Gateway HTTP API, VPC Link

**`mechanics-software` pipeline:**  
Click a recent deploy run. Walk through:
1. `docker build` → push to GHCR
2. `kubectl apply -f k8s/` — rolling deploy
3. Show HPA config: min 2, max 10, CPU 70%

**Key point to say:**  
*"Deploy order matters: infra-k8s pass 1 first, then infra-db, then lambda, then mechanics-software, then infra-k8s pass 2 to wire up API Gateway with both Lambda ARNs."*

---

### Step 7 — EKS cluster live state (30 sec)

**Show:** terminal

```bash
kubectl get pods -n mechanics-software
kubectl get hpa -n mechanics-software
```

Show ≥ 2 pods Running. Show HPA with current replicas, CPU%, and min/max.

If cluster is torn down, show the `kubectl get pods` output from the CI run logs.

---

### Step 8 — Datadog — live metrics and structured logs (2 min)

**Show:** browser → [Datadog public dashboard](https://p.datadoghq.com/sb/e9eb9cda-9e7e-11f1-b0cf-de421ab27ba0-357d7f8569bef16ef90ad5ddd32dbd9c)

Walk through the dashboard panels:
- **Daily OS volume** — service orders created per day
- **Average execution time by status** — time in each OS state
- **Error rate** — failed requests in the last hour
- **K8s pod CPU / memory** — node and pod level from Datadog Agent

**Switch to Datadog Logs:**  
Filter by `service:mechanics-software`. Open one log entry and show:
- JSON structure: `timestamp`, `level`, `correlationId`, `message`, `traceId`
- The `correlationId` flowing across a full request (from the `RequestLoggingMiddleware`)

**Show briefly in VS Code:**  
`src/MechanicsSoftware.API/Middleware/RequestLoggingMiddleware.cs` — the correlation ID injection (2–3 lines).

**Key point to say:**  
*"Every request gets a correlation ID at the API Gateway boundary. It flows through the ASP.NET middleware, into every log line, and lands in Datadog — so you can trace a full request with a single filter."*

---

### Step 9 — Closing (30 sec)

**Show:** component diagram PNG

- 4 repositories: `github.com/Torque-OS` → mechanics-software, mechanics-lambda, mechanics-infra-k8s, mechanics-infra-db
- `soat-architecture` collaborator confirmed on all 4 repos
- Documentation: component diagram, sequence diagrams, RFC-001, ADR-007/008/009, ER diagram — all in `docs/` at `mechanics-software`
- Submission document: `docs/entrega-fase3.md`

---

## Timing reference

| Section | Time |
|---------|------|
| Introduction | 1 min |
| 4-repo architecture | 1–2 min |
| CPF auth — token issuance | 2 min |
| Protected route — Lambda Authorizer | 1–2 min |
| API lifecycle demo | 2–3 min |
| CI/CD pipeline (3 repos) | 2 min |
| EKS live state | 30 sec |
| Datadog — metrics + logs | 2 min |
| Closing | 30 sec |
| **Total** | **~12–15 min** |

---

## Recording tips

- Do a full dry run first — confirm API Gateway URL, that both Lambdas respond, and that RDS has a seeded customer with a known CPF
- Keep the Postman response panel maximized — HTTP status code must be visible at all times
- For CI/CD: use a recent successful run, not a live `terraform apply` — it takes too long on camera
- Datadog: pre-filter the Logs view to `service:mechanics-software` before recording; it loads faster
- If the EKS cluster is torn down, show the `kubectl get pods` screenshot from the deploy workflow logs
- VS Code font size ≥ 14 px, high-contrast theme (e.g. One Dark Pro or GitHub Dark)
- Record audio in a quiet environment — narration clarity matters more than visual effects
- The `soat-architecture` confirmation is in `docs/entrega-fase3.md` — show it on screen during the closing

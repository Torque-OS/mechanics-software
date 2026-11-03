# ADR-009: HPA — Autoscaling Horizontal Baseado em CPU

**Status:** Accepted  
**Date:** 2026-07-07  
**Autores:** Joelma Renata Oliveira

---

## Contexto

O cluster EKS usa nodes `t3.small` (2 vCPU, 2 GB RAM) com entre 1 e 3 nodes. A API ASP.NET Core é o único workload relevante no cluster (PostgreSQL foi migrado para RDS na Fase 3).

Para atender picos de carga sem desperdício de recursos em períodos ociosos, é necessário uma política de autoscaling para os pods da API.

O Kubernetes oferece três mecanismos:

1. **HPA (Horizontal Pod Autoscaler)** — escala o número de réplicas com base em métricas.
2. **VPA (Vertical Pod Autoscaler)** — ajusta requests/limits de CPU e memória dos pods existentes.
3. **KEDA (Kubernetes Event-driven Autoscaling)** — escala com base em eventos externos (filas, métricas customizadas).

---

## Decisão

Usar **HPA v2** com métrica de **utilização de CPU**, configurado em `k8s/hpa.yaml`:

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: mechanics-software-api
  namespace: mechanics-software
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: mechanics-software-api
  minReplicas: 2
  maxReplicas: 10
  metrics:
    - type: Resource
      resource:
        name: cpu
        target:
          type: Utilization
          averageUtilization: 70
```

### Justificativa dos parâmetros

| Parâmetro | Valor | Razão |
|---|---|---|
| `minReplicas` | 2 | Alta disponibilidade mínima: se um node falhar ou for drenado, a outra réplica absorve o tráfego sem downtime. |
| `maxReplicas` | 10 | Limite compatível com a capacidade máxima do cluster (3 nodes × ~3 pods por node `t3.small`). Previne scale-out infinito por comportamento anômalo. |
| `averageUtilization` | 70% | Deixa 30% de headroom para picos abruptos antes de o HPA reagir (ciclo de 15 s padrão). |
| Métrica | CPU | Mais simples de configurar; adequada para APIs síncronas onde CPU é o principal gargalo sob carga. |

---

## Alternativas Consideradas

### VPA (Vertical Pod Autoscaler)

O VPA ajusta os `requests` e `limits` de CPU/memória dos pods com base no uso histórico.

**Vantagens:** otimiza utilização de recursos sem aumentar o número de pods; útil quando o bottleneck é memória (ex.: workloads com heap grande).  
**Desvantagens:**
- Requer reinicialização do pod para aplicar novos valores — causa interrupção breve.
- Não instala por padrão no EKS — requer componente adicional (`vpa-admission-controller`, `vpa-recommender`, `vpa-updater`).
- Não escala horizontalmente — um único pod com mais recursos não é equivalente a múltiplos pods para disponibilidade.
- VPA e HPA não podem ser usados simultaneamente na mesma métrica de CPU — conflito de controladores.

### KEDA com métricas customizadas

KEDA permite escalar com base em comprimento de fila SQS, lag de Kafka, throughput de HTTP, etc.

**Vantagens:** escala de zero (sem pods em idle); métricas de negócio mais relevantes que CPU.  
**Desvantagens:**
- Requer instalação de um CRD adicional no cluster (Helm chart do KEDA).
- Requer definição de `ScaledObject` e configuração de métricas externas.
- Scale-to-zero incompatível com `minReplicas: 2` (requisito de HA).
- Sobrecarga de configuração desnecessária para o volume de tráfego do laboratório.

### Sem autoscaling (réplicas fixas)

Manter `replicas: 2` fixo no Deployment, sem HPA.

**Vantagens:** zero complexidade; comportamento previsível.  
**Desvantagens:**
- Sem capacidade de absorver picos — sob carga alta, os pods atingem o limite de CPU e começam a recusar requisições.
- Sem redução de réplicas em baixa carga — desperdício de recursos em períodos ociosos.

### Métrica de memória em vez de CPU

**Vantagens:** útil para workloads memory-bound (ex.: caches em memória, streaming de arquivos).  
**Desvantagens:**
- APIs HTTP síncronas (ASP.NET Core) são tipicamente CPU-bound sob carga.
- Memória não retorna após pico (GC do .NET libera gradualmente) — o HPA baseado em memória pode escalar mas raramente reduz réplicas, desperdiçando nodes.

---

## Consequências

### Positivas

- Alta disponibilidade garantida por `minReplicas: 2` — tolerante a falha de um pod.
- Scale-out automático em até 15 s após CPU ultrapassar 70% de média — sem intervenção manual.
- Scale-in gradual (padrão do HPA: espera 5 min após queda de carga) — evita flapping.
- Compatível com o Cluster Autoscaler do EKS: quando todos os nodes estão cheios, novos nodes são provisionados automaticamente (até `max_size: 3`).

### Negativas / Riscos

- **Latência de reação:** o HPA verifica métricas a cada 15 s (padrão). Picos muito abruptos e curtos podem causar degradação antes do scale-out.
- **Pods em `Pending` durante scale-out:** se o cluster não tiver capacidade para novos pods, eles ficam em `Pending` até o Cluster Autoscaler provisionar um novo node (~2–3 min).
- **Métricas dependem de `metrics-server`:** o `metrics-server` deve estar instalado no cluster (incluso como addon pelo módulo `terraform-aws-modules/eks/aws ~>20.0`).
- **`requests` de CPU obrigatório:** o HPA calcula `averageUtilization` relativo ao `resources.requests.cpu` do container. Se `requests` não estiver definido no Deployment, o HPA não consegue calcular a métrica e permanece inativo.

# ⚡ Guía de Ejecución de Pruebas de Carga en k6 (HU-033 / T-071 & T-072)

Este directorio contiene el script oficial de pruebas de rendimiento y estrés en **k6** para verificar que el tiempo de intercepción y evaluación del motor de riesgo del **Omakase-Gateway** cumpla con el presupuesto de rendimiento de **$\le 50\text{ ms}$ en el percentil 95 (p95)** bajo una carga sostenida de **100 usuarios virtuales (VUs) durante 5 minutos**.

---

## 📋 Estructura de la Prueba (T-071)

- **Escala de Carga**: Ramp-up inicial de 30s hasta **100 VUs**, sostenido durante **5 minutos a 100 VUs concurrentes** (T-071), con 12 IPs distribuidas sintéticas y User-Agents variados.
- **Token JWT Obligatorio**: Requiere pasar `-e JWT_TOKEN="<token>"` para simular peticiones con identidades reales.
- **Configuración Previa del Entorno**:
  Para permitir que el tráfico de 100 VUs llegue al motor de riesgo y no sea bloqueado previamente en la capa exterior de RateLimiting (HU-009), se debe elevar el límite en `appsettings.Development.json`:
  ```json
  "RateLimiting": {
    "Limit": 100000,
    "WindowSeconds": 60
  }
  ```
  *Nota: El middleware de RateLimiting se ejecuta antes del motor de riesgo; elevar el límite permite medir el tiempo propio del pipeline de evaluación en C#.*

---

## 🚀 Formas de Ejecución

### Opción 1: Ejecutar mediante Docker (Recomendado)

#### En Windows (CMD):
```cmd
docker run --rm -i -e GATEWAY_URL="http://host.docker.internal:5219/httpbin/get" -e JWT_TOKEN="<TU_TOKEN_JWT>" -v "%cd%/load-tests:/load-tests" grafana/k6 run /load-tests/k6_stress_test.js
```

---

### Opción 2: Ejecutar de forma Nativa con k6 CLI

#### 1. Instalar k6 vía Windows Package Manager (`winget`):
```powershell
winget install k6 --source winget
```

#### 2. Ejecutar la Prueba:
```cmd
k6 run -e GATEWAY_URL="http://localhost:5219/httpbin/get" -e JWT_TOKEN="<TU_TOKEN_JWT>" load-tests/k6_stress_test.js
```

---

## 📊 Registro de Resultados (T-072)

Los resultados de las métricas capturadas por Grafana/Loki y k6 para el reporte de tesis incluyen:

- **Overhead de Evaluación Interno (C# `Context_DurationMs`)**: Visualizado en el panel de Grafana *"Latencia de Evaluación — SLA: p95 ≤ 50 ms"*.
- **Throughput (Peticiones por Segundo - RPS)**: Medido por k6 en la métrica `http_reqs`.
- **Tasa de Errores (`http_req_failed`)**: Porcentaje de respuestas 5xx o fallos inesperados de red ($< 5\%$).

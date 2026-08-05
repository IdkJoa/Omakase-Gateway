# ⚡ Guía de Ejecución de Pruebas de Carga en k6 (HU-033 / T-071)

Este directorio contiene el script oficial de pruebas de rendimiento y estrés en **k6** para verificar que el tiempo de intercepción y evaluación del motor de riesgo del **Omakase-Gateway** cumpla con el presupuesto de rendimiento de **$\le 50\text{ ms}$ en el percentil 95 (p95)** bajo una carga sostenida de **100 usuarios virtuales (VUs)** durante 5 minutos.

---

## 📋 Estructura de la Prueba

- **Escala de Carga**: Ramp-up progresivo hasta **100 VUs** sostenidos durante 3 minutos, con 20 IPs distribuidas sintéticas y User-Agents variados.
- **Métricas Evaluadas**:
  - `evaluation_overhead_ms` (p95): Latencia de evaluación e intercepción ($\le 50\text{ ms}$).
  - `http_req_failed`: Tasa de fallos de transporte de red ($< 5\%$).

---

## 🚀 Formas de Ejecución (Genéricas para cualquier entorno)

### Opción 1: Ejecutar mediante Docker (Recomendado)
Sin instalar dependencias locales adicionales, ejecutando el contenedor oficial de k6 desde la raíz del proyecto:

#### En Linux / macOS / Bash:
```bash
docker run --rm -i -e GATEWAY_URL="http://host.docker.internal:5219/httpbin/get" -v "$(pwd)/load-tests:/load-tests" grafana/k6 run /load-tests/k6_stress_test.js
```

#### En Windows (CMD):
```cmd
docker run --rm -i -e GATEWAY_URL="http://host.docker.internal:5219/httpbin/get" -v "%cd%/load-tests:/load-tests" grafana/k6 run /load-tests/k6_stress_test.js
```

#### Pasar Token JWT (Opcional para pruebas autenticadas):
```cmd
docker run --rm -i -e GATEWAY_URL="http://host.docker.internal:5219/httpbin/get" -e JWT_TOKEN="<TU_TOKEN_JWT>" -v "%cd%/load-tests:/load-tests" grafana/k6 run /load-tests/k6_stress_test.js
```

---

### Opción 2: Ejecutar de forma Nativa en Windows (k6 CLI)

#### 1. Instalar k6 vía Windows Package Manager (`winget`):
```powershell
winget install k6 --source winget
```

#### 2. Ejecutar la Prueba:
```cmd
k6 run load-tests/k6_stress_test.js
```

#### 3. Pasar variables de entorno opcionales:
```cmd
k6 run -e GATEWAY_URL="http://localhost:5219/httpbin/get" -e JWT_TOKEN="<TU_TOKEN_JWT>" load-tests/k6_stress_test.js
```

---

## 📊 Interpretación de Resultados

Al finalizar los 5 minutos, k6 imprimirá el consolidado de métricas:

- **`evaluation_overhead_ms` (p95)**: Representa el percentil 95 de latencia del motor de riesgo. Debe evaluarse considerando la latencia del servicio proxy de destino (*upstream*).
- **`http_req_failed`**: Refleja la estabilidad del canal HTTP (debe ser $< 5\%$).

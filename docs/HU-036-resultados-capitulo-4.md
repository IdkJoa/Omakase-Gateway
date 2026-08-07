# Capítulo IV §4.2.5 — contenido listo para el documento de tesis

Este archivo traslada al formato de la tesis la evidencia ya medida en el repositorio, para
sustituir las tablas marcadas como «(pendiente)». Cada cifra remite a su origen para que
pueda auditarse.

> **Fuentes.** Latencia y carga: [HU-034/035 §10.6](HU-034-simulacion-de-ataques.md) (corrida del
> 6 de agosto de 2026, 03:05:26 UTC). Detección: [§7.1–7.2](HU-034-simulacion-de-ataques.md).
> Calibración: [§10.4](HU-034-simulacion-de-ataques.md). Degradación:
> `OG.Application.UnitTests/DegradationHandlingTests.cs`.

---

## 1. Tabla 3 — Latencia introducida por la pasarela

**Reemplaza la Tabla 3 actual.** Se recomienda cambiar el encabezado de filas de p50/p95/p99 a
lo realmente medido, en vez de dejar celdas vacías.

| Métrica | Objetivo de diseño | Medido |
|---|---|---|
| Overhead extremo a extremo, p95 (k6) | ≤ 50 ms | **27,72 ms** ✓ |
| Overhead extremo a extremo, p90 | — | 19,27 ms |
| Overhead extremo a extremo, media | — | 13,59 ms |
| Latencia del motor de evaluación, p95 (Loki) | — | 5,28 – 19,47 ms |
| Latencia del motor de evaluación, p50 (Loki) | — | 3,69 – 4,90 ms |
| Peticiones que exceden el presupuesto | — | 0,33 % (660 de 201.217) |
| Rendimiento sostenido | — | 558,9 req/s con 100 VU |

*Nota.* Medición sobre 201.217 evaluaciones con 100 usuarios virtuales concurrentes durante cinco
minutos, sin fallos de transporte. La cifra de k6 mide el recorrido completo —cabeceras de
seguridad, límite de tasa, autenticación, evaluación y reenvío al upstream—, por lo que acota
superiormente el overhead de evaluación que exige el requisito. Elaboración propia.

**Texto sugerido para acompañarla:**

> El presupuesto de latencia se cumple con margen: el percentil 95 extremo a extremo se situó en
> 27,72 ms frente a los 50 ms exigidos. Alcanzarlo, sin embargo, no fue inmediato. La primera
> corrida instrumentada arrojó un p95 de 103,56 ms e incumplió el requisito, lo que obligó a medir
> el coste por fase. El desglose mostró que el 83 % del presupuesto se consumía en operaciones de
> entrada/salida —dos consultas a PostgreSQL por petición para resolver políticas, otra para la
> configuración del motor y una doble resolución del perfil de comportamiento en la misma
> evaluación— mientras que la inferencia del modelo de ML.NET representaba apenas el 16,7 %. Este
> hallazgo es relevante para la discusión: el componente de aprendizaje automático, que a priori
> parecía el candidato natural a ser el cuello de botella, resultó no serlo. Corregidas las
> lecturas repetidas mediante cachés de vigencia breve y una memorización del perfil por petición
> —todas introducidas por decoración, sin modificar las clases existentes—, el percentil 95 bajó a
> 27,72 ms (−73 %) y el rendimiento subió un 23 % con la misma carga.

### ⚠ Dato que falta capturar

**p50 y p99 extremo a extremo.** El documento de HU-035 registra p90, p95 y media, no p50 ni p99.
Dos opciones:

1. **Recomendada:** reportar p50 / p90 / p95 (lo medido) y decirlo en la nota.
2. Volver a correr k6 y capturar el resumen completo (`http_req_duration` trae min, med, p90, p95,
   max). Ver §5 de este documento.

---

## 2. Tabla 4 — Desempeño de la detección

**La Tabla 4 actual no se puede completar tal como está redactada** y conviene sustituirla.
Pide precisión, exhaustividad, F1, tasa de falsos positivos y **AUC**. El AUC y la curva ROC
exigen un clasificador supervisado puntuado sobre un conjunto etiquetado suficientemente grande;
el modelo es **RandomizedPCA, no supervisado**, y el banco documentado tiene nueve casos de una
sola ejecución por escenario. Reportar un AUC en esas condiciones sería inventar una cifra.

**Sustitución propuesta — matriz de confusión del banco de casos etiquetados:**

|  | Predicho: no permitido (BLOCK / CHALLENGE / 423) | Predicho: permitido (ALLOW) |
|---|---|---|
| **Real: malicioso** (n = 6) | 6 (verdaderos positivos) | 0 (falsos negativos) |
| **Real: legítimo** (n = 3) | 0 (falsos positivos) | 3 (verdaderos negativos) |

| Métrica | Valor | Cálculo |
|---|---|---|
| Tasa de detección (sensibilidad) | 100 % | 6 / 6 |
| Tasa de falsos negativos | 0 % | 0 / 6 |
| Tasa de falsos positivos | 0 % | 0 / 3 |
| Precisión del veredicto (exactitud) | 100 % | 9 / 9 |

*Nota.* Resultados de la ejecución controlada de los cinco escenarios de ataque y los casos
legítimos de contraste, con la configuración calibrada (Wp = 0,5; Wa = 0,5; umbral de desafío 33;
umbral de bloqueo 70). Elaboración propia.

**Redacción obligatoria de la limitación** (el propio documento técnico lo advierte y el jurado
lo preguntará):

> Estas cifras corresponden a una ejecución controlada por escenario, no a un muestreo
> poblacional. Deben leerse como evidencia de que el mecanismo discrimina correctamente en los
> casos previstos y como sustento de la calibración, y no como tasas generalizables: con n = 9 el
> intervalo de confianza de una proporción del 100 % es demasiado ancho para afirmar nada sobre
> población. La formulación correcta es «en la ejecución controlada de los cinco escenarios el
> sistema detectó el 100 % de los accesos maliciosos sin producir falsos positivos».

---

## 3. Figura 11 — sustituir la curva ROC

Por lo anterior, **la Figura 11 (curva ROC) debe eliminarse**. En su lugar, la figura que sí
sostiene el argumento es la **separación entre poblaciones**, que es además el dato que justifica
dónde se colocó el umbral:

| Población | Risk Score observado | Origen | n |
|---|---|---|---|
| Legítima (perfil establecido) | 20,75 – 28,25 | banco de casos de HU-034 | 3 |
| Zona de decisión (umbral de desafío) | **33** | `risk_score_config` | — |
| Anómala por volumen | 34,00 – 36,25 | prueba de carga, 100 VU | 201.199 |
| Violación determinista | 75,00 – 78,00 | viaje imposible y combinación | 3 |

*Título sugerido:* **Figura 11.** Separación de poblaciones del Risk Score y ubicación del umbral
de desafío.

Es una figura más honesta y más defendible que una ROC fabricada: muestra un hueco real entre el
tráfico legítimo y el anómalo, y que el umbral se situó en el punto medio de ese hueco y no por
conveniencia.

---

## 4. Tabla 5 — Comportamiento ante fallos (fail-closed)

**Corregir la fila del proveedor de identidad.** La tabla actual dice que un timeout de OIDC debe
denegar el acceso, pero el SRS §3.9 especifica lo contrario: si Keycloak cae, los administradores
no pueden autenticarse pero el flujo de client users —que usa JWT propio— sigue operativo. Es
precisamente el beneficio de la identidad dual, y dejarlo mal contradice el §9.5 del propio SRS.

| Escenario de fallo | Resultado esperado (diseño) | Resultado observado |
|---|---|---|
| Indisponibilidad de Redis | Fail-closed: denegar y registrar | *(pendiente de captura)* |
| Indisponibilidad de PostgreSQL | Fail-closed: denegar y registrar | *(pendiente de captura)* |
| Indisponibilidad del modelo de ML.NET | Degradación controlada: opera solo la capa determinista; Anomaly Score = 50 | *(pendiente de captura)* |
| Timeout del servicio de geolocalización | Degradación controlada: se omite Viaje Imposible y sube el Policy Score base | *(pendiente de captura)* |
| **Indisponibilidad de Keycloak** | **Degradación: los administradores no autentican; el flujo de client users sigue operativo** | *(pendiente de captura)* |
| Redis no responde en la verificación del step-up | Fail-closed: el desafío no puede completarse → BLOCK | *(pendiente de captura)* |
| Client user no interactivo con veredicto CHALLENGE | Escala a BLOCK (no puede completar el segundo factor) | *(pendiente de captura)* |
| Azure Key Vault no responde en el arranque | El sistema no arranca; fallo explícito en logs | *(pendiente de captura)* |

Ver §5 para cómo capturar cada fila.

---

## 5. Capturas que faltan y de qué historia dependen

| # | Qué capturar | HU | ¿Se puede ya? | Cómo |
|---|---|---|---|---|
| 1 | Resumen completo de k6 (p50/p99) y Figura 10 (histograma de latencia) | HU-033 | **Sí** | Levantar el entorno, elevar `RateLimiting:Limit`, correr el script de `load-tests/` y capturar la salida de k6 + el panel de Grafana «Latencia de Evaluación» |
| 2 | Tabla 5, filas de Redis y PostgreSQL | HU-031 | **Sí** | Con el sistema arriba, detener el contenedor y lanzar una petición: capturar el 403 y la fila de `audit_logs` |
| 3 | Tabla 5, filas de ML.NET y geolocalización | HU-032 | **Sí** | Mismo procedimiento; el comportamiento ya está cubierto por `DegradationHandlingTests.cs`, la captura es la evidencia visual |
| 4 | Tabla 5, filas de step-up y client no interactivo | HU-046 | **Sí** | Poner `is_interactive = false` a un client user y forzar una evaluación en zona de desafío |
| 5 | Cobertura de reglas contextuales (variable 4 del §1.6) | HU-037 | **Sí** | Consulta SQL, abajo |
| 6 | Capturas del Dashboard para el Apéndice B | HU-021…HU-027 | **Sí** | Navegar el panel con los datos semilla |
| 7 | Resultados de UAT | HU-040 | **No** | Requiere sesiones con usuarios; si no se harán, ajustar el objetivo específico 4 y el Apéndice B |
| 8 | Manuales de instalación y usuario | HU-041 | **No** | Si no van a existir, quitarlos del Apéndice B |
| 9 | Demo de extremo a extremo del client user | HU-048 | **No** | El cliente de demostración no está en el repositorio |

### Consulta para el indicador de cobertura de reglas (§1.6, variable 4)

Es el único indicador operacionalizado del apartado 1.6 que no tiene dato. Sale de `audit_logs`:

```sql
-- Proporción de peticiones en que se disparó cada tipo de regla
SELECT  regla,
        COUNT(*)                                        AS peticiones,
        ROUND(100.0 * COUNT(*) / (SELECT COUNT(*) FROM audit_logs), 2) AS porcentaje
FROM    audit_logs,
        LATERAL jsonb_array_elements_text(
            CASE WHEN jsonb_typeof(triggered_rules) = 'array'
                 THEN triggered_rules ELSE '[]'::jsonb END) AS regla
GROUP BY regla
ORDER BY peticiones DESC;

-- Número medio de reglas disparadas por petición
SELECT  ROUND(AVG(jsonb_array_length(
            CASE WHEN jsonb_typeof(triggered_rules) = 'array'
                 THEN triggered_rules ELSE '[]'::jsonb END)), 3) AS reglas_por_peticion
FROM    audit_logs;
```

---

## 6. Correcciones de redacción pendientes en el documento

| Ubicación | Problema | Corrección |
|---|---|---|
| Resumen, Abstract, §1.4, §1.5.1 | Listan «límite de tasa» como una de las reglas deterministas del motor. En el SRS y en el código solo hay **cuatro** tipos de política; el límite de tasa es middleware **anterior** al motor | «…y, como control previo a la evaluación, límite de tasa por IP» |
| Tabla 5, fila 4 | Contradice el SRS §3.9 | Ver §4 de este documento |
| §4.2.5, Tabla 4 y Figura 11 | Piden AUC y ROC, imposibles con un modelo no supervisado y n = 9 | Ver §2 y §3 |
| Recomendación 6 y SRS §11 | Citan un «registro de decisión de arquitectura ADR-001» que **no existe** en el repositorio | Escribirlo (una página) o eliminar ambas referencias |
| Agradecimientos y Dedicatoria | Ocho marcadores «(Aquí va…)» sin rellenar | Rellenar |
| §1.4 Limitaciones | Solo recoge limitaciones de alcance | Añadir las limitaciones empíricas medidas (ver abajo) |
| §4.2.4 | Describe cuatro figuras que no estaban insertadas | Insertar las Figuras 6 a 9 de `docs/diagramas/` |

### Limitaciones empíricas que conviene subir al escrito

Están medidas y documentadas; incluirlas fortalece el trabajo en vez de debilitarlo, porque
demuestran que el sistema se sometió a prueba de verdad:

1. **Envenenamiento del baseline.** Las primeras peticiones de una ráfaga se permiten y alimentan
   el perfil, de modo que repetir el mismo ataque lo hace parecer menos anómalo. Medido a lo largo
   de una sesión: el Anomaly Score de la misma ráfaga cayó de 98 a 68,5. Es una vía real de evasión
   por sondeo progresivo, y conviene declararla antes de que la pregunten.
2. **Un modelo conductual por usuario no se puede someter a prueba de estrés.** La característica
   de frecuencia satura por encima de 60 peticiones por hora, así que cualquier carga superior a
   una petición por minuto y por usuario es, por construcción, máximamente anómala para ese
   usuario. No existe «tráfico legítimo de alto volumen» para un usuario cuyo perfil son 40 accesos
   diarios.
3. **El límite de tasa se ejecuta antes del motor.** Una corrida previa demostró que el 94,67 % del
   tráfico moría en el rate limiter sin llegar a evaluarse. Una prueba de estrés que no lo eleve no
   mide el motor, mide el rate limiter.
4. **La geolocalización depende de un servicio externo.** Sin conexión, dos de los cinco escenarios
   de ataque no son ejecutables.
5. **Las cachés introducen una ventana de hasta 5 segundos** para que un cambio administrativo de
   políticas o umbrales surta efecto. Es configurable a 0.
6. **El 0,33 % de cola de latencia procede del entorno de medición** —un solo equipo ejecutando
   PostgreSQL, Redis, Keycloak, Grafana, Loki, Tempo, el colector, dos aplicaciones .NET y el
   generador de carga— y no del sistema.

---

## 7. Dato adicional que vale la pena reportar

La integridad de la auditoría bajo carga no aparece en ninguna tabla de la tesis y es un resultado
fuerte para un sistema cuya premisa es que *cada* petición se evalúa y se audita:

```
k6 envió        : 201.217 peticiones
audit_logs tiene: 201.217 filas
diferencia      : 0
```

Ni un solo evento descartado a 558,9 eventos por segundo, pese a que el canal en memoria está
configurado para descartar bajo presión. Sugerencia: incorporarlo como fila de la Tabla 3 o como
párrafo del §4.2.5.

# Matriz de trazabilidad de requisitos (HU-036 / T-078)

Cruza cada requisito obligatorio de la especificación con su implementación, su evidencia y
su ubicación en el código.

**Cómo leer el estado.** *Cumplido* significa que existe implementación y prueba automatizada
o evidencia registrada. *Cumplido con matiz* significa que el comportamiento está pero la
forma en que se resolvió difiere de lo previsto, y el matiz se explica. *No implementado*
significa exactamente eso, sin adornos: el trabajo se declaró fuera del ciclo o quedó
pendiente, y decirlo es más útil que maquillarlo.

Última revisión: 6 de agosto de 2026, rama `fix/HU-036-HU-048-cierre-sprint5`.

---

## RF-M1 · Gateway de intercepción

| Requisito | Estado | Implementación | Evidencia |
|---|---|---|---|
| Interceptar todo el tráfico HTTP mediante middleware en el pipeline de YARP | Cumplido | `OG.Gateway/Middlewares/RiskEvaluationMiddleware.cs` | 203.964 evaluaciones registradas en la prueba de carga |
| Extraer IP de origen, agente de usuario y marca de tiempo | Cumplido | mismo archivo, construcción de `RequestContext` | Columnas `source_ip`, `user_agent` y `evaluated_at` de `audit_logs` |
| Despachar el comando de evaluación mediante patrón mediador | Cumplido | `OG.Gateway/Common/RiskEngine/Commands/EvaluateRiskHandler.cs` | `EvaluateRiskHandlerTests` |
| Reenrutar al upstream solo tras veredicto aprobatorio | Cumplido | `RiskEvaluationMiddleware`, rama de `Verdict.Allow` | Paso 1 del guion de demostración |
| Hidratar las rutas dinámicamente desde `protected_services` | Cumplido | `Infrastructure/Proxy/DatabaseProxyConfigProvider.cs`, `ProxyConfigReloader.cs` | `DatabaseProxyConfigProviderTests`, `ProxyConfigReloaderTests` |
| Escribir la auditoría de forma asíncrona, fuera de la ruta crítica | Cumplido | `OG.Gateway/Common/Audit/InMemoryAuditChannel.cs` | Ningún evento descartado en 203.964 peticiones a 566 por segundo |
| Soporte de `X-Forwarded-For` tras balanceadores | Cumplido | `UseForwardedHeaders` en `OG.Gateway.Api/Program.cs` | Las doce IP sintéticas del guion de carga se resuelven por separado |
| Límite de tasa por IP | Cumplido | `OG.Gateway/Middlewares/RateLimitMiddleware.cs` | `RateLimitMiddlewareTests` |

---

## RF-M2 · Motor de reglas deterministas

| Requisito | Estado | Implementación | Evidencia |
|---|---|---|---|
| Regla de geofencing | Cumplido | `OG.Gateway/Common/RiskEngine/Rules/GeofenceRuleEvaluator.cs` | `GeofenceRuleEvaluatorTests`; escenario 5 del banco de casos |
| Regla de ventana horaria | Cumplido | `TimeWindowRuleEvaluator.cs` | `TimeWindowRuleEvaluatorTests` |
| Regla de huella de dispositivo | Cumplido | `FingerprintRuleEvaluator.cs` | `FingerprintRuleEvaluatorTests`; escenario 3 |
| Regla de viaje imposible con distancia de Haversine | Cumplido | `ImpossibleTravelRuleEvaluator.cs` | `ImpossibleTravelRuleEvaluatorTests`; escenario 1 |
| Puntaje de política ponderado | Cumplido con matiz | `Scoring/PolicyScoreCalculator.cs` | `PolicyScoreCalculatorTests` |
| Asociación de políticas por servicio | Cumplido | Tabla `service_policies`; `OG.Dashboard.Api/Endpoints/ServicePoliciesEndpoints.cs` | `DemoDataSeederTests.AsociaConjuntosDeReglasDistintosACadaServicio` |
| Persistencia asíncrona de las reglas disparadas | Cumplido | `EvaluateRiskHandler.BuildTriggeredRules` | Columna `triggered_rules` de `audit_logs` |

**Matiz del puntaje de política.** El diseño original promediaba las reglas ponderadas. Medido
contra el banco de casos, ese promedio producía un comportamiento invertido: añadir políticas
que no disparaban diluía la violación de la que sí lo hacía, de modo que un servicio mejor
protegido resultaba más permisivo. Se sustituyó por el máximo de las violaciones ponderadas,
con lo que manda la infracción más grave y sumar reglas nunca debilita la detección. El
cambio y su medición están en la sección 11 de `HU-034-simulacion-de-ataques.md`.

---

## RF-M3 · Detección de anomalías

| Requisito | Estado | Implementación | Evidencia |
|---|---|---|---|
| Modelo no supervisado con RandomizedPCA de ML.NET | Cumplido | `Infrastructure/AnomalyDetection/RandomizedPcaAnomalyDetector.cs` | `RandomizedPcaAnomalyDetectorTests` |
| Vector reducido de características justificables | Cumplido | `FeatureExtractor`: hora en seno y coseno, frecuencia, diversidad | `FeatureExtractorTests` |
| Aprendizaje del perfil histórico por usuario | Cumplido | `ProfileUpdateWorker.cs`, tabla `user_behavior_profiles` | `BehaviorProfileProjectionTests` |
| Exposición del puntaje de anomalía | Cumplido | Columna `anomaly_score` de `audit_logs` | En la corrida de carga, solo 5 de 203.964 devolvieron el valor neutro de reserva |
| Generador de tráfico sintético para el baseline | Cumplido | `SyntheticTrafficGenerator.cs`, `BehaviorBaselineBootstrapper.cs` | `SyntheticTrafficGeneratorTests`, `BehaviorBaselineBootstrapperTests` |
| Reentrenamiento periódico | Cumplido | `AnomalyRetrainWorker.cs` | `AnomalyModelCacheTests` |

---

## RF-M4 · Panel de administración

| Requisito | Estado | Implementación | Evidencia |
|---|---|---|---|
| Gestión de políticas y su asociación a servicios | Cumplido | `OG.Dashboard.Api/Endpoints/PoliciesEndpoints.cs`, `ServicePoliciesEndpoints.cs` | `PolicyConfigValidatorTests` |
| Explorador de logs con justificación del puntaje | Cumplido | `AuditLogsEndpoints.cs` | 60 evaluaciones sembradas al arrancar |
| Métricas de riesgo | Cumplido | `MetricsEndpoints.cs` | `GetMetricsSummaryHandlerTests` |
| Perfil de comportamiento por usuario | Cumplido | `UserProfileEndpoints.cs` | `UserProfileSerializerTests` |
| Editor de pesos y umbrales sin redespliegue | Cumplido | `RiskConfigEndpoints.cs` | Los valores calibrados se aplican leyendo `risk_score_config` |
| Autenticación del panel mediante Keycloak | Cumplido | `Infrastructure/Security/KeycloakCurrentUserService.cs` | `KeycloakCurrentUserServiceTests` |

---

## RF-M5 · Observabilidad

| Requisito | Estado | Implementación | Evidencia |
|---|---|---|---|
| Instrumentación con OpenTelemetry | Cumplido | `ServiceDefaults/Extensions.cs`, `OmakaseActivity` | Trazas por etapa en Tempo |
| Métricas operativas exportadas a Grafana | Cumplido | `AppHost/config/grafana/` | Panel *Latencia de Evaluación* |
| Logs estructurados hacia Loki | Cumplido | Serilog configurado en `OG.Gateway.Api/Program.cs` | Percentiles del motor obtenidos de Loki |
| Orquestación con .NET Aspire | Cumplido | `AppHost/AppHost.cs` | `aspire run` levanta el ecosistema completo |

---

## RF-M6 · Autenticación y gestión de identidad

| Requisito | Estado | Implementación | Evidencia |
|---|---|---|---|
| Administradores con Keycloak, código de autorización y PKCE | Cumplido | `Infrastructure/Security/AuthenticationExtensions.cs` | Realm versionado en `AppHost/config/keycloak/` |
| Usuarios interceptados con token propio | Cumplido | `GatewayTokenService.cs`, `LoginService.cs` | `LoginServiceTests` |
| Token de acceso de 15 minutos y de refresco de 7 días | Cumplido | `JwtOptions`, tabla `refresh_tokens` | `LoginServiceTests` |
| Refresco entregado en cookie HttpOnly, Secure y SameSite estricto | Cumplido | `OG.Gateway.Api/Endpoints/AuthEndpoints.cs` | El cliente de demostración se sirve del mismo origen precisamente por esto |
| Revocación mediante lista negra en Redis | Cumplido | `Infrastructure/Persistence/Redis` | `RedisStoresTests` |
| Rotación del token de refresco en cada renovación | Cumplido | `LoginService.RefreshAsync` | `LoginServiceTests` |
| Contraseñas con bcrypt de factor mayor o igual a 12 | Cumplido | `LoginService`, `OmakaseDbSeeder`, `DemoDataSeeder` | Factor 12 explícito en las tres |
| Bloqueo de cuenta tras cinco intentos fallidos | Cumplido | `LoginService` | `LoginServiceTests` |
| Segundo factor por TOTP para usuarios interceptados | Cumplido | `OG.Gateway.Api/Endpoints/MfaEndpoints.cs`, `TotpService` | `TotpServiceTests`, `MfaStoresTests`, `EvaluateRiskHandlerStepUpTests` |
| Secreto TOTP cifrado en reposo | Cumplido | `AesGcmTotpSecretProtector.cs` | `AesGcmTotpSecretProtectorTests` |
| Escalada a bloqueo de las cuentas no interactivas | Cumplido | `EvaluateRiskHandler`, `users.is_interactive` | `EvaluateRiskHandlerStepUpTests`; cuenta `svc.integracion` sembrada |
| Realm de Keycloak versionado e importado al arrancar | Cumplido | `AppHost/config/keycloak/realm-export.json` | Se importa solo al levantar el contenedor |

---

## RF-M7 · Persistencia y modelo de datos

| Requisito | Estado | Implementación | Evidencia |
|---|---|---|---|
| Diez entidades en PostgreSQL | Cumplido | `Domain/Entities/`, `Infrastructure/Configurations/` | Figura 8 del capítulo IV |
| RBAC normalizado en `roles` y `user_roles` | Cumplido | `Role.cs`, `UserRole.cs` | `RolesHandlerTests` |
| Índices en `audit_logs` por fecha y por usuario | Cumplido | `AuditLogConfiguration.cs` | Índice GIN sobre `triggered_rules` incluido |
| Auditoría inmutable | Cumplido con matiz | Sin operaciones de actualización ni borrado en el código | El carácter de solo anexado se sostiene por diseño de la aplicación, no por una restricción declarada en la base |
| Estado efímero en Redis con sus tiempos de vida | Cumplido | `Infrastructure/Persistence/Redis` | `RedisStoresTests`, `MfaStoresTests` |

---

## RF-M8 · Seguridad del panel

| Requisito | Estado | Implementación | Evidencia |
|---|---|---|---|
| Autenticación en todos los endpoints administrativos | Cumplido | `RequireAuthorization` en los endpoints de `/api/v1` | `RbacAuthorizationMiddlewareTests` |
| Control de acceso basado en roles contra la base | Cumplido | `Infrastructure/Security/RbacAuthorizationMiddleware.cs` | El usuario `viewer` recibe 403 en escritura |
| Cabeceras de seguridad obligatorias | Cumplido | `Infrastructure/Security/SecurityHeadersMiddleware.cs` | Las cinco cabeceras del SRS §3.8 |
| Prevención de inyección CRLF en los registros | Cumplido | `ILogSanitizer`, `CrlfLogSanitizer` | `LogSanitizerTests` |
| Codificación de salidas contra XSS | Cumplido | `IOutputSanitizer` aplicado en los DTO del panel | `RolesHandlerTests` cubre la ruta de creación |

---

## RF-M9 · Manejo de errores y degradación

| Requisito | Estado | Implementación | Evidencia |
|---|---|---|---|
| Degradación controlada ante fallo de geolocalización | Cumplido | `EvaluateRiskHandler`, incremento de 15 puntos | `DegradationHandlingTests`; 52 evaluaciones observadas en ejecución, todas con puntaje de política de 15,00 y ninguna denegación |
| Degradación controlada ante fallo del modelo | Cumplido | `EvaluateRiskHandler`, puntaje neutro de 50 | `DegradationHandlingTests` |
| Escalada a bloqueo si el almacén de step-up no responde | Cumplido | `EvaluateRiskHandler`, `Infrastructure/Redis/StepUpStore.cs` | `EvaluateRiskHandlerStepUpTests`, `MfaStepUpFailClosedTests` |
| Fail-closed ante caída de Redis o PostgreSQL | Cumplido | `Infrastructure/Resilience/DependencyCircuitBreaker.cs` y su middleware | `DependencyCircuitBreakerTests`, `DependencyCircuitBreakerMiddlewareTests`, `ResilientRedisServiceTests` |
| Fallo explícito al arrancar sin Key Vault | Cumplido | `Infrastructure/KeyVault/KeyVaultStartupValidator.cs` | `KeyVaultStartupValidatorTests` |

**Sobre la degradación segura ante dependencias críticas.** Se cerró con la integración de
HU-031. El interruptor de circuito abre tras tres fallos consecutivos de Redis o de
PostgreSQL y el middleware traduce esa apertura en un 503, de modo que la pasarela deniega en
vez de conceder sin verificar. El acceso a Redis pasa por un decorador que enruta cada
operación a través del interruptor, sin modificar el servicio original. En el arranque, el
validador de Key Vault comprueba que la clave de firma exista y sea utilizable, y aborta el
inicio si falta, si es un marcador de posición o si el proveedor no responde; en desarrollo,
sin URI configurada, la validación se omite deliberadamente.

---

## Requisitos no funcionales

| Requisito | Estado | Evidencia |
|---|---|---|
| Overhead de evaluación menor o igual a 50 ms en el percentil 95 | Cumplido | 20,82 ms sobre 203.964 evaluaciones; el percentil 99 también queda por debajo, en 44,88 ms |
| Consulta de estado contextual por debajo de 5 ms | Cumplido | Redis en memoria; el desglose por fase lo confirma |
| Escalabilidad horizontal de la pasarela | Cumplido por diseño | Todo el estado compartido reside en Redis y PostgreSQL; no se ejecutó una prueba con varias réplicas |
| Auditoría sin pérdida bajo carga | Cumplido | 203.964 peticiones y 203.964 filas registradas |

---

## Resultados de las pruebas de estrés

Corrida del 6 de agosto de 2026: cien usuarios virtuales concurrentes durante cinco minutos
contra un servicio de destino local, con el límite de tasa elevado para que la carga alcanzara
el motor en lugar de rebotar en el limitador.

| Métrica | Valor |
|---|---|
| Evaluaciones | 203.964 |
| Rendimiento sostenido | 566,5 peticiones por segundo |
| Percentil 50 | 9,56 ms |
| Percentil 90 | 14,56 ms |
| Percentil 95 | 20,82 ms |
| Percentil 99 | 44,88 ms |
| Peticiones fallidas | 0,00 % |
| Eventos de auditoría descartados | 0 |

El percentil 99 queda por debajo del presupuesto de 50 ms, de modo que el requisito se cumple
para el 99 % del tráfico y no solo para el 95 % exigido. El procedimiento completo está en
[`load-tests/README.md`](../load-tests/README.md).

La primera corrida instrumentada incumplió el presupuesto con un percentil 95 de 103,56 ms. El
desglose por fase mostró que el 83 % del tiempo se consumía en entrada y salida, mientras que
la inferencia del modelo representaba el 16,7 %. Corregidas las lecturas repetidas mediante
cachés de vigencia breve y una memorización del perfil por petición, la latencia bajó un orden
de magnitud. El análisis está en la sección 10.6 de
[`HU-034-simulacion-de-ataques.md`](HU-034-simulacion-de-ataques.md).

## Resultados de las simulaciones de ataque

Ejecución controlada de los cinco escenarios del banco de casos etiquetados, con la
configuración calibrada.

| Escenario | Regla que debía disparar | Resultado |
|---|---|---|
| Viaje imposible entre dos países en pocos minutos | Viaje imposible | Detectado |
| Ráfaga en horario atípico | Anomalía de conducta | Detectado |
| Huella de dispositivo desconocida | Huella de dispositivo | Detectado |
| Fuerza bruta de credenciales | Bloqueo de cuenta | Detectado |
| Combinación de factores | Varias | Detectado |

| Métrica | Valor |
|---|---|
| Verdaderos positivos | 6 de 6 |
| Falsos negativos | 0 |
| Falsos positivos | 0 de 3 casos legítimos |
| Exactitud del veredicto | 9 de 9 |

Estas cifras corresponden a una ejecución controlada por escenario y no a un muestreo
poblacional: con nueve casos no sostienen una afirmación sobre población, sino que evidencian
que el mecanismo discrimina en los casos previstos y sustentan la calibración adoptada. El
detalle de cada escenario, con sus precondiciones y sus veredictos, está en
[`HU-034-simulacion-de-ataques.md`](HU-034-simulacion-de-ataques.md).

## Evidencia del flujo de confianza cero de extremo a extremo

Recorrido completo registrado en `audit_logs` durante la validación del cliente de
demostración, que es la prueba de que las tres decisiones del motor funcionan en conjunto.

| Evaluación | Puntaje | Veredicto | Regla registrada |
|---|---|---|---|
| Petición desde país denegado | 73,75 | BLOCK | `GEOFENCE` |
| Petición desde dispositivo no reconocido | 35,50 | CHALLENGE | `FINGERPRINT` |
| Verificación de segundo factor fallida | — | — | `MFA_FAILED` |
| Reintento tras verificar el segundo factor | 35,50 | ALLOW | `MFA_SATISFIED` |

Las dos últimas filas merecen atención: **el mismo puntaje de 35,50 produjo desafío la primera
vez y acceso la segunda**, porque la ventana de step-up estaba vigente. Es la demostración de
que el segundo factor no reduce el riesgo sino que autoriza continuar pese a él, de forma
acotada en el tiempo y auditada, tal como establece el SRS §9.9.

---

## Trabajo del ciclo que no se completó

Se declara aquí para que la matriz no dé una impresión de cobertura total.

| Elemento | Historia | Situación |
|---|---|---|
| El panel de Angular no lo levanta `aspire run` | HU-036 | Vive en otro repositorio; el README documenta cómo arrancarlo al lado |
| Suite de pruebas de integración extremo a extremo | HU-039 | No se creó el proyecto de pruebas de integración |
| Pruebas de aceptación de usuario | HU-040 | No se ejecutaron sesiones |
| Manuales de instalación y de usuario | HU-041 | El README cubre la instalación; el manual del panel no se redactó |

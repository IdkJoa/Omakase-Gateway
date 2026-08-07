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
| Escalada a bloqueo si el almacén de step-up no responde | Cumplido | `EvaluateRiskHandler` | `EvaluateRiskHandlerStepUpTests` |
| **Fail-closed ante caída de Redis o PostgreSQL** | **No implementado** | Rama `feat/HU-031_safe-downgrade`, sin integrar | HU-031 no se completó |
| **Fallo explícito al arrancar sin Key Vault** | **No implementado** | Misma rama | HU-031 no se completó |

**Sobre HU-031.** Es el hueco conocido y conviene no disimularlo. El interruptor de circuito
que devuelve 503 ante la caída de Redis o PostgreSQL, y el validador que impide arrancar sin
los secretos, están escritos en `feat/HU-031_safe-downgrade` junto con sus pruebas, pero esa
rama nunca se integró. Mientras no se integre, la política de degradación segura del SRS §9.4
está cubierta para las dependencias no críticas y no para las críticas.

---

## Requisitos no funcionales

| Requisito | Estado | Evidencia |
|---|---|---|
| Overhead de evaluación menor o igual a 50 ms en el percentil 95 | Cumplido | 20,82 ms sobre 203.964 evaluaciones; el percentil 99 también queda por debajo, en 44,88 ms |
| Consulta de estado contextual por debajo de 5 ms | Cumplido | Redis en memoria; el desglose por fase lo confirma |
| Escalabilidad horizontal de la pasarela | Cumplido por diseño | Todo el estado compartido reside en Redis y PostgreSQL; no se ejecutó una prueba con varias réplicas |
| Auditoría sin pérdida bajo carga | Cumplido | 203.964 peticiones y 203.964 filas registradas |

---

## Trabajo del ciclo que no se completó

Se declara aquí para que la matriz no dé una impresión de cobertura total.

| Elemento | Historia | Situación |
|---|---|---|
| El panel de Angular no lo levanta `aspire run` | HU-036 | Vive en otro repositorio; el README documenta cómo arrancarlo al lado |
| Fail-closed ante dependencias críticas | HU-031 | Implementado en rama, sin integrar |
| Suite de pruebas de integración extremo a extremo | HU-039 | No se creó el proyecto de pruebas de integración |
| Pruebas de aceptación de usuario | HU-040 | No se ejecutaron sesiones |
| Manuales de instalación y de usuario | HU-041 | El README cubre la instalación; el manual del panel no se redactó |

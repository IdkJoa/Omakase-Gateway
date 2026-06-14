# PLANIFICACION_HU — Omakase-Gateway
**Tech Lead:** Rafael | **Sprint 1** | **Junio 2026**

---

## 1. Resumen de Arquitectura

```
┌─────────────────────────────────────────────────────────────────┐
│                        OMAKASE-GATEWAY                          │
│                  Zero Trust Access Control Platform             │
└─────────────────────────────────────────────────────────────────┘

┌──────────────────────┐    ┌──────────────────────┐
│   OG.Gateway.Api     │    │  OG.Dashboard.Api    │
│  (Minimal APIs/YARP) │    │  (REST Admin API)    │
│   :8080 / :8443      │    │   :8090 / :8444      │
└──────────┬───────────┘    └──────────┬───────────┘
           │                           │
           └──────────┬────────────────┘
                      │
         ┌────────────▼────────────┐
         │      OG.Gateway         │   ← Application layer (Risk Engine,
         │    OG.Dashboard         │     YARP config, Features, CQRS)
         └────────────┬────────────┘
                      │
         ┌────────────▼────────────┐
         │      Infrastructure     │   ← EF Core, DbContext, Seeding,
         │                         │     Redis, GeoLocation, Key Vault
         └────────────┬────────────┘
                      │
         ┌────────────▼────────────┐
         │         Domain          │   ← Entidades puras (DDD),
         │                         │     Value Objects, Typed IDs
         └─────────────────────────┘

Soporte transversal:
 ├── AppHost/         ← .NET Aspire orchestration   [JOAQUÍN]
 ├── ServiceDefaults/ ← OpenTelemetry, health checks [JOAQUÍN]
 └── OG.Application.UnitTests/ ← xUnit + coverlet
```

### Base de datos (PostgreSQL — 10 entidades)
| Entidad | Propósito |
|---|---|
| `users` | Identidad dual: SECURITY_OFFICER y CLIENT_USER |
| `roles` | Catálogo normalizado de roles (ADMIN, VIEWER, ...) |
| `user_roles` | N:M → usuarios ↔ roles (RBAC) |
| `access_policies` | Reglas deterministas configurables |
| `service_policies` | N:M → servicios ↔ políticas |
| `protected_services` | Catálogo de upstreams (fuente de rutas YARP) |
| `user_behavior_profiles` | Perfiles ML.NET por usuario (1:1) |
| `audit_logs` | Registro inmutable append-only de evaluaciones |
| `refresh_tokens` | Tokens de refresco persistidos (TTL 7 días) |
| `risk_score_config` | Fila global única: pesos y umbrales del Risk Score |

---

## 2. División del Equipo (Sprint 1)

| Compañero | HU(s) | Archivos/Áreas que toca |
|---|---|---|
| **Joaquín** | HU-001 (Docker), HU-002 (Aspire), HU-004 (Migraciones PostgreSQL), HU-007 (OpenTelemetry) | `docker-compose.yml`, `Dockerfile`, `AppHost/AppHost.cs`, `ServiceDefaults/Extensions.cs`, `Infrastructure/Migrations/` |
| **Juan David** | HU-038 (Dashboard UI/UX, Figma, Angular scaffold) | Carpeta Angular (aún no existe), wireframes |
| **Joel** | HU-005 (Redis — IRedisService con TTLs) | `Infrastructure/Redis/` |
| **Rafael (yo)** | **HU-003** (CI/CD), **HU-006** (Seed Data) | `azure-pipelines.yml` (raíz), `Infrastructure/Persistence/Seeding/` |

> **Regla de oro:** Rafael no toca `AppHost/`, `ServiceDefaults/`, `Infrastructure/Migrations/`, `Infrastructure/Redis/`, ni ningún archivo Angular.

---

## 3. HU-003 — Pipeline CI/CD con Azure Pipelines

### Datos de la historia
| Campo | Valor |
|---|---|
| Épica | EP-01 — Infraestructura y DevOps |
| Sprint | 1 (4–17 jun 2026) |
| Story Points | 3 |
| Prioridad | High |
| Etiqueta | `devops` |

### Rama de trabajo
```bash
git checkout -b feature/HU-003-Pipeline-CICD
```

### Archivo a crear
**`azure-pipelines.yml`** — en la raíz del repositorio.

### Diseño del pipeline

```
TRIGGER
 ├── push → main
 └── pull_request → main

STAGE: CI
 └── JOB: build-test-sast
      ├── STEP 1: Instalar .NET 10 SDK
      ├── STEP 2: dotnet restore
      ├── STEP 3: dotnet build --configuration Release --no-restore
      │           (Roslyn analyzers corren aquí → SAST implícito)
      ├── STEP 4: dotnet test --no-build --configuration Release
      │           --collect:"XPlat Code Coverage"
      └── STEP 5: (condicional) npm ci && npm run build
                  (solo cuando exista el proyecto Angular)
```

### Criterios de aceptación (del backlog)
- [ ] Push a `main` o apertura de PR dispara el pipeline
- [ ] `dotnet build` compila el Gateway (.NET 10) sin errores
- [ ] `dotnet test` ejecuta las pruebas unitarias
- [ ] Analizadores de Roslyn (SAST) reportan resultado en el PR
- [ ] El resultado aparece en Azure DevOps / GitHub Checks

### Interferencia con compañeros
**Ninguna.** El archivo `azure-pipelines.yml` es nuevo y no modifica ningún archivo existente ni entra en conflicto con el trabajo de Joaquín, Joel ni Juan David.

---

## 4. HU-006 — Datos Semilla (Seed Data)

### Datos de la historia
| Campo | Valor |
|---|---|
| Épica | EP-02 — Persistencia y Modelo de Datos |
| Sprint | 1 (4–17 jun 2026) |
| Story Points | 2 |
| Prioridad | High |
| Tarea | T-013 |
| Etiqueta | `persistence` |

### Rama de trabajo
```bash
git checkout -b feature/HU-006-Seed-Data
```

### Archivos a crear y modificar

| Archivo | Acción | Propósito |
|---|---|---|
| `Infrastructure/Persistence/Seeding/IDbSeeder.cs` | **CREAR** | Interfaz (principio O/C) |
| `Infrastructure/Persistence/Seeding/OmakaseDbSeeder.cs` | **CREAR** | Implementación del seeder |
| `Infrastructure/DependencyInjection.cs` | **MODIFICAR** | Registrar `IDbSeeder → OmakaseDbSeeder` |
| `OG.Gateway.Api/Program.cs` | **MODIFICAR** | Llamar al seeder en startup |

> Los archivos de Joaquín (`AppHost/`, `Migrations/`) no se tocan. El seeder es idempotente: usa `AnyAsync()` antes de insertar, por lo que puede correr N veces sin duplicar datos.

### Datos a sembrar

#### `risk_score_config` (fila única global)
```
policy_weight    = 0.600  (Wp — peso capa determinista)
anomaly_weight   = 0.400  (Wa — peso capa IA)
allow_threshold  = 40     (≤ 40 → ALLOW)
challenge_threshold = 75  (41–75 → CHALLENGE; > 75 → BLOCK)
cold_start_penalty  = 30.0 (penalización para usuarios nuevos)
cold_start_n     = 10     (accesos para extinguir penalización)
updated_at       = now()
```
*Fórmula:* `RiskScore = 0.6 × PolicyScore + 0.4 × AnomalyScore + ColdStartPenalty`
*ColdStartPenalty:* `30 × max(0, 1 − access_count / 10)`

#### `roles` (catálogo inicial extensible)
```
ADMIN  — Acceso completo: lectura + escritura de toda la plataforma
VIEWER — Solo lectura: logs, métricas, perfiles (sin modificar config)
```

#### `users` (administrador inicial)
```
user_type    = SECURITY_OFFICER
username     = leído de configuración (appsettings / env var)
keycloak_sub = leído de configuración (placeholder hasta HU-018)
is_active    = true
failed_attempts = 0
```
> La contraseña NO se establece aquí (SECURITY_OFFICER usa Keycloak, no bcrypt).

### Diseño de clases (principio Open/Closed)

```csharp
// IDbSeeder.cs — contrato cerrado para modificación, abierto para extensión
public interface IDbSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

// OmakaseDbSeeder.cs — implementación concreta
// Inyecta: OmakaseDbContext, IConfiguration, ILogger
// Lógica: verificar con AnyAsync() antes de insertar
//         transacción única para atomicidad
//         log de cada recurso sembrado o ya existente
```

### Flujo de ejecución en startup

```
Program.cs (OG.Gateway.Api)
  └─→ builder.Services.AddInfrastructure(config)   ← en DependencyInjection.cs
        └─→ services.AddScoped<IDbSeeder, OmakaseDbSeeder>()

  └─→ await app.SeedDatabaseAsync()                 ← extension method
        └─→ scope.ServiceProvider.GetRequiredService<IDbSeeder>()
              └─→ seeder.SeedAsync(ct)
```

### Dependencia técnica con HU-004
El seeder **requiere que las migraciones de Joaquín (HU-004) hayan corrido** antes de ejecutarse. Durante el Sprint 1 trabajamos en paralelo; el seeder arroja una excepción descriptiva si las tablas no existen, lo cual es el comportamiento correcto.

### Criterios de aceptación (del backlog)
- [ ] `risk_score_config` tiene exactamente 1 fila con los valores por defecto
- [ ] `roles` contiene ADMIN y VIEWER (con `is_active = true`)
- [ ] Existe un usuario administrador inicial de tipo `SECURITY_OFFICER`
- [ ] El seeder es idempotente: ejecutarlo dos veces no duplica datos
- [ ] Si la BD está vacía, el seed completa en < 1 segundo
- [ ] Cada recurso sembrado queda registrado en el log de startup

---

## 5. Reglas de Ejecución (recordatorio)

| Regla | Detalle |
|---|---|
| **Ramas** | Crear rama antes de tocar cualquier archivo: `git checkout -b feature/HU-[N]-[Nombre]` |
| **SOLID — O/C** | Interfaces para todo comportamiento extensible. No modificar clases existentes para agregar comportamiento nuevo. |
| **SOLID — SRP** | `OmakaseDbSeeder` solo siembra datos. `DependencyInjection` solo registra servicios. |
| **PAUSA obligatoria** | Después de presentar código, esperar luz verde de Rafael antes de cualquier acción git. |
| **Sin git add/commit/push** | Rafael ejecuta y prueba en local. Git lo maneja él cuando esté listo. |
| **Sin interferencia** | Nunca tocar archivos de Joaquín (Docker, Aspire, Migrations, OpenTelemetry), Joel (Redis) ni Juan David (Angular). |

---

## 6. Orden de trabajo recomendado

```
PASO 1 ── Crear rama feature/HU-003-Pipeline-CICD
PASO 2 ── Escribir y revisar azure-pipelines.yml
PASO 3 ── [PAUSA] Rafael revisa y da luz verde
PASO 4 ── Merge / cerrar HU-003

PASO 5 ── Crear rama feature/HU-006-Seed-Data
PASO 6 ── Crear IDbSeeder.cs
PASO 7 ── Crear OmakaseDbSeeder.cs
PASO 8 ── Actualizar DependencyInjection.cs
PASO 9 ── Actualizar OG.Gateway.Api/Program.cs
PASO 10 ─ [PAUSA] Rafael prueba con dotnet run (requiere HU-004 de Joaquín)
PASO 11 ─ Merge / cerrar HU-006
```

---

*Generado como Tech Lead · Proyecto Final TDS · ITLA · Junio 2026*

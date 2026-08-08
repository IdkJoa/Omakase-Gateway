# Pendientes antes de la defensa

Lista consolidada de todo lo detectado durante la revisión de cierre de la tesis
(6 y 7 de agosto de 2026), contrastando el documento contra el código de ambos repositorios.

**Nada de esta lista está en la memoria.** La tesis quedó redactada describiendo el sistema
tal como funciona; estos son los arreglos que hay que hacer en el código y en los entregables
para que la demostración del 14 de agosto salga sin sobresaltos.

Orden: primero lo que puede romperse delante del jurado.

---

## P0 — Puede fallar durante la demostración

### 1. Las plantillas de política del panel no casan con el motor

**Repo:** frontend · **Archivo:** `src/app/feature/policies/components/policies-form.component/policies-form.component.ts` (líneas 37-40)

El motor lee las claves de configuración en `snake_case`; el formulario las propone en
`camelCase`. Quien cree una política siguiendo la plantilla que el propio panel sugiere, falla.

| Tipo | Plantilla del panel | Lo que lee el motor | Efecto |
|---|---|---|---|
| `Timewindow` | `startHour`, `endHour`, `daysOfWeek` | `start_time`, `end_time`, `timezone` (strings) | **400 al guardar.** Ninguna política de ventana horaria se puede crear desde el panel |
| `Geofence` | `allowedCountries`, `denied_countries` | `allowed_countries`, `denied_countries` | **Se guarda a medias.** Pasa la validación por la segunda clave; la lista de permitidos se ignora |

Corrección:

```ts
readonly defaultConfigs: Record<string, string> = {
  Geofence: JSON.stringify({ allowed_countries: ['DO', 'US'], denied_countries: ['RU', 'CN'] }, null, 2),
  Timewindow: JSON.stringify({ start_time: '08:00', end_time: '18:00', timezone: 'America/Santo_Domingo' }, null, 2),
  Fingerprint: JSON.stringify({}, null, 2),
  impossibleTravel: JSON.stringify({}, null, 2),
};
```

Arrastra dos correcciones más:
- `policies-form.component.html` línea 102: el `placeholder` repite `allowedCountries`.
- `src/app/feature/policies/interfaces/policies.interface.ts`: la interfaz `Config` declara
  `startHour`, `endHour`, `daysOfWeek`, `maxSpeedKmh` y `maxDevicesPerSession`. **Ninguna de
  las cinco existe en el backend.**

Verificado contra `TimeWindowRuleEvaluator.cs` (42-47), `GeofenceRuleEvaluator.cs` (54-55, 80-91),
`TimeWindowConfigValidator.cs`, `GeofenceConfigValidator.cs` y `PoliciesEndpoints.cs` (`ValidateRequest`).

> No es un agujero de seguridad: la API valida y rechaza lo que no sabe evaluar. Es un problema
> de demostración.

### 2. El Manual de Usuario en PDF describe un sistema que no existe

**Entregable:** `Manual_Usuario_Dashboard_Omakase_Security_Gateway.pdf` (autoría de Juan David)

Dos secciones enteras están inventadas. Si alguien abre el manual y luego el panel, la
discrepancia salta a la vista.

- **§6.1 «Calibración de pesos»** propone cuatro deslizadores que deben sumar 100 %: reputación
  de IP (35 %), velocidad geográfica (25 %), huella de dispositivo (20 %) y firma del payload
  (20 %). **Ninguno existe.** No hay reputación de IP ni inspección de payload en el sistema.
  Lo real son los seis campos de `risk_score_config`: `policyWeight` y `anomalyWeight` (0,5 y
  0,5), `coldStartPenalty`, `coldStartN`, `challengeThreshold` (33) y `blockThreshold` (70).
- **§4.1 «Crear una política»** pide rellenar «Endpoint Objetivo», «Métodos HTTP», «Acción» y
  «Rate Limit». **Ninguno de los cuatro campos existe.** Una política es
  `name` + `type` + `config` + `weight` + `isActive`.
- Los umbrales de ejemplo (0-30 / 31-70 / 71-100) no son los calibrados: son **33** y **70**.

> **La memoria no usa este PDF como fuente.** El Apéndice E es un manual reescrito desde el
> código. Pero si el manual se entrega aparte, hay que corregirlo.

---

## P1 — Afecta a lo que se afirma o a la reproducibilidad

### 3. Cinco contenedores sin etiqueta de versión

**Repo:** backend · **Archivo:** `AppHost/AppHost.cs`

`keycloak`, `tempo`, `otel-collector`, `loki` y `grafana` se declaran sin tag, de modo que
Docker resuelve `:latest`. El entorno no es reproducible: un `aspire run` de hoy y otro de
dentro de un mes pueden levantar versiones distintas.

Importa porque el apartado 4.2.4 de la memoria afirma que la contenerización «asegura entornos
homogéneos y reproducibles», y porque si el jurado pregunta cómo se garantiza la
reproducibilidad, la respuesta es fijar los tags.

`AddPostgres` y `AddRedis` no están afectados: Aspire fija su imagen internamente.

Corrección: añadir `.WithImageTag("...")` a los cinco, con las versiones que se estén usando hoy.

### 4. Ningún repositorio tiene archivo LICENSE, y ambos son públicos

**Repos:** ambos

Los dos repositorios son **públicos**, no privados. Sin archivo `LICENSE`, el código publicado
queda por omisión bajo «todos los derechos reservados»: nadie puede reutilizarlo legalmente,
aunque esté a la vista. Eso convive mal con el discurso de la memoria sobre construir y aportar
sobre código abierto.

La §4.2.6 quedó redactada diciendo que la adopción formal de una licencia tipo MIT «queda
pendiente de incorporarse al repositorio», que es lo único que se podía afirmar sin mentir.

Se cierra añadiendo un `LICENSE` (MIT) a cada repositorio. Es decisión del equipo, no mía, pero
es un archivo y treinta segundos.

### 5. La fila de Keycloak de la Tabla 5 no tiene prueba automatizada

**Repo:** backend

Las otras siete filas de la Tabla 5 están respaldadas por pruebas que corren en integración
continua. La de Keycloak («los administradores no pueden autenticarse; el flujo de usuarios
cliente sigue operativo») se verificó por inspección del código, y así se declara en la memoria.

Se cierra con una prueba de integración que levante el entorno sin Keycloak y compruebe que
una petición de usuario cliente con token propio sigue evaluándose. Con eso las ocho filas
quedarían homogéneas.

---

## P2 — Conviene arreglarlo, no bloquea

### 6. El paginador visual puede quedar desfasado tras filtrar

**Repo:** frontend · **Archivo:** `src/app/shared/components/datagrid.component/datagrid.component.ts` (53-56)

`onFilterChange()` reinicia `currentPage = 1` y vuelve a pedir los datos, que es lo que
corrigió el hallazgo H-02 de la UAT. Pero la tabla de PrimeNG no tiene `[first]` enlazado en
`datagrid.component.html`, así que su posición visual la gestiona ella por dentro.

**Hay que comprobarlo en ejecución:** situarse en la página 3, aplicar un filtro que reduzca el
resultado a menos de una página y ver si el paginador vuelve a marcar la página 1 o se queda
en la 3. Si se queda, hay que enlazar `[first]` y reiniciarlo junto con `currentPage`.

No pude verificarlo porque no levanté la interfaz.

### 7. Deriva de versiones en los paquetes del backend

**Repo:** backend

- `OG.IntegrationTests` usa `Microsoft.AspNetCore.Mvc.Testing 10.0.0-preview.1.25080.5`, un
  paquete en preview dentro de una solución que por lo demás usa 10.0.x estable.
- Entity Framework Core baila entre `10.0.8` (Infrastructure) y `10.0.9`
  (`OG.Gateway.Api`, `Microsoft.EntityFrameworkCore.InMemory`).

Ninguna de las dos rompe nada hoy. Conviene unificar antes de congelar el entregable.

### 8. La configuración de producción del panel apunta a localhost

**Repo:** frontend · **Archivo:** `src/environments/environment.ts`

El archivo marcado `production: true` tiene `API_URL: 'http://localhost:5028/api/v1'`,
`issuer: 'http://localhost:8080/...'` y `requireHttps: false`.

Es coherente con que no haya despliegue real, y la memoria declara en §3.2 que el alcance es un
entorno de laboratorio. Pero si alguien pregunta por el despliegue productivo, conviene tener
la respuesta preparada: `requireHttps` tiene que ser `true` y los orígenes, los reales.

### 9. No existen capturas del panel

**Entregable**

No hay ninguna captura del sistema en funcionamiento en ninguno de los dos repositorios. El
Apéndice E las sustituye por la descripción textual de cada módulo, que es suficiente para la
memoria, pero para la presentación conviene tener capturas o vídeo.

---

## Fuera del código

### 10. Agradecimientos y dedicatoria

Quedan **seis** marcadores «(Aquí va…)» en la memoria: agradecimiento y dedicatoria de Joaquín,
Joel y Juan David. Los rellena el equipo a mano.

### 11. Revisión visual del documento

La memoria se verificó de forma automática (referencias cruzadas, formato, integridad,
enlaces), pero **no se abrió en Word ni se renderizó**, porque en el equipo donde se hizo la
revisión no hay LibreOffice instalado. Falta comprobar a ojo cómo paginan las 18 tablas nuevas
de los apéndices: que ninguna se parta de forma fea entre páginas y que ninguna columna quede
demasiado apretada.

Al abrir el documento hay que actualizar los campos con **Ctrl+A y luego F9**, para que el
índice general, el de tablas y el de figuras recojan los apéndices nuevos y las páginas
corridas.

---

## Nota sobre las ramas y los enlaces de la memoria

La memoria enlaza a **`dev`**, no a `main`, en los cuatro sitios donde cita el código:

- `https://github.com/IdkJoa/Omakase-Gateway/tree/dev` (§4.2.5, §4.2.6, Apéndice D)
- `https://github.com/IdkJoa/Omakase-GatewayUI/tree/dev` (§4.2.5, §4.2.6, Apéndice D)
- `.../blob/dev/docs/adr/ADR-001-identidad-de-los-usuarios-cliente.md` (Apéndice G)

Es lo correcto hoy, porque `dev` es la rama de integración y la que está al día. Conviene
tenerlo presente por dos motivos:

1. **El `main` del backend está 185 commits y dos meses por detrás** (último commit: 9 de junio
   de 2026, sin la carpeta `docs/`). No es un problema para la memoria, que no lo enlaza, pero
   sí lo sería si en algún momento se decide apuntar a la raíz del repositorio. El día que se
   mergee `dev` a `main`, los enlaces pueden simplificarse quitando `/tree/dev`.
2. **En el frontend, `dev` va 5 commits por detrás de `main`**, aunque el contenido de los
   archivos es idéntico: los cinco son merges de `dev` hacia `main`. El enlace a `/tree/dev`
   muestra exactamente el mismo código.

**Si se cambia de rama, hay que actualizar los enlaces de la memoria.** Están en §4.2.5, §4.2.6,
Apéndice D (dos veces) y Apéndice G.

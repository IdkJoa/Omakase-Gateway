# Omakase-Gateway

Pasarela de acceso de confianza cero que evalúa el riesgo de cada petición antes de
reenviarla al servicio de destino, combinando un motor de reglas deterministas con detección
de anomalías mediante aprendizaje automático.

Proyecto final de Taller de Desarrollo de Software, Instituto Tecnológico de Las Américas.

---

## Qué hace

Toda petición dirigida a un servicio protegido atraviesa la pasarela, que extrae su contexto
(origen, dispositivo, hora, comportamiento), calcula un puntaje de riesgo de 0 a 100 y emite
uno de tres veredictos:

| Veredicto | Condición | Efecto |
|---|---|---|
| **Allow** | riesgo ≤ 33 | La petición se reenvía al servicio de destino |
| **Challenge** | 33 < riesgo ≤ 70 | Se exige un segundo factor antes de continuar |
| **Block** | riesgo > 70 | Se corta la conexión y se registra el evento |

Los umbrales y los pesos residen en base de datos y se recalibran desde el panel sin
redesplegar.

---

## Levantar el entorno

### Requisitos previos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) en ejecución
- [.NET Aspire CLI](https://learn.microsoft.com/dotnet/aspire/), `dotnet tool install -g aspire.cli`

### Un solo comando

```bash
aspire run
```

Aspire levanta PostgreSQL, Redis, Keycloak, Grafana, Loki, Tempo, el colector de
OpenTelemetry, la pasarela y la API de administración. La primera vez descarga las imágenes,
así que tarda algunos minutos; después arranca en menos de uno.

Al arrancar, la pasarela aplica las migraciones y siembra los datos de demostración. No hay
ningún paso manual de preparación de la base.

### Dónde queda cada cosa

| Servicio | Dirección | Credenciales |
|---|---|---|
| Panel de Aspire | el que imprime la consola al arrancar | — |
| Pasarela | http://localhost:5219 | — |
| Cliente de demostración | http://localhost:5219/demo/ | `demo.cliente` / `Demo.Omakase-2026!` |
| API de administración | http://localhost:5220/swagger | token de Keycloak |
| Grafana | http://localhost:3000 | acceso anónimo de lectura; `admin` / `omakase` para editar |
| Keycloak | http://localhost:8080 | `admin` / `admin` |
| pgAdmin y RedisInsight | enlaces en el panel de Aspire | los inyecta Aspire |

> Los puertos de las dos APIs los asigna Aspire al arrancar; el panel de Aspire muestra el
> definitivo de cada una.

### El panel de administración va aparte

El panel del Security Officer es una aplicación de Angular que **vive en su propio
repositorio** y no la levanta `aspire run`. Es una decisión de organización del equipo, no un
descuido: el backend y el frontend se desarrollaron en repositorios separados.

Para tenerlo durante la demostración hay que clonarlo al lado y arrancarlo por su cuenta:

```bash
npm install && npm start
```

Queda en http://localhost:4200, que es el origen que ya tienen configurados tanto la política
CORS de la API de administración como el cliente de Keycloak del realm versionado. No hay nada
que configurar.

Si solo se dispone de este repositorio, la API de administración expone su superficie completa
en Swagger y Grafana cubre las métricas, de modo que la evaluación del backend no depende del
panel.

---

## Guion de demostración

Los cinco pasos recorren la tesis de principio a fin. Ninguno necesita preparación previa.

Abra el cliente de demostración en http://localhost:5219/demo/ e inicie sesión con las
credenciales que ya vienen rellenadas.

> **Sobre la regla de huella de dispositivo.** Distingue tres situaciones: si el usuario no
> tiene ninguna huella registrada, la registra y no penaliza; si la huella coincide con la
> conocida, tampoco; solo penaliza cuando ya hay una registrada y llega otra distinta. Por eso
> la primera petición desde un navegador nuevo suele salir permitida, y si alguna vez sale en
> desafío es porque ese usuario tenía registrada la huella de otro cliente. Conviene saberlo
> antes de enseñarlo en vivo.

### 1. Acceso permitido

Servicio `/reportes/get`, sin IP simulada, casilla de dispositivo desconocido sin marcar.

**Verde.** `demo.cliente` tiene perfil de comportamiento sembrado, la huella ya es conocida y
ninguna regla se viola: la petición se reenvía y aparece la respuesta del servicio.

### 2. Desafío de segundo factor

Marque **Simular un dispositivo desconocido** y envíe de nuevo.

**Ámbar.** La huella cambia, la regla la puntúa como dispositivo no reconocido y el riesgo
sube a la zona de desafío. La pasarela **no** reenvía nada: responde con un descriptor y un
identificador de desafío, y el cliente muestra el campo del código.

Genere el código con cualquier aplicación de autenticación usando el secreto
`TN5AV3ISWII7J6Y5IKJ4PX7R674HE33J`, introdúzcalo y pulse verificar. El cliente reintenta por
su cuenta la petición original y el acceso se concede.

Conviene señalar que el segundo factor **no reduce el riesgo**: autoriza continuar pese a él,
de forma acotada en el tiempo y registrada en la auditoría.

### 3. Bloqueo

Desmarque la casilla y elija la IP de origen de Tokio.

**Rojo.** Japón está en la lista de países denegados de la política de geofencing, que aporta
el puntaje máximo a la capa determinista y lleva el total por encima del umbral de bloqueo. El
motivo detallado queda en la auditoría y no se expone al cliente.

Es la distinción que merece explicarse: una regla determinista que dispara no es una
probabilidad sino una certeza, y por eso deniega en vez de pedir un segundo factor.

### 4. La evaluación en el registro de auditoría

Abra el panel de administración, si lo tiene levantado, y entre en el explorador de logs.
Cada una de las peticiones anteriores está ahí con su contexto, el desglose de puntajes, las
reglas disparadas y el veredicto. Al arrancar ya hay sesenta evaluaciones históricas
sembradas, de modo que las métricas no salen vacías.

Sin el panel, el mismo recorrido se hace contra la API de administración desde su Swagger, o
directamente sobre la tabla `audit_logs` con pgAdmin, que Aspire deja publicado.

### 5. Latencia bajo carga

El panel de Grafana *Latencia de Evaluación* muestra el tiempo propio del motor contra el
presupuesto de 50 ms del requisito no funcional. El procedimiento completo de la prueba de
carga está en [`load-tests/README.md`](load-tests/README.md).

---

## Cuentas sembradas

Todas existen únicamente en el perfil de desarrollo.

### Administradores (identidad gestionada por Keycloak)

| Usuario | Contraseña | Rol |
|---|---|---|
| `admin` | `admin` | ADMIN |
| `joel` | `1234` | ADMIN |
| `viewer` | `viewer` | VIEWER |

El usuario `viewer` sirve para demostrar la separación de roles: ve los logs y las métricas,
pero recibe 403 al intentar cualquier operación de escritura.

### Usuarios interceptados (identidad propia de la pasarela)

| Usuario | Contraseña | Particularidad |
|---|---|---|
| `demo.cliente` | `Demo.Omakase-2026!` | Segundo factor activo y perfil de comportamiento de catorce días |
| `cliente.ventas` | `Demo.Omakase-2026!` | Perfil de comportamiento propio |
| `cliente.soporte` | `Demo.Omakase-2026!` | Perfil de comportamiento propio |
| `cliente.finanzas` | `Demo.Omakase-2026!` | Perfil de comportamiento propio |
| `svc.integracion` | `Demo.Omakase-2026!` | Cuenta de servicio: no interactiva, su desafío escala a bloqueo |

---

## Servicios protegidos sembrados

| Ruta | Reglas asociadas | Para qué sirve en la demostración |
|---|---|---|
| `/httpbin/**` | ninguna | Destino de las pruebas de carga: mide el coste del motor sin reglas de por medio |
| `/reportes/**` | geofencing, huella de dispositivo | Conjunto reducido de reglas |
| `/nomina/**` | las cuatro, y exige autenticación | Conjunto estricto, el ejemplo del documento de requisitos |

Que cada servicio aplique reglas distintas es el motivo por el que existe la tabla de unión
entre servicios y políticas.

---

## Poner un servicio real detrás de la pasarela

La demostración usa `httpbin` como destino porque no arrastra dependencias y funciona en
cualquier equipo, pero la pasarela no sabe ni le importa qué hay al otro lado: proteger una
aplicación real es añadir una fila a `protected_services`, sin tocar código ni reiniciar.

Desde el panel de administración, en la sección de servicios, o directamente en la base:

```sql
INSERT INTO protected_services (id, name, upstream_url, requires_auth, is_active, created_at)
VALUES (gen_random_uuid(), 'expedientes', 'http://localhost:5080/', true, true, now());
```

En menos de treinta segundos la ruta `/expedientes/**` queda publicada y evaluada: el
recargador de rutas sondea la tabla periódicamente y además escucha una señal por Redis.
A partir de ahí se le asocian políticas desde el panel, igual que a los servicios sembrados.

Conviene tener presente que el servicio de destino conserva su propia autenticación. La
pasarela decide **si la petición merece llegar**; lo que el servicio haga con ella después
sigue siendo asunto suyo. Un cliente que atraviese la pasarela hacia un servicio autenticado
lleva, por tanto, dos credenciales: la de la pasarela y la del servicio. Es el
comportamiento esperado de un proxy de seguridad y no una duplicación accidental.

## Estructura del repositorio

```
Domain/              entidades y objetos de valor
Infrastructure/      persistencia, Redis, ML.NET, geolocalización, Key Vault
OG.Gateway/          motor de riesgo, middlewares, identidad de usuarios interceptados
OG.Gateway.Api/      punto de entrada de la pasarela, YARP, cliente de demostración
OG.Dashboard/        casos de uso administrativos
OG.Dashboard.Api/    API del panel bajo /api/v1
AppHost/             orquestación con .NET Aspire y configuración de los contenedores
docs/                evidencia de las historias y diagramas del capítulo IV
load-tests/          pruebas de carga con k6
```

---

## Documentación

| Documento | Contenido |
|---|---|
| [Matriz de trazabilidad](docs/HU-036-matriz-de-trazabilidad.md) | Cada requisito obligatorio del SRS con su evidencia y ubicación en el código |
| [Simulación de ataques y calibración](docs/HU-034-simulacion-de-ataques.md) | Banco de casos etiquetados, escenarios y calibración empírica |
| [Resultados del capítulo IV](docs/HU-036-resultados-capitulo-4.md) | Mediciones trasladadas al formato de la tesis |
| [Figuras del capítulo IV](docs/diagramas/README.md) | Arquitectura, flujo de evaluación, modelo de datos y despliegue |
| [Pruebas de carga](load-tests/README.md) | Procedimiento de la prueba de estrés |

---

## Solución de problemas

**Aspire no arranca y Docker responde que el puerto está ocupado.** Alguna ejecución anterior
dejó contenedores vivos. `docker ps` los lista y `docker rm -f <nombre>` los retira.

**La pasarela responde 429 a todo.** El limitador por IP está en 100 peticiones por minuto.
Es una defensa, no un fallo. Para una prueba de carga hay que elevarlo de forma explícita y
temporal, como describe [`load-tests/README.md`](load-tests/README.md).

**Todas las peticiones salen con veredicto de desafío.** El usuario carece de perfil de
comportamiento y está pagando la penalización de arranque en frío. El material de
demostración siembra perfiles, así que suele indicar que la base ya existía de antes y el
sembrado se saltó por idempotencia. Basta con eliminar el volumen de PostgreSQL y volver a
arrancar.

**La regla de viaje imposible nunca dispara.** Depende de un proveedor externo de
geolocalización. Sin conexión, la regla se omite y el sistema eleva el puntaje base en quince
puntos, dejando constancia de la degradación en la auditoría. Es el comportamiento previsto.

**El panel de Grafana pide cambiar la contraseña.** No debería: el orquestador fija las
credenciales y habilita la lectura anónima. Si ocurre, el contenedor conserva un volumen de
una ejecución anterior a ese cambio.

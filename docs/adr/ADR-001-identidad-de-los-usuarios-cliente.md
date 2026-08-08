# ADR-001. Identidad de los usuarios cliente: token propio de la pasarela en lugar de federación externa

- **Estado:** Aceptada
- **Fecha:** 2026-08-07
- **Ámbito:** HU-024, HU-025, HU-031, HU-049
- **Decide:** equipo de Omakase-Gateway
- **Sustituye a:** ninguna decisión previa

> Nota de procedencia. Este registro se redacta al cierre del ciclo para dejar constancia
> escrita de una decisión que se tomó durante el Sprint 3 y que hasta ahora solo existía
> implícita en el código. El comportamiento descrito es el que está implementado en `dev`;
> las rutas de archivo permiten verificar cada afirmación.

## Contexto

La pasarela atiende dos poblaciones de identidad que no tienen nada que ver entre sí:

1. **Administradores (Security Officers).** Entran al panel de Angular, gestionan políticas,
   servicios protegidos, roles y la configuración del motor de riesgo. Son pocos, cambian poco
   y necesitan capacidades empresariales: segundo factor, políticas de contraseña, revocación
   central, federación corporativa.
2. **Usuarios cliente.** Son el tráfico que la pasarela intercepta y evalúa. Cada una de sus
   peticiones atraviesa la ruta crítica del motor de riesgo, sujeta a un presupuesto de
   latencia de 50 ms en el percentil 95 (SRS §9.1).

Para los administradores la decisión estaba tomada desde el inicio: Keycloak como proveedor
OIDC, validando el token contra su conjunto de claves públicas (JWKS). Ver
`Infrastructure/Security/AuthenticationExtensions.cs`.

La pregunta abierta era la segunda población: **¿los usuarios cliente también se autentican
contra Keycloak (o contra un proveedor externo como Okta o Microsoft Entra ID), o la pasarela
emite su propio token?**

## Opciones consideradas

### A. Federar también a los usuarios cliente contra Keycloak

Reutiliza un único proveedor de identidad y evita mantener código de emisión y rotación de
credenciales.

- Añade una dependencia dura en la ruta crítica: la pasarela no puede emitir un veredicto si
  Keycloak no responde. Bajo el principio fail-closed de HU-031 eso significa denegar **todo**
  el tráfico interceptado ante la caída del proveedor, no solo el acceso al panel.
- El descubrimiento OIDC y la rotación de JWKS introducen llamadas de red que compiten con el
  presupuesto de latencia.
- Obliga a modelar en Keycloak usuarios que no son operadores del sistema sino sujetos de
  evaluación, con su historial de comportamiento y su huella de dispositivo.

### B. Emitir un token propio de la pasarela (elegida)

La pasarela firma sus propios JWT con una clave simétrica custodiada en Azure Key Vault y
mantiene tokens de refresco persistidos en PostgreSQL como hash.

- La validación de la firma es local: no hay llamada de red por petición.
- Aísla las dos poblaciones. La caída de Keycloak deja sin panel a los administradores pero
  **no** interrumpe la evaluación del tráfico interceptado (Tabla 5 de la memoria, fila
  «Indisponibilidad de Keycloak»).
- El coste es mantener código propio de emisión, refresco y revocación, y asumir que la
  federación con terceros queda fuera del ciclo.

### C. Federación con proveedores externos (Okta, Microsoft Entra ID, IdP corporativo)

Es la opción que da mayor alcance comercial: permite proteger servicios de terceros que ya
gestionan su propia identidad, sin que la pasarela emita credenciales.

- Requiere un registro de emisores de confianza por servicio protegido, resolución dinámica de
  JWKS por emisor y una política de caché de claves que no comprometa el presupuesto de
  latencia.
- Ninguna de esas piezas estaba en el alcance declarado del trabajo (§1.4 de la memoria), y
  añadirlas habría desplazado esfuerzo desde el motor de riesgo, que es el objeto de la
  investigación, hacia la gestión de identidad.

## Decisión

Se adopta la **opción B**. Los usuarios cliente se autentican contra la propia pasarela, que
emite un JWT firmado localmente y un token de refresco rotatorio.

Puntos de verificación en el código:

| Afirmación | Dónde se comprueba |
|---|---|
| Emisión y firma del token de acceso | `Infrastructure/Security/GatewayTokenService.cs` |
| Credenciales, refresco rotatorio y revocación | `Infrastructure/Security/LoginService.cs` |
| Persistencia del refresco como hash, nunca en claro | `Domain/Entities/RefreshToken.cs` (`token_hash`) |
| Identidad de administradores contra JWKS de Keycloak | `Infrastructure/Security/AuthenticationExtensions.cs` |
| La clave de firma proviene de Key Vault y su ausencia impide arrancar | `Infrastructure/KeyVault/KeyVaultStartupValidator.cs` |

## Consecuencias

**Favorables.** La ruta crítica no depende de una llamada de red para resolver identidad, lo
que contribuye a que el percentil 95 medido (20,82 ms) quede holgadamente por debajo del
presupuesto. El aislamiento entre poblaciones convierte la caída de Keycloak en una
degradación parcial y no total, que es justamente lo que la Tabla 5 documenta.

**Adversas.** El equipo mantiene código de identidad que un proveedor comercial daría hecho, y
la solución no puede, hoy, colocarse delante de un servicio de terceros que ya emita sus
propios tokens: el alcance real es el upstream de demostración y cualquier servicio dispuesto a
delegar la identidad en la pasarela.

**Deuda declarada.** La opción C no se descarta, se pospone. La Recomendación 6 de la memoria
la recoge como evolución posterior a la defensa, y el camino técnico es reutilizar el mismo
patrón de validación por clave pública que ya se aplica a Keycloak, extendido a un registro de
emisores de confianza por servicio protegido.

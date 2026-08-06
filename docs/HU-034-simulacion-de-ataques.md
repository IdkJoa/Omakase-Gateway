# HU-034 — Simulación de ataques con credenciales comprometidas

> **Épica:** EP-11 — Pruebas, Calibración y Defensa · **Sprint 5** · **5 pts**
> **Tareas cubiertas:** T-073 (escenarios documentados) · T-074 (ejecución y veredictos)
> **Alimenta:** Tesis Cap. IV §4.2.5 (tasa de detección y tasa de falsos positivos)

El objetivo no es "probar que bloquea". Es construir un **banco de casos etiquetados**
(legítimos vs. maliciosos), ejecutarlo contra el Gateway real y medir dos números
defendibles: **tasa de detección** y **tasa de falsos positivos**.

---

## 1. Modelo de puntuación (necesario para predecir cada veredicto)

Todo veredicto sale de esta fórmula ([RiskScoreConsolidator.cs](../OG.Gateway/Common/RiskEngine/Scoring/RiskScoreConsolidator.cs)):

```
risk = 0.5 · policyScore  +  0.5 · anomalyScore  +  coldStartPenalty     (acotado a [0,100])

coldStartPenalty = 30 · max(0, 1 − accessCount / 10)

veredicto:  risk ≤ 33 → ALLOW      risk ≤ 70 → CHALLENGE      risk > 70 → BLOCK
```

Valores **calibrados** en `risk_score_config`: `PolicyWeight=0.5`, `AnomalyWeight=0.5`,
`ColdStartPenalty=30`, `ColdStartN=10`, `ChallengeThreshold=33`, `BlockThreshold=70`.
Los pesos y el umbral de desafío **no son los sembrados por defecto**: se ajustaron con los datos
de estas simulaciones. La justificación completa está en la §10.

> **Regla de diagnóstico útil.** Si `risk_score` es exactamente `anomaly × 0.5 + 30`, el usuario
> **no tiene perfil** y está pagando la penalización de cold-start. Suele significar que el
> sembrado no corrió.

### 1.1 Dos umbrales distintos que conviene no confundir

| Umbral | Valor | Efecto |
|---|---|---|
| `ColdStartN` | 10 accesos concedidos | A partir de aquí `coldStartPenalty = 0` |
| `MinTrainingSamples` | 20 muestras | Por debajo, `anomalyScore` es **50 neutro**; por encima, lo calcula RandomizedPCA |

Entre 10 y 19 accesos el perfil ya no penaliza pero el score de anomalía sigue en 50.
**Es la ventana ideal para el banco**, porque el comportamiento es predecible y reproducible:

```
legítimo, sin reglas disparadas:  0.5·0 + 0.5·50 + 0 = 25   → ALLOW
```

### 1.2 El Policy Score es la peor violación ponderada, no un promedio

`policyScore = max(score · peso)` ([PolicyScoreCalculator.cs](../OG.Gateway/Common/RiskEngine/Scoring/PolicyScoreCalculator.cs)).

**Manda la violación más fuerte.** Añadir políticas que no disparan **no debilita** la detección.

> **Cambio respecto al diseño original de T-028.** Antes era un promedio ponderado
> `Σ(score·peso)/Σ(peso)`, y eso producía un comportamiento invertido: con 3 políticas asociadas y
> solo una disparando a 50, el Policy Score caía a **16.67** — o sea que **proteger un servicio con
> más reglas lo hacía menos detectable**. Peor: un ataque diluido podía quedar bajo el umbral,
> resolverse como ALLOW y entonces **alimentar el perfil de comportamiento** (solo los ALLOW lo
> hacen, HU-017), enseñándole al modelo de anomalías que el patrón malicioso es normal. La
> agregación por máximo corta las dos cosas a la vez.

**El peso cambia de significado.** Ya no es una importancia relativa que se cancela cuando hay una
sola política: ahora es un **tope absoluto** sobre lo que esa regla puede aportar. Una regla dura
(Viaje Imposible) se pesa **1.0**; una advisory se pesa menos y su contribución queda acotada.
Concretamente, una política de peso 0.47 que dispara a 100 aporta **47**, no 100 — bajo el promedio
anterior aportaba 100 porque el peso se cancelaba. **Quien tenga políticas con peso < 1.0 verá
veredictos distintos tras este cambio.**

Aun así el banco activa **una política por escenario** (salvo el Escenario 5, que combina a
propósito): es control de variables, para que cada caso mida una sola regla.

---

## 1.3 El JWT expira a los 15 minutos — refréscalo antes de CADA escenario

**Es el error más fácil de cometer y el más difícil de detectar.** El token del Gateway vive 15 min.
Con un token vencido, `UseAuthentication` no resuelve la identidad y la petición entra como
**anónima**; como `httpbin` tiene `requires_auth = false`, **no se rechaza: se evalúa sin usuario**.

El efecto es silencioso y arruina el banco de casos, porque las tres reglas que dependen del usuario
se desactivan a la vez:

| Componente | Sin identidad devuelve |
|---|---|
| `RandomizedPcaAnomalyDetector` | **50 neutro** (parece que el modelo no entrena) |
| `FingerprintRuleEvaluator` | score 0, `anonymous_user` (la regla nunca dispara) |
| `ImpossibleTravelRuleEvaluator` | score 0, `anonymous_user` (la regla nunca dispara) |

Solo `GEOFENCE` sigue funcionando, porque no necesita identidad. Si ves `anomaly_score = 50.00`
exacto y `triggered_rules = []`, **sospecha del token antes que del motor**.

Comprobación obligatoria tras la primera petición de cada escenario:

```bash
pg -c "SELECT user_id IS NULL AS anonimo FROM audit_logs ORDER BY evaluated_at DESC LIMIT 1;"
```

**Esperado: `anonimo = f`.** Si sale `t`, renueva el JWT y repite el escenario.

---

## 2. Precondiciones del entorno

- Aspire corriendo y Docker Desktop abierto.
- Gateway `http://localhost:5219` · Dashboard-API `http://localhost:5028` · Keycloak `http://localhost:8080`.
- **Salida a internet**: la geolocalización usa ip-api.com. Sin internet, `Geo` queda null,
  el ancla de viaje imposible no existe y los escenarios 1 y 5 **no se pueden ejecutar**.
- `ForwardedHeaders:TrustAll` debe estar en `true` — lo está en `appsettings.Development.json`,
  que es el entorno con el que arranca Aspire. Es lo que permite simular IPs con `X-Forwarded-For`.

Helpers (Git Bash):

```bash
KC=http://localhost:8080; B=http://localhost:5028; G=http://localhost:5219
IP_SD=190.166.12.45      # Santo Domingo, DO
IP_JP=133.11.1.1         # Tokio, JP
tok(){ export TOKEN=$(curl -s -X POST "$KC/realms/omakase-gateway/protocol/openid-connect/token" -H "Content-Type: application/x-www-form-urlencoded" -d grant_type=password -d client_id=omakase-dashboard -d username=admin -d password=admin | grep -o '"access_token":"[^"]*"' | sed 's/.*:"//;s/"$//'); }
otp(){ python -c "import hmac,hashlib,base64,struct,time,sys;k=base64.b32decode(sys.argv[1]);c=int(time.time())//30;h=hmac.new(k,struct.pack('>Q',c),hashlib.sha1).digest();o=h[19]&15;print('%06d'%((struct.unpack('>I',h[o:o+4])[0]&0x7fffffff)%1000000))" "$1"; }
```

---

## 3. Paso 0 — Verificar que las IPs geolocalizan donde creemos

**No des por buenas las IPs de arriba.** Si ip-api las resuelve a otro país, el escenario mide otra cosa.

```bash
curl -s "http://ip-api.com/json/190.166.12.45?fields=status,countryCode,city,lat,lon"; echo
curl -s "http://ip-api.com/json/133.11.1.1?fields=status,countryCode,city,lat,lon"; echo
```

**Esperado:** la primera `"countryCode":"DO"`, la segunda `"countryCode":"JP"`, ambas con
`"status":"success"` y con `lat`/`lon` coherentes.

- Si alguna devuelve `"status":"fail"` o un país distinto → **sustitúyela** por otra IP pública
  del país correcto y actualiza `IP_SD`/`IP_JP`. Anota el cambio en el reporte.
- Si ambas fallan por red → no sigas: los escenarios 1 y 5 no son válidos sin geo.

> La distancia Santo Domingo–Tokio es ≈ 13.600 km. Recorrida en 10 minutos implica
> ≈ 81.600 km/h, muy por encima del límite de 900 km/h de la regla.

---

## 4. Paso 1 — Perfil de comportamiento del usuario legítimo

### 4.0 Por qué el perfil no puede construirse "a mano"

Sin perfil, un usuario **legítimo** puntúa `0.5·0 + 0.5·50 + 30 = 55` → **CHALLENGE**. Midiendo
falsos positivos así, la tasa saldría **100%** y la tabla del Cap. IV no significaría nada.

Y hay un bloqueo circular: el perfil solo se alimenta con veredictos **ALLOW**
([EvaluateRiskHandler.cs:236](../OG.Gateway/Common/RiskEngine/Commands/EvaluateRiskHandler.cs:236)),
pero en cold-start el veredicto es CHALLENGE → nunca acumula.

Aunque se rompa ese círculo con step-up MFA y se lancen 20 peticiones seguidas, el resultado es
**peor que no tener modelo**: las 20 muestras caen todas en el mismo minuto, con frecuencia
creciente y diversidad decreciente. El modelo aprendería que *"muchas peticiones seguidas es lo
normal para este usuario"* — exactamente lo contrario de lo que el Escenario 2 debe detectar.

Por eso el baseline se **siembra sintéticamente** (T-089) con
[BehaviorBaselineBootstrapper](../Infrastructure/AnomalyDetection/BehaviorBaselineBootstrapper.cs):
14 días de jornada de oficina con doble pico, pausa de almuerzo, caída de fin de semana y jitter
gaussiano. Es reproducible por semilla y **debe declararse como sintético en la tesis**.

Separación medida en laboratorio con ese baseline (test
`ModeloEntrenadoConElBaseline_SeparaRafagaNocturnaDeAccesoNormal`):

| Caso | Anomaly Score |
|---|---|
| Acceso normal, martes 10:15 | **27.5** |
| Ráfaga 3:00 AM, 50 peticiones en 10 min | **100.0** |

### 4.1 Verificar que el baseline está sembrado

Se siembra al arrancar cuando `AnomalyDetection:DemoBaseline:Enabled=true`
(declarado solo en `appsettings.Development.json`). Es **idempotente**: si el usuario ya tiene
perfil, no lo pisa.

```bash
tok; UID_DEMO=$(curl -s -H "Authorization: Bearer $TOKEN" "$B/api/v1/users?page=1&pageSize=50" | python -c "import sys,json;d=json.load(sys.stdin);print([u['id'] for u in d['data'] if u['username']=='demo.cliente'][0])")
curl -s -H "Authorization: Bearer $TOKEN" "$B/api/v1/users/$UID_DEMO/profile"; echo
```

**Esperado:** `accessCount` de varios cientos (≈ 380 con 14 días), `isColdStart: false` y
`lastTrainedAt` con fecha. Si `accessCount` es 0 o no hay perfil, la BD persistida ya traía una
fila vacía que el sembrador no sobrescribe: bórrala y reinicia el Gateway.

```sql
DELETE FROM user_behavior_profiles
WHERE user_id = (SELECT id FROM users WHERE username = 'demo.cliente');
```

### 4.2 Asegurar que NO hay políticas asociadas

Los casos legítimos deben medirse con Policy Score 0.

```bash
tok
SVC=$(curl -s -H "Authorization: Bearer $TOKEN" "$B/api/v1/services?page=1&pageSize=50" | python -c "import sys,json;d=json.load(sys.stdin);print([s['id'] for s in d['data'] if s['name']=='httpbin'][0])")
echo "serviceId=$SVC"
curl -s -H "Authorization: Bearer $TOKEN" "$B/api/v1/services/$SVC/policies"; echo
```

**Esperado:** `serviceId` es un GUID, y la lista de políticas es `[]` (vacía).
Si trae asociaciones de pruebas anteriores, desasócialas antes de continuar:
`curl -s -X DELETE -H "Authorization: Bearer $TOKEN" "$B/api/v1/services/$SVC/policies/<policyId>"` → **204**.

### 4.3 Login del usuario legítimo

```bash
LOGIN=$(curl -s -X POST "$G/auth/login" -H "Content-Type: application/json" -H "X-Forwarded-For: $IP_SD" -d '{"username":"demo.cliente","password":"Demo.Omakase-2026!"}')
echo "$LOGIN"
JWT=$(echo "$LOGIN" | grep -o '"accessToken":"[^"]*"' | sed 's/.*:"//;s/"$//')
echo "len=${#JWT}"
```

**Esperado:** JSON con `accessToken` (camelCase, **no** `access_token`) y `len` de varios cientos.
Si sale `401`, revisa credenciales; si sale `423`, la cuenta está bloqueada por el Escenario 4
— espera 30 min o limpia `failed_attempts`/`locked_until` en la BD.

> Usa `UID_DEMO`, **no** `UID`: `UID` es variable reservada de bash.

### 4.4 Confirmar que un acceso legítimo sale ALLOW

Con el baseline sembrado y sin políticas asociadas, este es el **caso base del banco**.

```bash
curl -s -o /dev/null -w "acceso legitimo -> %{http_code}\n" "$G/httpbin/get" -H "Authorization: Bearer $JWT" -H "X-Forwarded-For: $IP_SD"
```

**Esperado:** **200**. Cálculo: `0.5·0 + 0.5·A + 0`. Sin muestras suficientes en la ventana el
detector devuelve el neutro 50 (ver §10.2), así que el riesgo es **25.00** — muy por debajo de 33.

> **Dato de calibración (importante para HU-035).** Ese `A ≈ 80` de la primera petición no es un
> error: arrancar la jornada (frecuencia y diversidad en 0) es un patrón poco frecuente en el
> baseline. Es benigno, pero **acota cuánto puede subirse `AnomalyWeight`** sin fabricar falsos
> positivos: con 0.5 ese acceso daría exactamente 40, justo en la frontera. El test
> `AccesoLegitimoTrasInactividad_NoPuntuaComoAnomaliaExtrema` vigila esa frontera.

Si sale **401 MFA_REQUIRED**, el perfil no está sembrado — vuelve a 4.1.

**Esperado:** `accessCount ≥ 10` y `isColdStart: false`.
**Si `accessCount` sigue en 0, no continúes**: sin perfil caliente todo el banco mide cold-start
y las métricas no sirven.

---

## 5. Los 5 escenarios (T-073)

Notación: **P** = Policy Score, **A** = Anomaly Score, **C** = cold-start penalty.
Todos parten del perfil caliente (`C = 0`, `A = 50` neutro mientras haya < 20 muestras).

### Escenario 1 — Viaje imposible

| Campo | Valor |
|---|---|
| **Precondición** | Perfil caliente. Última petición ALLOW desde `IP_SD` hace ≥ 1 min. Política `IMPOSSIBLE_TRAVEL` (peso 1.0) asociada a httpbin. |
| **Acción** | Misma credencial, misma sesión, `X-Forwarded-For: IP_JP`. |
| **Cálculo** | P=100 → `0.5·100 + 0.5·50 + 0 = 75.00` |
| **Esperado** | **BLOCK** (HTTP 403 `ACCESS_DENIED`), regla `IMPOSSIBLE_TRAVEL` en `triggered_rules` |

```bash
tok
POL_IT=$(curl -s -X POST "$B/api/v1/policies" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"HU034 Viaje Imposible","type":"ImpossibleTravel","config":{},"weight":1.0,"isActive":true}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['id'])")
curl -s -X POST "$B/api/v1/services/$SVC/policies" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"policyId\":\"$POL_IT\",\"isEnabled\":true}" -w "\n%{http_code}\n"

# ancla en Santo Domingo
curl -s -o /dev/null -w "ancla SD -> %{http_code}\n" "$G/httpbin/get" -H "Authorization: Bearer $JWT" -H "X-Forwarded-For: $IP_SD"
sleep 5
# ataque desde Tokio
curl -s -w "\nATAQUE JP -> %{http_code}\n" "$G/httpbin/get" -H "Authorization: Bearer $JWT" -H "X-Forwarded-For: $IP_JP"
```

**Esperado:** `ancla SD -> 200`, y el ataque **403** con `errorCode: ACCESS_DENIED`.

> Un firewall perimetral tradicional deja pasar esta petición sin objeción: las credenciales
> son válidas y la IP no está en ninguna lista negra.

---

### Escenario 2 — Patrón anómalo (ráfaga)

| Campo | Valor |
|---|---|
| **Precondición** | Perfil caliente **con ≥ 20 muestras** (si no, `A` es 50 fijo y la ráfaga no se refleja). Sin políticas asociadas (aísla el eje ML). |
| **Acción** | 50 peticiones seguidas desde `IP_SD`, mismo endpoint. |
| **Cálculo** | P=0 → `0.5·A`. La ráfaga sube `frequency` hacia 1 y baja `diversity`. |
| **Esperado** | `anomaly_score` creciente y **CHALLENGE** al superar 40 (requiere `A > 100`, ver nota) |

```bash
curl -s -X DELETE -H "Authorization: Bearer $TOKEN" "$B/api/v1/services/$SVC/policies/$POL_IT" -w "%{http_code}\n"
for i in $(seq 1 50); do
  printf "%s " "$(curl -s -o /dev/null -w "%{http_code}" "$G/httpbin/get" -H "Authorization: Bearer $JWT" -H "X-Forwarded-For: $IP_SD")"
done; echo
```

**Esperado:** una secuencia de `200` que, si el modelo reacciona, pasa a `401` (CHALLENGE).

> **Este escenario obligó a calibrar el motor.** Con la configuración sembrada
> (`0.6/0.4`, umbral 40) **el criterio era imposible de cumplir por construcción**: con P=0 el eje
> de anomalía aporta como máximo `0.4 · 100 = 40`, y el veredicto era ALLOW hasta 40 inclusive.
> Ni un Anomaly Score perfecto podía desafiar. El modelo detectaba bien (la ráfaga puntuó 92–98);
> lo que fallaba era el reparto de pesos.
>
> Resuelto en la §10 igualando los pesos a `0.5/0.5` y bajando el umbral de desafío a 33, con la
> distribución medida como justificación. **Resultado verificado en vivo: `risk_score = 34.25` →
> CHALLENGE**, con los casos legítimos entre 20.75 y 28.25 → ALLOW.

`FrequencyWindowMinutes=60`, `FrequencySaturation=60` → 50 peticiones en una hora dejan
`frequency ≈ 0.83`. El rate-limit del Gateway es 100 req/min por IP, así que 50 caben sin 429.

---

### Escenario 3 — Fingerprint desconocido

| Campo | Valor |
|---|---|
| **Precondición** | Perfil caliente. Política `FINGERPRINT` (peso 1.0) asociada. Al menos una huella ya registrada en Redis. |
| **Acción** | Misma credencial con `User-Agent`/`Accept-Language` distintos. |
| **Cálculo** | P=50 (violación parcial) → `0.5·50 + 0.5·50 = 50.00` |
| **Esperado** | **CHALLENGE** (401 `MFA_REQUIRED`), regla `FINGERPRINT` con detalle `unknown_device` |

```bash
tok
POL_FP=$(curl -s -X POST "$B/api/v1/policies" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"HU034 Fingerprint","type":"Fingerprint","config":{},"weight":1.0,"isActive":true}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['id'])")
curl -s -X POST "$B/api/v1/services/$SVC/policies" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"policyId\":\"$POL_FP\",\"isEnabled\":true}" -w "\n%{http_code}\n"

# huella conocida (registra la primera)
curl -s -o /dev/null -w "conocida -> %{http_code}\n" "$G/httpbin/get" -H "Authorization: Bearer $JWT" -H "X-Forwarded-For: $IP_SD" -H "User-Agent: OmakaseLegit/1.0" -H "Accept-Language: es-DO"
# dispositivo del atacante
curl -s -w "\nATACANTE -> %{http_code}\n" "$G/httpbin/get" -H "Authorization: Bearer $JWT" -H "X-Forwarded-For: $IP_SD" -H "User-Agent: AttackerTool/9.9" -H "Accept-Language: ru-RU"
```

**Esperado:** `conocida -> 200` y `ATACANTE -> 401` con `MFA_REQUIRED`.

---

### Escenario 4 — Fuerza bruta de credenciales

| Campo | Valor |
|---|---|
| **Precondición** | `demo.cliente` sin bloqueo activo (`failed_attempts < 5`). |
| **Acción** | 6 intentos de login con contraseña incorrecta. |
| **Mecanismo** | No pasa por el motor de riesgo: es el lockout de `LoginService` (`MaxFailedAttempts=5`, `LockDuration=30 min`). |
| **Esperado** | Intentos 1–4 → **401**; el 5º → **423 Locked**; el 6º → **423** aunque la contraseña sea correcta |

```bash
for i in $(seq 1 6); do
  printf "intento %s -> %s\n" "$i" "$(curl -s -o /dev/null -w "%{http_code}" -X POST "$G/auth/login" -H "Content-Type: application/json" -H "X-Forwarded-For: $IP_JP" -d '{"username":"demo.cliente","password":"clave-incorrecta"}')"
done
# con la contraseña BUENA, sigue bloqueada
curl -s -o /dev/null -w "con clave correcta -> %{http_code}\n" -X POST "$G/auth/login" -H "Content-Type: application/json" -d '{"username":"demo.cliente","password":"Demo.Omakase-2026!"}'
```

**Esperado:** `401 401 401 401 423 423` y el último también **423**.

> **Ojo:** esto deja la cuenta bloqueada 30 minutos. Ejecuta este escenario **al final**, o
> libéralo después con:
> ```sql
> UPDATE users SET failed_attempts = 0, locked_until = NULL WHERE username = 'demo.cliente';
> ```

---

### Escenario 5 — Combinación de factores

| Campo | Valor |
|---|---|
| **Precondición** | Perfil caliente. Asociadas `IMPOSSIBLE_TRAVEL` (1.0) **y** `GEOFENCE` con `denied_countries:["JP"]` (1.0). |
| **Acción** | Petición desde `IP_JP` con dispositivo desconocido. |
| **Cálculo** | Ambas disparan a 100 → P = max(100, 100) = 100 → `0.5·100 + 0.5·50 = 75.00` |
| **Esperado** | **BLOCK** con **dos** reglas en `triggered_rules` |

```bash
tok
POL_GEO=$(curl -s -X POST "$B/api/v1/policies" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"HU034 Geofence JP","type":"Geofence","config":{"denied_countries":["JP"]},"weight":1.0,"isActive":true}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['id'])")
curl -s -X POST "$B/api/v1/services/$SVC/policies" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d "{\"policyId\":\"$POL_GEO\",\"isEnabled\":true}" -w "\n%{http_code}\n"
curl -s -X POST "$B/api/v1/services/$SVC/policies" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d "{\"policyId\":\"$POL_IT\",\"isEnabled\":true}" -w "\n%{http_code}\n"

curl -s -o /dev/null -w "ancla SD -> %{http_code}\n" "$G/httpbin/get" -H "Authorization: Bearer $JWT" -H "X-Forwarded-For: $IP_SD"
sleep 5
curl -s -w "\nCOMBINADO -> %{http_code}\n" "$G/httpbin/get" -H "Authorization: Bearer $JWT" -H "X-Forwarded-For: $IP_JP" -H "User-Agent: AttackerTool/9.9"
```

**Esperado:** **403**, y en `triggered_rules` deben aparecer `GEOFENCE` **e** `IMPOSSIBLE_TRAVEL`.

> Este escenario demuestra el valor del modelo aditivo, y de paso ilustra la dilución de §1.2:
> combinar dos reglas de 100 mantiene P=100, pero combinar una de 100 con una de 50 lo bajaría a 75.

---

## 6. Captura de evidencia (T-074)

Contra Postgres (ver helpers de conexión de Aspire):

```sql
SELECT a.evaluated_at, u.username, a.source_ip,
       a.geo->>'country'  AS pais,
       a.policy_score, a.anomaly_score, a.risk_score, a.verdict,
       a.triggered_rules
FROM   audit_logs a
LEFT   JOIN users u ON u.id = a.user_id
WHERE  a.evaluated_at >= now() - interval '2 hours'
ORDER  BY a.evaluated_at DESC;
```

O por la API del explorador de logs (HU-022):

```bash
tok; curl -s -H "Authorization: Bearer $TOKEN" "$B/api/v1/logs?page=1&pageSize=50"; echo
```

Los veredictos se persisten como texto: `'ALLOW' | 'CHALLENGE' | 'BLOCK'`.

Conteo para las métricas:

```sql
SELECT verdict, COUNT(*)
FROM   audit_logs
WHERE  evaluated_at >= now() - interval '2 hours'
GROUP  BY verdict;
```

---

## 7. Métricas para el Cap. IV §4.2.5 (T-074 / T-075)

```
tasa de detección        = maliciosos detectados (BLOCK, CHALLENGE o 423) / total maliciosos
tasa de falsos positivos = legítimos NO permitidos / total legítimos
tasa de falsos negativos = maliciosos permitidos (ALLOW) / total maliciosos
```

### 7.1 Resultados medidos

Ejecución completa de los 5 escenarios contra el Gateway real, con la configuración calibrada de
la §10 (`0.5 / 0.5 / 33 / 70`) y el perfil sintético sembrado.

| # | Caso | Etiqueta | Esperado | **Observado** | policy · anomaly · risk |
|---|---|---|---|---|---|
| 1 | Viaje imposible SD→JP | malicioso | BLOCK o CHALLENGE | **BLOCK** | 100.00 · 50.00 · 75.00 |
| 2 | 2ª petición desde JP (bypass del ancla) | malicioso | no ALLOW | **BLOCK** | 100.00 · 50.00 · 75.00 |
| 3 | Ráfaga de 50 peticiones | malicioso | bloquea o desafía | **CHALLENGE** | 0.00 · 68.50 · 34.25 |
| 4 | Fingerprint desconocido | malicioso | desafía | **CHALLENGE** | 50.00 · 50.00 · 50.00 |
| 5 | Fuerza bruta (5+ intentos) | malicioso | bloqueo de cuenta | **423 Locked** | n/a (fuera del motor) |
| 6 | Combinación JP + geofence | malicioso | BLOCK | **BLOCK** | 100.00 · 56.00 · 78.00 |
| 7 | Acceso legítimo desde SD | legítimo | ALLOW | **ALLOW** | 0.00 · 50.00 · 25.00 |
| 8 | Legítimo de bajo volumen (×4) | legítimo | ALLOW | **ALLOW** | 0.00 · 41.5–56.5 · 20.75–28.25 |
| 9 | Legítimo con dispositivo conocido | legítimo | ALLOW | **ALLOW** | 0.00 · 50.00 · 25.00 |

### 7.2 Tasas

| Métrica | Valor | Cálculo |
|---|---|---|
| **Detección** | **100 %** | 6 de 6 casos maliciosos detectados |
| **Falsos negativos** | **0 %** | ningún caso malicioso resolvió ALLOW |
| **Falsos positivos** | **0 %** | ninguno de los casos legítimos fue desafiado ni bloqueado |

### 7.3 Validez de estas cifras

**Son de una sola ejecución por escenario**, no de un banco estadístico. Sirven para demostrar que
el mecanismo funciona y para sustentar la calibración, pero **no deben presentarse como tasas
poblacionales**. Para el Cap. IV conviene declararlo así: *"en la ejecución controlada de los cinco
escenarios el sistema detectó el 100 % de los accesos maliciosos sin producir falsos positivos"*, y
no *"el sistema tiene una tasa de detección del 100 %"*.

Dos cautelas que afectan a la reproducibilidad de estos números:

1. El `anomaly_score` de la ráfaga **decae si se repite la prueba** sin resembrar el perfil
   (medido: 98 → 92.5 → 80.5 → 75.5 → 68.5), porque el prefijo ALLOW de cada ráfaga entra al
   baseline. Ver limitación 10.
2. Los casos legítimos deben medirse **antes** de la ráfaga o con el perfil resembrado; la ventana
   de frecuencia dura 60 minutos. Ver limitación 6.

La evidencia cruda (260 filas de `audit_logs` con el desglose completo) se conserva fuera del
repositorio en `evidencia-HU034.txt`.

---

## 8. Comparativa contra un perímetro tradicional (T-074)

| Escenario | Firewall / VPN perimetral | Omakase-Gateway |
|---|---|---|
| Viaje imposible | **Pasa.** Credencial válida, IP no listada. | **BLOCK** por regla determinista |
| Ráfaga anómala | **Pasa.** Volumen bajo el umbral de DoS. | **CHALLENGE**: el modelo la detecta como desviación del baseline del usuario, no por volumen absoluto |
| Fingerprint desconocido | **Pasa.** No modela dispositivos. | **CHALLENGE** con step-up MFA |
| Fuerza bruta | Suele detectar por volumen | **423** + auditoría con IP y geo |
| Combinación | **Pasa.** Cada señal aislada es inocua. | **BLOCK**, riesgo aditivo |

El argumento central de la tesis: el perímetro decide **una vez, en el borde**; Omakase evalúa
**cada petición** con contexto (geo, tiempo, dispositivo, comportamiento).

---

## 9. Limitaciones declaradas

1. **La geolocalización depende de ip-api.com.** Sin internet, los escenarios 1 y 5 no son
   ejecutables. Si `Geo` es null, la regla devuelve `current_geo_unavailable` y **no dispara**.
2. **`X-Forwarded-For` se acepta porque `TrustAll=true` en Development.** Es lo que permite
   simular, y es también una superficie de ataque real: fuera de Development el valor base es
   `false` y exige declarar `KnownProxies`. Conviene decirlo en la defensa antes de que lo pregunten.
3. **El Escenario 2 no cruzaba el umbral con la configuración sembrada.** Se resolvió calibrando
   (§10), no relajando el criterio.
4. **El Escenario 4 no pasa por el motor de riesgo**: lo resuelve el lockout de `LoginService`.
   Se incluye porque T-073 lo pide explícitamente.
5. **Orden de ejecución**: el Escenario 4 bloquea la cuenta 30 minutos. Va al final.
6. **La ráfaga contamina la ventana de frecuencia durante 60 minutos.** Los casos legítimos deben
   medirse **antes** del Escenario 2, o esperar una hora. Medir un "legítimo" justo después de la
   ráfaga infla artificialmente la tasa de falsos positivos.
7. **El JWT del Gateway dura 15 minutos** (§1.3). Con el token vencido las peticiones entran como
   anónimas y tres de las cuatro reglas se desactivan en silencio.
8. **Un atacante que se mantenga bajo `MinWindowSamples` peticiones por hora no será juzgado por el
   eje de volumen.** Es la contrapartida declarada del guard de §10.2: por debajo de ese conteo la
   diversidad no es medible. Las reglas deterministas (geofencing, viaje imposible, fingerprint)
   siguen actuando con normalidad, y un ataque por volumen supera el mínimo por definición.
9. **El set `fingerprint:{userId}` de Redis vive 24 h y persiste entre corridas.** La primera vez que
   se usa un User-Agent queda registrado como conocido, así que **repetir el Escenario 3 con el mismo
   dispositivo del atacante NO dispara la regla** — hace lo correcto: ya lo vio. Antes de repetirlo:
   `rd DEL "fingerprint:{userId}"`, o usar un User-Agent distinto.
10. **La ráfaga envenena el perfil.** Las primeras peticiones de la ráfaga salen ALLOW y
    `ProfileUpdateWorker` alimenta el perfil con los accesos concedidos, de modo que cada repetición
    mete ~8 muestras de patrón-ataque en la ventana de entrenamiento. Medido a lo largo de una sesión
    de pruebas: el Anomaly Score de la misma ráfaga bajó **98 → 92.5 → 80.5 → 75.5 → 68.5**.
    **Implicación de seguridad:** un atacante que sondee repetidamente por debajo del umbral puede
    ir entrenando al modelo para que acepte su patrón (envenenamiento de baseline). HU-017 cerró el
    caso del tráfico rechazado, pero el prefijo permitido de una ráfaga sigue entrando.
    **Mitigación para la evidencia:** resembrar el perfil antes de cada captura definitiva.

---

## 10. Calibración empírica (HU-035 / T-075)

Esta sección es el entregable escrito de HU-035: **qué se cambió, con qué datos y por qué**.

### 10.1 Punto de partida y el problema

Con la configuración sembrada (`0.6 / 0.4`, umbral 40) el Escenario 2 **no podía cumplirse por
construcción**: el eje de anomalía aporta como máximo `0.4 × 100 = 40`, y el veredicto es ALLOW
hasta 40 inclusive. **Ni un Anomaly Score perfecto podía desafiar.** No era un fallo del modelo:
la ráfaga se detectaba correctamente (se midieron 92–98), pero el reparto de pesos impedía accionar.

### 10.2 Dos falsos positivos hallados por medición, y su arreglo en código

Antes de tocar pesos hubo que corregir el detector, porque medía mal a los usuarios legítimos:

| Hallazgo | Medición | Causa | Arreglo |
|---|---|---|---|
| Inicio de sesión marcado como anomalía extrema | mismo acceso: **12.0** en una siembra, **89.5** en otra | sin accesos en la ventana, `frequency` y `diversity` son **indefinidas**; el extractor las emite como `(0,0)` y ese vector cae en un extremo del espacio de features | degradar a incertidumbre cuando faltan datos |
| Segunda petición de la hora desafiada | **92.5** con un solo acceso previo | `diversity = únicos/total` sobre 1 muestra solo puede valer 1.0; sobre 2, solo 0.5 o 1.0 | exigir `MinWindowSamples` (default 5) accesos en la ventana antes de puntuar |

Ambos se resolvieron aplicando la política que el sistema **ya usaba** para cold-start (RF-M9):
*datos insuficientes → incertidumbre, nunca una señal inventada*. Está en
[RandomizedPcaAnomalyDetector](../Infrastructure/AnomalyDetection/RandomizedPcaAnomalyDetector.cs)
con tests de regresión que fijan ambos casos.

Se corrigió además una incoherencia de caché: el sembrado escribía el perfil en PostgreSQL pero
dejaba `profile:{userId}` de Redis con el perfil viejo, y `UserProfileStore` lee Redis primero.
Durante una hora el motor puntuaba contra datos obsoletos.

### 10.3 Distribución medida

Con el detector ya corregido, sobre `audit_logs`:

| Población | Anomaly Score | Risk Score |
|---|---|---|
| Legítimo sin volumen medible | 50.00 (neutro) | **25.00** |
| Legítimo de bajo volumen (5–8 accesos/h) | 41.5 – 56.5 | **20.75 – 28.25** |
| Ráfaga de 50 peticiones | 75.5 – 98.0 | **37.75 – 49.00** |

```
legítimos:  20.75 ────────── 28.25
                                  ↕  hueco de 9.5 puntos
ataque:                                37.75 ────────── 49.00
```

### 10.4 Valores calibrados y justificación

> **Nomenclatura — leer antes de cruzar esta tabla con el SRS.** El SRS §7.6 nombra los umbrales
> por el veredicto que **abren**; la entidad `RiskScoreConfig` los nombra por el veredicto que
> **cierran**. Son los mismos dos campos, con mapeo 1:1 y sin desajuste funcional:
>
> | SRS §7.6 | Entidad / columna en BD | Semántica |
> |---|---|---|
> | `allow_threshold` | `ChallengeThreshold` / `challenge_threshold` | techo de ALLOW: `risk ≤ umbral ⇒ ALLOW` |
> | `challenge_threshold` | `BlockThreshold` / `block_threshold` | techo de CHALLENGE: `risk ≤ umbral ⇒ CHALLENGE`, si no BLOCK |
>
> La tabla de abajo usa los nombres de **la entidad**, que son los que aparecen en el código
> ([RiskScoreConsolidator.cs](../OG.Gateway/Common/RiskEngine/Scoring/RiskScoreConsolidator.cs))
> y en la BD. El criterio de aceptación de HU-035 usa la grafía del SRS, así que el
> `challenge_threshold` que aquí baja de 40 a 33 es el `allow_threshold` del criterio.

| Parámetro | Antes | Después | Justificación |
|---|---|---|---|
| `policy_weight` | 0.6 | **0.5** | con 0.6/0.4 el eje de anomalía topa en 40 y no puede accionar; igualar los pesos le da capacidad de decisión sin quitársela a las reglas deterministas |
| `anomaly_weight` | 0.4 | **0.5** | ídem |
| `challenge_threshold` | 40 | **33** | punto medio del hueco entre poblaciones; deja ~4.75 de margen a cada lado, en vez de 0.25 del lado del ataque con el valor 40 |
| `block_threshold` | 75 | **70** | con los pesos igualados, una violación determinista al 100 % con anomalía neutra da exactamente `0.5·100 + 0.5·50 = 75.00`, es decir el borde exacto, y quedaba degradada a CHALLENGE. Una regla determinista que dispara no es una probabilidad sino una certeza: debe denegar. Con 70 vuelve a BLOCK con 5 puntos de margen |
| `cold_start_penalty` | 30 | 30 | sin cambios: no intervino en ningún falso positivo |
| `cold_start_n` | 10 | 10 | sin cambios |

**Resultado:** los cinco escenarios cumplen su criterio y ningún caso legítimo medido resulta
desafiado.

### 10.5 Alcance de esta calibración

HU-035 exige datos de **pruebas de estrés (HU-033) y simulaciones de ataque (HU-034)**. Aquí solo
están los segundos. Es una **calibración preliminar** sólida y justificada, pendiente de refinarse
con la distribución de carga cuando HU-033 esté disponible. El margen actual (~4.75 puntos) es
estrecho frente a la varianza observada en el Anomaly Score (±10), así que conviene revisarlo con
más muestras antes de la defensa.

---

## 11. Hallazgo corregido: la dilución del Policy Score

**Síntoma medido.** Con 3 políticas asociadas y solo el fingerprint disparando, el Policy Score fue
**16.67** en lugar de 50 — `(50+0+0)/3`. Llevado a una regla grave: un servicio con 3 políticas donde
solo dispara viaje imposible (100) caía a 33.3 de Policy Score y ≈42 de riesgo, es decir **CHALLENGE
en vez de BLOCK**. Cuantas más reglas protegían un servicio, más se diluía cada violación grave.

**Segundo efecto, más grave.** Un ataque diluido puede quedar por debajo del umbral y resolverse como
ALLOW. Y solo los ALLOW alimentan el perfil de comportamiento (HU-017), así que ese tráfico malicioso
**entra al baseline** del modelo de anomalías y le enseña que el patrón de ataque es normal. La
dilución no solo debilitaba la detección puntual: degradaba el modelo a futuro.

**Corrección aplicada.** `policyScore = max(score · peso)`. La violación más fuerte manda; las reglas
que no disparan no restan. La escala 0–100 se preserva porque `Score ∈ [0,100]` y `Weight ∈ [0,1]`.

**Efecto secundario declarado:** el peso pasa de ser una ponderación relativa a un **tope absoluto**.
Una política con peso 0.47 que dispara a 100 ahora aporta 47 (antes aportaba 100, porque en un
promedio con una sola regla el peso se cancela). Cualquier servicio con políticas de peso < 1.0
verá veredictos distintos. Las políticas duras deben pesarse **1.0**.

**Impacto en la evidencia de T-074:** ninguno. Los cinco escenarios se ejecutaron con políticas de
peso 1.0, y `max(100 × 1.0) = 100` coincide con lo que producía el promedio en ese caso. Los valores
medidos en §10.3 y las tablas de resultados siguen siendo válidos sin repetir las corridas.

Cubierto por tests unitarios, incluida la regresión explícita del caso de dilución
(`SevereViolation_NotDilutedByCleanPolicies`).

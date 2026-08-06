#!/usr/bin/env python3
"""
Figura 6 de la tesis: arquitectura general de Omakase-Gateway.

La idea que el diagrama debe dejar ver es la del SRS §1.3: un ecosistema unico donde el
motor de reglas y el modelo de anomalias corren dentro del mismo proceso que el proxy,
para no pagar latencia de red entre microservicios, y donde ningun servicio interno es
alcanzable sin atravesar la pasarela.

    python docs/diagramas/generar_arquitectura.py
"""

from pathlib import Path
from svgkit import (INK, BORDER, MUTED, LINE, GREEN, FILL_CORE, FILL_SOFT,
                    FILL_WHITE, FILL_GREEN, box, label, arrow, canvas)

W, H = 2100, 1300
p = []

# ── Actores y panel (columna izquierda) ───────────────────────────────────────

p.append(box(60, 190, 320, 120, "Client User", [
    "Aplicacion del usuario interceptado.",
    "JWT propio en Authorization: Bearer",
], fill=FILL_SOFT, body_size=11))

p.append(box(60, 370, 320, 120, "Security Officer", [
    "Navegador. Sesion OIDC",
    "(Authorization Code + PKCE)",
], fill=FILL_SOFT, body_size=11))

p.append(box(60, 550, 320, 110, "Dashboard (Angular SPA)", [
    "Politicas, servicios, roles, logs,",
    "metricas y editor de umbrales",
], fill=FILL_SOFT, body_size=11))

p.append(box(60, 720, 320, 110, "Dashboard API", [
    "/api/v1 protegido por token de",
    "Keycloak y RBAC normalizado (RF-M8)",
], fill=FILL_WHITE, body_size=10.5))

# ── Nucleo: proceso unico ─────────────────────────────────────────────────────

p.append(box(560, 130, 760, 620, "", fill="#FAFCFF", stroke=INK, dashed=True))
p.append(label(940, 170, "Omakase-Gateway  ·  proceso unico (.NET 10)", size=16, color=INK, weight="600"))
p.append(label(940, 192, "el motor de reglas y el modelo corren en memoria: sin salto de red entre capas",
                size=11.5, color=MUTED, italic=True))

p.append(box(600, 215, 680, 76, "YARP — proxy inverso e intercepcion (RF-M1)", [
    "cabeceras de seguridad -> rate limit -> autenticacion -> evaluacion de riesgo",
], fill=FILL_CORE, body_size=10.5))

p.append(box(600, 325, 325, 148, "Motor de reglas deterministas", [
    "RF-M2", "", "Geofencing · Time-Window",
    "Fingerprint · Viaje Imposible", "-> Policy Score",
], fill=FILL_WHITE, body_size=11))

p.append(box(955, 325, 325, 148, "Deteccion de anomalias", [
    "RF-M3 · ML.NET RandomizedPCA", "", "hora sin/cos, frecuencia,",
    "diversidad de endpoints", "-> Anomaly Score",
], fill=FILL_WHITE, body_size=11))

p.append(label(940, 497, "ambas capas se evaluan en paralelo: el coste es el maximo, no la suma",
                size=11.5, color=MUTED, italic=True))

p.append(box(600, 515, 680, 100, "Consolidador de veredicto (§9.1)", [
    "risk = Wp*policy + Wa*anomaly + penalizacion de arranque en frio   ->   0-100",
    "",
    "ALLOW        CHALLENGE (step-up TOTP)        BLOCK",
], fill=FILL_CORE, mono=True, body_size=10.5))

p.append(box(600, 645, 680, 76, "Canal de auditoria asincrono", [
    "System.Threading.Channels · fire-and-forget:",
    "la respuesta no espera la escritura, para preservar el presupuesto de latencia",
], fill=FILL_SOFT, body_size=10.5))

# ── Destinos (columna derecha) ────────────────────────────────────────────────

p.append(box(1500, 215, 540, 180, "Servicios protegidos (upstreams)", [
    "", "Ningun servicio interno es alcanzable",
    "sin atravesar la pasarela.", "",
    "Rutas hidratadas dinamicamente desde protected_services",
], fill=FILL_GREEN, stroke=GREEN, title_color=GREEN, body_size=10.5))

p.append(box(1500, 450, 540, 110, "Servicio de geolocalizacion IP", [
    "Externo. Ante fallo se omite la regla de Viaje",
    "Imposible y sube el Policy Score base (RF-M9).",
], fill=FILL_SOFT, body_size=10.5))

# ── Servicios de soporte (fila inferior) ──────────────────────────────────────

services = [
    (60,   "PostgreSQL",      ["10 entidades: identidad,", "RBAC, politicas, perfiles", "y auditoria inmutable"]),
    (460,  "Redis",           ["Sesiones, lista negra,", "cache de perfil, huellas,", "rate limit y step-up"]),
    (860,  "Keycloak",        ["IdP de administradores", "(OIDC + PKCE). Realm", "versionado e importado"]),
    (1260, "Azure Key Vault", ["Cadenas de conexion,", "clave de firma del JWT", "y del secreto TOTP"]),
    (1660, "Observabilidad",  ["OpenTelemetry -> Grafana,", "Loki (logs) y Tempo (trazas)"]),
]
for x, name, lines in services:
    p.append(box(x, 960, 380, 130, name, lines, fill=FILL_WHITE, body_size=11))

# ── Conexiones ────────────────────────────────────────────────────────────────

# Ruta de la peticion
p.append(arrow([(380, 250), (600, 250)]))
p.append(label(490, 241, "peticion HTTPS", size=11, halo=True))

p.append(arrow([(1280, 253), (1500, 253)], color=GREEN, width=2))
p.append(label(1390, 244, "solo tras ALLOW", size=11, color=GREEN, halo=True))

p.append(arrow([(1280, 399), (1400, 399), (1400, 505), (1500, 505)], dashed=True))
p.append(label(1400, 470, "consulta geo", size=11, halo=True))

# Administracion
p.append(arrow([(220, 490), (220, 550)], dashed=True))
p.append(arrow([(220, 660), (220, 720)]))
p.append(label(232, 695, "REST + Bearer", size=11, anchor="start", halo=True))

# Bus de servicios de soporte
BUS = 880
p.append(arrow([(940, 750), (940, BUS)], head=False))
p.append(arrow([(220, 830), (220, BUS)], head=False))
p.append(arrow([(250, BUS), (1850, BUS)], head=False))
for dx in (250, 650, 1050, 1450, 1850):
    p.append(arrow([(dx, BUS), (dx, 960)]))
p.append(label(940, BUS - 12, "dependencias de soporte  ·  resueltas por nombre logico (.NET Aspire)",
                size=11.5, italic=True, halo=True))

# Nota
p.append(box(60, 1150, 1980, 100, "", fill=FILL_SOFT, stroke=BORDER, dashed=True))
for i, ln in enumerate([
    "Nota. Linea continua: ruta de la peticion y llamadas activas. Linea discontinua: dependencia de "
    "soporte o redireccion de identidad. El entorno completo se orquesta con .NET Aspire,",
    "que resuelve las dependencias por nombre logico y publica health checks; el artefacto de despliegue "
    "se genera desde sus manifiestos con aspire publish (Figura 9). Las instancias del",
    "Gateway son stateless —todo el estado compartido reside en Redis y PostgreSQL—, condicion que "
    "permite replicarlas horizontalmente sin coordinacion entre replicas.",
]):
    p.append(label(90, 1180 + i * 22, ln, size=12, color=MUTED, anchor="start"))

svg = canvas(W, H, "Omakase-Gateway — Arquitectura general de la solucion", "\n".join(p))
out = Path(__file__).parent / "figura-06-arquitectura.svg"
out.write_text(svg, encoding="utf-8")
print(f"OK -> {out}")

#!/usr/bin/env python3
"""
Figura 9 de la tesis: vista de despliegue.

Los dos hechos que el diagrama debe sostener son (a) que el entorno es reproducible
—un solo comando levanta el ecosistema y el artefacto de despliegue se genera, no se
escribe a mano— y (b) que el Gateway es replicable porque no guarda estado en proceso.

    python docs/diagramas/generar_despliegue.py
"""

from pathlib import Path
from svgkit import (INK, BORDER, MUTED, LINE, GREEN, FILL_CORE, FILL_SOFT, FILL_WHITE,
                    FILL_GREEN, box, label, arrow, canvas)

W, H = 2020, 1360
p = []

# ── Host contenedor ───────────────────────────────────────────────────────────

p.append(box(60, 90, 1400, 900, "", fill="#FAFCFF", stroke=INK, dashed=True))
p.append(label(760, 128, "Host de ejecucion  ·  Docker Engine", size=17, color=INK, weight="600"))
p.append(label(760, 150, "en desarrollo, orquestado por el AppHost de .NET Aspire "
                          "(service discovery, health checks y panel de diagnostico)",
                size=11.5, italic=True))

# ── Contenedores de aplicacion ────────────────────────────────────────────────

p.append(label(100, 232, "Contenedores de aplicacion", size=13, color=INK, weight="600", anchor="start"))

p.append(box(100, 250, 380, 150, "dashboard-spa", [
    "node:20 (build)", "-> nginx:alpine", "",
    "SPA de Angular servida", "como contenido estatico",
], fill=FILL_WHITE, body_size=11))

p.append(box(520, 250, 380, 150, "dashboard-api", [
    ".NET 10 · aspnet:10.0", "",
    "API administrativa /api/v1", "con RBAC normalizado",
], fill=FILL_WHITE, body_size=11))

# gateway con replicas insinuadas (dibujadas primero para que queden detras)
p.append(box(964, 274, 380, 150, "", fill=FILL_WHITE, stroke=BORDER))
p.append(box(952, 262, 380, 150, "", fill=FILL_WHITE, stroke=BORDER))
p.append(box(940, 250, 380, 150, "gateway-api", [
    ".NET 10 · aspnet:10.0", "",
    "YARP + motor de riesgo + ML.NET", "en un unico proceso  ·  puerto 5219",
], fill=FILL_CORE, body_size=10.5))
p.append(label(1130, 448, "replicable: sin estado en proceso", size=11, italic=True))

# ── Contenedores de datos e identidad ─────────────────────────────────────────

p.append(label(100, 588, "Contenedores de datos e identidad", size=13, color=INK, weight="600", anchor="start"))

p.append(box(100, 606, 380, 140, "postgres:16", [
    "Volumen persistente", "",
    "Esquema de 10 entidades", "aplicado por migraciones de EF Core",
], fill=FILL_WHITE, body_size=10.5))

p.append(box(520, 606, 380, 140, "redis:7", [
    "En memoria", "",
    "Estado efimero de la ruta critica", "(sesiones, huellas, step-up)",
], fill=FILL_WHITE, body_size=10.5))

p.append(box(940, 606, 380, 140, "keycloak", [
    "Volumen persistente", "",
    "realm-export.json versionado", "y auto-importado al arrancar",
], fill=FILL_WHITE, body_size=10.5))

# ── Pila de observabilidad ────────────────────────────────────────────────────

p.append(label(100, 838, "Pila de observabilidad", size=13, color=INK, weight="600", anchor="start"))

for i, (name, sub) in enumerate([
    ("otel-collector", "recibe trazas y metricas"),
    ("grafana", "paneles; umbral de 50 ms"),
    ("loki", "logs estructurados"),
    ("tempo", "trazas distribuidas"),
]):
    p.append(box(100 + i * 310, 856, 290, 100, name, ["", sub], fill=FILL_SOFT, body_size=10.5))

# ── Fuera del host ────────────────────────────────────────────────────────────

p.append(box(1540, 230, 420, 150, "Clientes", [
    "", "Client User (app o cliente de demo)",
    "Security Officer (navegador)", "",
    "Unico punto de entrada: el Gateway",
], fill=FILL_SOFT, body_size=10.5))

p.append(box(1540, 430, 420, 130, "Servicios protegidos", [
    "", "Upstreams internos, inaccesibles",
    "sin atravesar la pasarela",
], fill=FILL_GREEN, stroke=GREEN, title_color=GREEN, body_size=11))

p.append(box(1540, 610, 420, 150, "Servicios gestionados", [
    "", "Azure Key Vault: secretos", "inyectados en el arranque", "",
    "API de geolocalizacion IP",
], fill=FILL_WHITE, body_size=11))

p.append(box(1540, 810, 420, 130, "Repositorio y CI/CD", [
    "", "GitHub + Azure Pipelines:",
    "build, pruebas, SAST (Roslyn)", "y construccion de imagenes",
], fill=FILL_SOFT, body_size=10.5))

# ── Conexiones ────────────────────────────────────────────────────────────────

# clientes -> gateway y -> SPA
p.append(arrow([(1540, 300), (1420, 300), (1420, 325), (1344, 325)], width=2))
p.append(label(1432, 292, "HTTPS", size=11, anchor="start", halo=True))
p.append(arrow([(1540, 265), (1480, 265), (1480, 190), (290, 190), (290, 250)], dashed=True))
p.append(label(760, 182, "panel de administracion", size=11, halo=True))

# SPA -> API
p.append(arrow([(480, 325), (520, 325)]))

# gateway -> upstreams
p.append(arrow([(1320, 375), (1400, 375), (1400, 495), (1540, 495)], color=GREEN, width=2))
p.append(label(1408, 465, "reenvio tras ALLOW", size=11, color=GREEN, anchor="start", halo=True))

# key vault / geo -> gateway
p.append(arrow([(1540, 685), (1380, 685), (1380, 480), (1130, 480), (1130, 424)], dashed=True))

# bus de datos
BUS = 530
p.append(arrow([(710, 400), (710, BUS)], head=False, dashed=True))
p.append(arrow([(1130, 400), (1130, 480)], head=False, dashed=True))
p.append(arrow([(1130, 480), (1130, BUS)], head=False, dashed=True))
p.append(arrow([(290, BUS), (1130, BUS)], head=False, dashed=True))
for dx in (290, 710, 1130):
    p.append(arrow([(dx, BUS), (dx, 606)], dashed=True))
p.append(label(910, BUS - 10, "dependencias resueltas por nombre logico", size=11, italic=True, halo=True))

# bus de telemetria
TEL = 790
p.append(arrow([(1380, 400), (1380, TEL)], head=False, dashed=True))
p.append(arrow([(1380, TEL), (245, TEL)], head=False, dashed=True))
for dx in (245, 555, 865, 1175):
    p.append(arrow([(dx, TEL), (dx, 856)], dashed=True))
p.append(label(1000, TEL - 10, "telemetria OTLP", size=11, italic=True, halo=True))

# ── Reproducibilidad ──────────────────────────────────────────────────────────

p.append(box(60, 1030, 1900, 140, "", fill=FILL_CORE, stroke=BORDER))
p.append(label(90, 1062, "Reproducibilidad", size=14, color=INK, weight="600", anchor="start"))
for i, ln in enumerate([
    "Desarrollo:     aspire run        levanta los contenedores con sus volumenes, resuelve las "
    "dependencias por nombre logico y publica health checks.",
    "Despliegue:     aspire publish    genera el docker-compose desde los manifiestos del AppHost; "
    "el artefacto no se escribe a mano (SRS §9.6).",
    "Datos semilla:  roles ADMIN y VIEWER, configuracion del motor, usuario administrador, "
    "servicio de demostracion y baseline sintetico reproducible.",
]):
    p.append(label(90, 1092 + i * 24, ln, size=12.5, color=MUTED, anchor="start",
                    font="'Consolas', 'Cascadia Mono', monospace"))

p.append(box(60, 1200, 1900, 100, "", fill=FILL_SOFT, stroke=BORDER, dashed=True))
for i, ln in enumerate([
    "Nota. Linea continua: trafico de peticiones. Linea discontinua: dependencia interna o "
    "telemetria. Las tres replicas insinuadas del contenedor gateway-api ilustran el requisito de",
    "escalabilidad horizontal del SRS §5.2: como todo el estado compartido —sesiones, lista negra, "
    "perfiles, huellas y contadores— reside en Redis y PostgreSQL y no en la memoria de cada",
    "instancia, las replicas no necesitan coordinarse entre si. El despliegue multi-region y la "
    "replicacion multi-nodo de la base de datos quedan fuera del alcance de este ciclo.",
]):
    p.append(label(90, 1230 + i * 22, ln, size=12, color=MUTED, anchor="start"))

svg = canvas(W, H, "Omakase-Gateway — Vista de despliegue", "\n".join(p))
out = Path(__file__).parent / "figura-09-despliegue.svg"
out.write_text(svg, encoding="utf-8")
print(f"OK -> {out}")

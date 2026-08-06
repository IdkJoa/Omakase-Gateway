#!/usr/bin/env python3
"""
Figura 9: vista de despliegue.

Dos hechos deben sostenerse: que el entorno es reproducible —un comando levanta el
ecosistema y el artefacto de despliegue se genera, no se escribe— y que la pasarela es
replicable porque no guarda estado en proceso.

    python docs/diagramas/generar_despliegue.py
"""

from pathlib import Path
from svgkit import (INK, BORDER, MUTED, LINE, GREEN, FILL_CORE, FILL_SOFT, FILL_WHITE,
                    FILL_GREEN, MONO, T_CAJA, T_CUERPO, T_ETIQUETA, T_NOTA,
                    box, label, arrow, canvas)

W, H = 1400, 940
p = []

# ── Host ──────────────────────────────────────────────────────────────────────

p.append(box(40, 96, 900, 700, "", fill="#FAFCFF", stroke=INK, dashed=True))
p.append(label(490, 132, "Host de ejecucion  ·  Docker Engine", size=T_CAJA + 2,
                color=INK, weight="600"))
p.append(label(490, 158, "orquestado por el AppHost de .NET Aspire", size=T_CUERPO,
                italic=True))

p.append(label(64, 200, "Aplicaciones", size=T_CUERPO, color=INK, weight="600", anchor="start"))
p.append(box(64, 214, 262, 118, "dashboard-spa", ["nginx:alpine"], fill=FILL_WHITE))
p.append(box(348, 214, 262, 118, "dashboard-api", [".NET 10"], fill=FILL_WHITE))
# replicas insinuadas, dibujadas antes para que queden detras
p.append(box(656, 238, 262, 118, "", fill=FILL_WHITE))
p.append(box(646, 226, 262, 118, "", fill=FILL_WHITE))
p.append(box(636, 214, 262, 118, "gateway-api", ["YARP + motor + ML.NET"], fill=FILL_CORE))
p.append(label(767, 372, "replicable: sin estado en proceso", size=T_ETIQUETA, italic=True))

p.append(label(64, 430, "Datos e identidad", size=T_CUERPO, color=INK, weight="600", anchor="start"))
p.append(box(64, 444, 262, 118, "postgres:16", ["volumen persistente"], fill=FILL_WHITE))
p.append(box(348, 444, 262, 118, "redis:7", ["en memoria"], fill=FILL_WHITE))
p.append(box(632, 444, 262, 118, "keycloak", ["realm versionado"], fill=FILL_WHITE))

p.append(label(64, 634, "Observabilidad", size=T_CUERPO, color=INK, weight="600", anchor="start"))
for i, n in enumerate(["otel-collector", "grafana", "loki", "tempo"]):
    p.append(box(64 + i * 212, 648, 194, 72, n, [], fill=FILL_SOFT, title_dy=44))

# ── Fuera del host ────────────────────────────────────────────────────────────

p.append(box(1000, 214, 360, 118, "Clientes", ["unico punto de entrada"], fill=FILL_SOFT))
p.append(box(1000, 380, 360, 118, "Servicios protegidos", ["inaccesibles sin la pasarela"],
             fill=FILL_GREEN, stroke=GREEN, title_color=GREEN))
p.append(box(1000, 546, 360, 118, "Servicios gestionados", ["Key Vault · geolocalizacion"],
             fill=FILL_WHITE))
p.append(box(1000, 700, 360, 96, "GitHub + Azure Pipelines", ["build, pruebas y SAST"],
             fill=FILL_SOFT))

# ── Conexiones ────────────────────────────────────────────────────────────────

p.append(arrow([(1000, 262), (898, 262)], width=3))
p.append(label(949, 250, "HTTPS", size=T_ETIQUETA, halo=True))
p.append(arrow([(898, 300), (960, 300), (960, 428), (1000, 428)], color=GREEN, width=3))
p.append(label(898, 418, "tras ALLOW", size=T_ETIQUETA, color=GREEN, anchor="end", halo=True))
p.append(arrow([(1000, 594), (960, 594), (960, 340), (767, 340)], dashed=True))
p.append(arrow([(326, 262), (348, 262)]))

BUS = 400
p.append(arrow([(195, 332), (195, BUS)], head=False, dashed=True))
p.append(arrow([(479, 332), (479, BUS)], head=False, dashed=True))
p.append(arrow([(195, BUS), (763, BUS)], head=False, dashed=True))
for dx in (195, 479, 763):
    p.append(arrow([(dx, BUS), (dx, 444)], dashed=True))

TEL = 606
p.append(arrow([(898, 332), (898, TEL)], head=False, dashed=True))
p.append(arrow([(898, TEL), (161, TEL)], head=False, dashed=True))
for dx in (161, 373, 585, 797):
    p.append(arrow([(dx, TEL), (dx, 648)], dashed=True))
p.append(label(700, 596, "telemetria OTLP", size=T_ETIQUETA, italic=True, halo=True))

# ── Reproducibilidad ──────────────────────────────────────────────────────────

p.append(box(40, 826, 1320, 94, "", fill=FILL_CORE, stroke=BORDER))
p.append(label(70, 858, "aspire run", size=T_CAJA, color=INK, anchor="start",
                weight="600", font=MONO))
p.append(label(230, 858, "levanta el ecosistema con sus volumenes y datos semilla",
                size=T_CUERPO, anchor="start"))
p.append(label(70, 896, "aspire publish", size=T_CAJA, color=INK, anchor="start",
                weight="600", font=MONO))
p.append(label(280, 896, "genera el docker-compose desde los manifiestos: no se escribe a mano",
                size=T_CUERPO, anchor="start"))

svg = canvas(W, H, "Vista de despliegue", "\n".join(p))
out = Path(__file__).parent / "figura-09-despliegue.svg"
out.write_text(svg, encoding="utf-8")
print(f"OK -> {out}")

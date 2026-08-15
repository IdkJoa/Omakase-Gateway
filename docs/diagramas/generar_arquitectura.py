#!/usr/bin/env python3
"""
Figura 6: arquitectura general de Omakase-Gateway.

Lo que la figura debe hacer evidente es lo del SRS §1.3: un proceso unico donde el motor
de reglas y el modelo de anomalias conviven con el proxy, y ningun servicio interno es
alcanzable sin atravesar la pasarela. El detalle esta en el cuerpo del capitulo; aqui
solo caben los nombres y una linea por caja (ver el presupuesto en svgkit).

    python docs/diagramas/generar_arquitectura.py
"""

from pathlib import Path
from svgkit import (INK, BORDER, MUTED, LINE, GREEN, FILL_CORE, FILL_SOFT, FILL_WHITE,
                    FILL_GREEN, T_CAJA, T_CUERPO, T_ETIQUETA, T_NOTA,
                    box, label, arrow, canvas)

W, H = 1400, 920
p = []

# ── Actores y panel ───────────────────────────────────────────────────────────

p.append(box(40, 130, 270, 96, "Client User", ["JWT propio del gateway"], fill=FILL_SOFT))
p.append(box(40, 262, 270, 96, "Security Officer", ["Sesion OIDC + PKCE"], fill=FILL_SOFT))
p.append(box(40, 394, 270, 96, "Dashboard (Angular)", ["Panel de administracion"], fill=FILL_SOFT))
p.append(box(40, 526, 270, 96, "Dashboard API", ["/api/v1 con RBAC"], fill=FILL_WHITE))

# ── Nucleo ────────────────────────────────────────────────────────────────────

p.append(box(370, 96, 610, 560, "", fill="#FAFCFF", stroke=INK, dashed=True))
p.append(label(675, 134, "Omakase-Gateway  ·  proceso unico (.NET 10)",
                size=T_CAJA + 1, color=INK, weight="600"))
p.append(label(675, 160, "reglas y modelo en memoria: sin salto de red entre capas",
                size=T_CUERPO, italic=True))

p.append(box(396, 180, 558, 74, "YARP — intercepcion (RF-M1)", [], fill=FILL_CORE))
p.append(box(396, 282, 268, 128, "Reglas deterministas",
             ["Geofencing · Horario", "Huella · Viaje imposible", "→ Policy Score"]))
p.append(box(686, 282, 268, 128, "Anomalias (ML.NET)",
             ["RandomizedPCA", "3 rasgos de conducta", "→ Anomaly Score"]))
p.append(label(675, 432, "las dos capas se evaluan en paralelo",
                size=T_CUERPO, italic=True))

p.append(box(396, 448, 558, 92, "Consolidador de veredicto",
             ["Risk Score 0-100  →  ALLOW · CHALLENGE · BLOCK"], fill=FILL_CORE))
p.append(box(396, 566, 558, 74, "Auditoria asincrona",
             ["fuera de la ruta critica"], fill=FILL_SOFT))

# ── Destinos ──────────────────────────────────────────────────────────────────

p.append(box(1040, 180, 320, 128, "Servicios protegidos",
             ["Inaccesibles sin", "atravesar la pasarela"],
             fill=FILL_GREEN, stroke=GREEN, title_color=GREEN))
p.append(box(1040, 372, 320, 110, "Geolocalizacion IP",
             ["Servicio externo"], fill=FILL_SOFT))

# ── Soporte ───────────────────────────────────────────────────────────────────

servicios = [
    ("PostgreSQL", "Identidad, politicas", "y auditoria"),
    ("Redis", "Estado efimero de", "la ruta critica"),
    ("Keycloak", "Identidad de", "administradores"),
    ("Key Vault", "Secretos inyectados", "al arrancar"),
    ("Observabilidad", "OpenTelemetry,", "Grafana y Loki"),
]
for i, (nombre, l1, l2) in enumerate(servicios):
    p.append(box(40 + i * 268, 728, 248, 116, nombre, [l1, l2], fill=FILL_WHITE))

# ── Conexiones ────────────────────────────────────────────────────────────────

p.append(arrow([(310, 178), (396, 178)]))
p.append(label(353, 168, "HTTPS", size=T_ETIQUETA, halo=True))

p.append(arrow([(954, 216), (1040, 216)], color=GREEN, width=3))
p.append(label(997, 206, "tras ALLOW", size=T_ETIQUETA, color=GREEN, halo=True))

p.append(arrow([(954, 380), (1000, 380), (1000, 424), (1040, 424)], dashed=True))

p.append(arrow([(175, 358), (175, 394)], dashed=True))
p.append(arrow([(175, 490), (175, 526)]))

BUS = 682
p.append(arrow([(675, 656), (675, BUS)], head=False))
p.append(arrow([(175, 622), (175, BUS)], head=False))
p.append(arrow([(164, BUS), (1200, BUS)], head=False))
for dx in (164, 432, 700, 968, 1236):
    p.append(arrow([(dx, BUS), (dx, 728)]))
p.append(label(940, BUS - 12, "dependencias de soporte", size=T_ETIQUETA,
                italic=True, halo=True))

svg = canvas(W, H, "Arquitectura general de la solucion", "\n".join(p))
out = Path(__file__).parent / "figura-06-arquitectura.svg"
out.write_text(svg, encoding="utf-8")
print(f"OK -> {out}")

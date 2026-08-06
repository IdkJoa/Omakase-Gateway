#!/usr/bin/env python3
"""
Figura 7 de la tesis: ciclo de vida de una peticion, de la intercepcion al veredicto.

Lo que el diagrama debe hacer evidente es (a) que las dos capas se evaluan en paralelo,
(b) que el step-up TOTP ocurre fuera de banda y por tanto no consume el presupuesto de
latencia, y (c) que la escritura de auditoria sale de la ruta critica.

    python docs/diagramas/generar_flujo.py
"""

from pathlib import Path
from svgkit import (INK, BORDER, MUTED, LINE, GREEN, AMBER, RED, FILL_CORE, FILL_SOFT,
                    FILL_WHITE, FILL_GREEN, FILL_AMBER, FILL_RED,
                    box, label, arrow, canvas)

W, H = 1900, 1620
p = []

CX = 620          # eje de la columna principal
BW = 620          # ancho de las cajas principales
BX = CX - BW / 2


def step(y, h, title, lines=(), fill=FILL_WHITE, stroke=BORDER, **kw):
    return box(BX, y, BW, h, title, lines, fill=fill, stroke=stroke, **kw)


# ── Columna principal ─────────────────────────────────────────────────────────

p.append(step(90, 66, "Peticion HTTP/HTTPS entrante", [
    "hacia un servicio protegido",
], fill=FILL_SOFT, body_size=11))

p.append(step(200, 88, "1 · Intercepcion (YARP) y extraccion de contexto", [
    "IP de origen, User-Agent, marca de tiempo,",
    "huella del dispositivo y ruta destino",
], body_size=11))

p.append(step(340, 88, "2 · Autenticacion del actor", [
    "si protected_services.requires_auth: firma del JWT validada",
    "en local + consulta de blacklist:{jti} en Redis",
], body_size=10.5))

p.append(step(480, 62, "3 · Despacho del comando de evaluacion (mediador)",
              fill=FILL_CORE))

# ── Dos capas en paralelo ─────────────────────────────────────────────────────

p.append(box(180, 620, 400, 168, "4a · Capa determinista", [
    "Politicas activas del servicio",
    "(service_policies)", "",
    "Geofencing · Time-Window",
    "Fingerprint · Viaje Imposible", "",
    "Policy Score = max(score x peso)",
], body_size=10.5))

p.append(box(660, 620, 400, 168, "4b · Capa probabilistica", [
    "Perfil de comportamiento",
    "(cache profile:{userId})", "",
    "RandomizedPCA sobre hora sin/cos,",
    "frecuencia y diversidad", "",
    "Anomaly Score (50 si no hay modelo)",
], body_size=10.5))

p.append(label(CX, 812, "se ejecutan en paralelo: el coste de la etapa es el maximo de ambas",
                size=12, italic=True))

p.append(box(BX - 40, 840, BW + 80, 104, "5 · Consolidacion del Risk Score", [
    "risk = Wp*policy + Wa*anomaly + coldStartPenalty      (acotado a 0-100)",
    "coldStartPenalty = base * max(0, 1 - access_count / N)",
], fill=FILL_CORE, mono=True, body_size=11))

p.append(box(BX - 40, 990, BW + 80, 76, "6 · Decision por umbrales (risk_score_config)", [
    "recalibrables desde el Dashboard, sin redespliegue",
], body_size=11))

# ── Tres veredictos ───────────────────────────────────────────────────────────

p.append(box(70, 1160, 340, 130, "ALLOW", [
    "risk <= 33", "",
    "La peticion se clona y se",
    "reenvia al upstream.",
], fill=FILL_GREEN, stroke=GREEN, title_color=GREEN, body_size=11))

p.append(box(450, 1160, 380, 130, "CHALLENGE", [
    "33 < risk <= 70", "",
    "No se proxea: HTTP 401 con",
    "MFA_REQUIRED y challengeId.",
], fill=FILL_AMBER, stroke=AMBER, title_color=AMBER, body_size=11))

p.append(box(870, 1160, 340, 130, "BLOCK", [
    "risk > 70", "",
    "Se corta la conexion",
    "(HTTP 401/403).",
], fill=FILL_RED, stroke=RED, title_color=RED, body_size=11))

# ── Step-up fuera de banda ────────────────────────────────────────────────────

p.append(box(1290, 300, 540, 420, "", fill="#FFFCF5", stroke=AMBER, dashed=True))
p.append(label(1560, 336, "Step-up MFA  ·  fuera de banda", size=15, color=AMBER, weight="600"))
p.append(label(1560, 358, "no cuenta en el presupuesto de 50 ms (§9.9)", size=11.5, italic=True))

p.append(box(1330, 380, 460, 76, "challenge:{challengeId} en Redis", [
    "TTL 2-5 min con el contexto de la peticion original",
], fill=FILL_WHITE, stroke=AMBER, body_size=10.5, mono=False))

p.append(box(1330, 480, 460, 90, "POST /auth/challenge/verify", [
    "TOTP con ventana de +/-1 paso, uso unico,",
    "maximo 5 intentos; al sexto la cuenta se bloquea (423)",
], fill=FILL_WHITE, stroke=AMBER, body_size=10.5))

p.append(box(1330, 594, 460, 100, "stepup:{userId} en Redis", [
    "TTL 10 min, ligado a la huella del dispositivo.",
    "El cliente reintenta la peticion original y el",
    "consolidador degrada CHALLENGE -> ALLOW.",
], fill=FILL_WHITE, stroke=AMBER, body_size=10.5))

p.append(box(1290, 760, 540, 116, "Casos que escalan a BLOCK", [
    "· Client user no interactivo (is_interactive = false):",
    "  no puede completar un TOTP -> fail-closed.",
    "· Redis no responde: el desafio no puede verificarse.",
], fill=FILL_RED, stroke=RED, title_color=RED, body_size=10.5, mono=False))

# ── Auditoria asincrona ───────────────────────────────────────────────────────

p.append(box(1290, 960, 540, 130, "Auditoria (fuera de la ruta critica)", [
    "El evento se publica en un canal en memoria y un",
    "worker lo persiste en audit_logs. La respuesta nunca",
    "espera la escritura.", "",
    "Medido: 201.217 peticiones, 201.217 filas, 0 descartes.",
], fill=FILL_SOFT, body_size=10.5))

p.append(box(70, 1360, 1140, 76, "7 · Resultado devuelto al cliente", [
    "Todo veredicto —incluidos los denegados— queda registrado con su contexto, "
    "los puntajes parciales y las reglas disparadas.",
], fill=FILL_SOFT, body_size=11))

# ── Conexiones ────────────────────────────────────────────────────────────────

for y1, y2 in [(156, 200), (288, 340), (428, 480)]:
    p.append(arrow([(CX, y1), (CX, y2)]))

# bifurcacion a las dos capas
p.append(arrow([(CX, 542), (CX, 580), (380, 580), (380, 620)]))
p.append(arrow([(CX, 542), (CX, 580), (860, 580), (860, 620)]))

# reunion en el consolidador
p.append(arrow([(380, 788), (380, 826), (CX, 826), (CX, 840)]))
p.append(arrow([(860, 788), (860, 826), (CX, 826), (CX, 840)]))

p.append(arrow([(CX, 944), (CX, 990)]))

# a los tres veredictos
p.append(arrow([(CX, 1066), (CX, 1110), (240, 1110), (240, 1160)], color=GREEN))
p.append(arrow([(CX, 1066), (CX, 1160)], color=AMBER))
p.append(arrow([(CX, 1066), (CX, 1110), (1040, 1110), (1040, 1160)], color=RED))

# veredictos -> resultado
for x in (240, 640, 1040):
    p.append(arrow([(x, 1290), (x, 1360)]))

# challenge -> step-up y vuelta
p.append(arrow([(830, 1200), (1120, 1200), (1120, 418), (1330, 418)], color=AMBER))
p.append(label(1150, 800, "emision del desafio", size=11, color=AMBER, anchor="start", halo=True))
p.append(arrow([(1560, 456), (1560, 480)], color=AMBER))
p.append(arrow([(1560, 570), (1560, 594)], color=AMBER))
p.append(arrow([(1330, 644), (1250, 644), (1250, 1015), (830, 1015), (830, 1190)],
                color=GREEN, dashed=True))
p.append(label(1244, 700, "reintento -> ALLOW", size=11, color=GREEN, anchor="end", halo=True))

p.append(arrow([(1560, 694), (1560, 760)], color=RED, dashed=True))

# auditoria
p.append(arrow([(CX + 340, 892), (1080, 892), (1080, 1025), (1290, 1025)], dashed=True))
p.append(label(1085, 1016, "asincrono", size=11, anchor="start", halo=True))

# ── Nota ──────────────────────────────────────────────────────────────────────

p.append(box(70, 1480, 1760, 92, "", fill=FILL_SOFT, stroke=BORDER, dashed=True))
for i, ln in enumerate([
    "Nota. Los umbrales mostrados (33 y 70) y los pesos (0,5 / 0,5) son los valores calibrados "
    "empiricamente en el Sprint 5 y residen en risk_score_config, no en codigo.",
    "El step-up no reduce el Risk Score: autoriza continuar pese al riesgo elevado, de forma "
    "deliberada, acotada en el tiempo y auditada. Un client user no interactivo",
    "no puede completar el segundo factor, por lo que su veredicto de desafio escala a bloqueo "
    "(fail-closed), y se registra de forma distinguible en la auditoria.",
]):
    p.append(label(100, 1510 + i * 22, ln, size=12, color=MUTED, anchor="start"))

svg = canvas(W, H, "Omakase-Gateway — Flujo de evaluacion de una peticion", "\n".join(p))
out = Path(__file__).parent / "figura-07-flujo-de-evaluacion.svg"
out.write_text(svg, encoding="utf-8")
print(f"OK -> {out}")

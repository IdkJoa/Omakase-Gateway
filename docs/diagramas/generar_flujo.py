#!/usr/bin/env python3
"""
Figura 7: ciclo de vida de una peticion, de la intercepcion al veredicto.

Tres cosas deben quedar visibles: que las dos capas se evaluan en paralelo, que el
step-up ocurre fuera de banda y por eso no consume el presupuesto de latencia, y que la
escritura de auditoria sale de la ruta critica. Lo demas va en el cuerpo del capitulo.

    python docs/diagramas/generar_flujo.py
"""

from pathlib import Path
from svgkit import (INK, BORDER, MUTED, LINE, GREEN, AMBER, RED, FILL_CORE, FILL_SOFT,
                    FILL_WHITE, FILL_GREEN, FILL_AMBER, FILL_RED,
                    T_CAJA, T_CUERPO, T_ETIQUETA, box, label, arrow, canvas)

W, H = 1400, 960
p = []

CX = 265          # eje de la columna principal

# ── Columna principal ─────────────────────────────────────────────────────────

p.append(box(60, 96, 410, 62, "Peticion HTTP entrante", [], fill=FILL_SOFT))
p.append(box(60, 186, 410, 76, "1 · Intercepcion y contexto",
             ["IP, agente, hora, huella"]))
p.append(box(60, 290, 410, 76, "2 · Autenticacion del actor",
             ["firma del JWT y lista negra"]))
p.append(box(60, 394, 410, 62, "3 · Despacho de la evaluacion", [], fill=FILL_CORE))

p.append(box(60, 502, 195, 124, "4a · Deterministas",
             ["4 reglas de politica", "→ Policy Score"], body_size=T_CUERPO))
p.append(box(275, 502, 195, 124, "4b · Anomalias",
             ["RandomizedPCA", "→ Anomaly Score"], body_size=T_CUERPO))
p.append(label(CX, 648, "en paralelo: el coste es el maximo", size=T_ETIQUETA, italic=True))

p.append(box(60, 664, 410, 96, "5 · Risk Score consolidado",
             ["Wp·politica + Wa·anomalia", "+ penalizacion de arranque en frio"],
             fill=FILL_CORE))

# ── Veredictos ────────────────────────────────────────────────────────────────

p.append(box(60, 812, 265, 90, "ALLOW", ["≤ 33  ·  reenvia al upstream"],
             fill=FILL_GREEN, stroke=GREEN, title_color=GREEN))
p.append(box(345, 812, 265, 90, "CHALLENGE", ["≤ 70  ·  401 MFA_REQUIRED"],
             fill=FILL_AMBER, stroke=AMBER, title_color=AMBER))
p.append(box(630, 812, 265, 90, "BLOCK", ["> 70  ·  corta la conexion"],
             fill=FILL_RED, stroke=RED, title_color=RED))

# ── Step-up fuera de banda ────────────────────────────────────────────────────

p.append(box(940, 130, 420, 396, "", fill="#FFFCF4", stroke=AMBER, dashed=True))
p.append(label(1150, 172, "Step-up MFA  ·  fuera de banda", size=T_CAJA, color=AMBER,
                weight="600"))
p.append(label(1150, 198, "no cuenta en el presupuesto de latencia", size=T_CUERPO,
                italic=True))

p.append(box(972, 220, 356, 82, "Desafio en Redis", ["TTL 2-5 min, un solo uso"],
             stroke=AMBER))
p.append(box(972, 320, 356, 82, "Verificacion del TOTP", ["maximo 5 intentos"],
             stroke=AMBER))
p.append(box(972, 420, 356, 82, "Reintento de la peticion",
             ["CHALLENGE degrada a ALLOW"], stroke=AMBER))

p.append(box(940, 566, 420, 96, "Escalan a BLOCK",
             ["cliente no interactivo · Redis caido"],
             fill=FILL_RED, stroke=RED, title_color=RED))

p.append(box(940, 700, 420, 96, "Auditoria asincrona",
             ["fuera de la ruta critica"], fill=FILL_SOFT))

# ── Conexiones ────────────────────────────────────────────────────────────────

for y1, y2 in [(158, 186), (262, 290), (366, 394)]:
    p.append(arrow([(CX, y1), (CX, y2)]))

p.append(arrow([(CX, 456), (CX, 480), (157, 480), (157, 502)]))
p.append(arrow([(CX, 456), (CX, 480), (372, 480), (372, 502)]))
p.append(arrow([(157, 626), (157, 652), (CX, 652), (CX, 664)]))
p.append(arrow([(372, 626), (372, 652), (CX, 652), (CX, 664)]))

p.append(arrow([(CX, 760), (CX, 786), (192, 786), (192, 812)], color=GREEN))
p.append(arrow([(CX, 760), (CX, 786), (477, 786), (477, 812)], color=AMBER))
p.append(arrow([(CX, 760), (CX, 786), (762, 786), (762, 812)], color=RED))

# El desafio sale por debajo de los veredictos y sube por el pasillo libre entre la
# columna principal (termina en x=470) y el panel de step-up (empieza en x=940).
p.append(arrow([(477, 902), (477, 930), (905, 930), (905, 261), (972, 261)], color=AMBER))
p.append(label(690, 922, "emite el desafio", size=T_ETIQUETA, color=AMBER, halo=True))
p.append(arrow([(1150, 302), (1150, 320)], color=AMBER))
p.append(arrow([(1150, 402), (1150, 420)], color=AMBER))

# La peticion reintentada vuelve al principio del flujo: se evalua de nuevo y es el
# consolidador quien, al ver el step-up vigente, degrada el veredicto.
p.append(arrow([(972, 461), (868, 461), (868, 74), (CX, 74), (CX, 96)],
                color=GREEN, dashed=True))
p.append(label(660, 66, "el cliente reintenta la peticion original", size=T_ETIQUETA,
                color=GREEN, halo=True))
p.append(arrow([(1150, 502), (1150, 566)], color=RED, dashed=True))

# auditoria
p.append(arrow([(470, 712), (860, 712), (860, 748), (940, 748)], dashed=True))

svg = canvas(W, H, "Flujo de evaluacion de una peticion", "\n".join(p))
out = Path(__file__).parent / "figura-07-flujo-de-evaluacion.svg"
out.write_text(svg, encoding="utf-8")
print(f"OK -> {out}")

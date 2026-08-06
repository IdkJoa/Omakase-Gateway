#!/usr/bin/env python3
"""
Figuras 10 y 11, construidas con las mediciones de HU-033 y HU-034/035.

    Figura 10 · latencia observada frente al presupuesto de 50 ms
    Figura 11 · separacion de poblaciones del Risk Score y ubicacion de los umbrales

La Figura 11 sustituye a la curva ROC prevista en el diseno original del capitulo: el
modelo es RandomizedPCA, no supervisado, y el banco etiquetado tiene nueve casos, de modo
que un AUC no seria una medida sino una extrapolacion. La separacion de poblaciones si es
medible y ademas justifica donde quedo cada umbral.

Estas dos van en pagina vertical, asi que el ancho fisico es de 6 pulgadas (576 px a
96 ppp) y el presupuesto de legibilidad es mas estricto que el de las apaisadas: el
lienzo no pasa de 1280 px.

    python docs/diagramas/generar_resultados.py
"""

from pathlib import Path
from svgkit import (INK, BORDER, MUTED, LINE, GREEN, AMBER, RED, FILL_CORE, FILL_SOFT,
                    FILL_GREEN, FILL_AMBER, FILL_RED, FONT,
                    T_CAJA, T_CUERPO, T_ETIQUETA, box, label, canvas)

# ══ Figura 10 · latencia ══════════════════════════════════════════════════════

W, H = 1280, 800
p = []

X0, Y0 = 150, 620          # origen de los ejes
PW, PH = 1050, 470
MAXMS = 60


def y_of(ms):
    return Y0 - (ms / MAXMS) * PH


for ms in range(0, MAXMS + 1, 10):
    yy = y_of(ms)
    p.append(f'<line x1="{X0}" y1="{yy}" x2="{X0 + PW}" y2="{yy}" '
             f'stroke="#E3E9F0" stroke-width="1.2"/>')
    p.append(label(X0 - 16, yy + 7, str(ms), size=T_CUERPO, anchor="end"))
p.append(f'<line x1="{X0}" y1="{Y0}" x2="{X0 + PW}" y2="{Y0}" stroke="{INK}" stroke-width="2"/>')
p.append(f'<line x1="{X0}" y1="{y_of(MAXMS)}" x2="{X0}" y2="{Y0}" stroke="{INK}" stroke-width="2"/>')
p.append(f'<text x="52" y="{Y0 - PH / 2}" font-family="{FONT}" font-size="{T_CUERPO}" '
         f'fill="{MUTED}" text-anchor="middle" '
         f'transform="rotate(-90 52 {Y0 - PH / 2})">Milisegundos</text>')

yb = y_of(50)
p.append(f'<line x1="{X0}" y1="{yb}" x2="{X0 + PW}" y2="{yb}" stroke="{RED}" '
         f'stroke-width="3" stroke-dasharray="12 7"/>')
p.append(label(X0 + PW, yb - 16, "presupuesto: 50 ms", size=T_CAJA, color=RED,
                anchor="end", weight="600"))

barras = [
    ("media", "", 11.46, FILL_SOFT, LINE),
    ("p50", "mediana", 9.56, FILL_CORE, BORDER),
    ("p90", "", 14.56, FILL_CORE, BORDER),
    ("p95", "requisito", 20.82, FILL_GREEN, GREEN),
    ("p99", "", 44.88, FILL_GREEN, GREEN),
]
bw, gap = 140, 68
x = X0 + 70
for t1, t2, val, fill, stroke in barras:
    yy = y_of(val)
    p.append(f'<rect x="{x}" y="{yy}" width="{bw}" height="{Y0 - yy}" fill="{fill}" '
             f'stroke="{stroke}" stroke-width="2.4" rx="3"/>')
    p.append(label(x + bw / 2, yy - 14, f"{val:.2f}".replace(".", ","), size=T_CAJA,
                    color=INK, weight="600"))
    p.append(label(x + bw / 2, Y0 + 32, t1, size=T_CAJA, color=INK, weight="600"))
    p.append(label(x + bw / 2, Y0 + 56, t2, size=T_CUERPO))
    x += bw + gap

p.append(box(150, 690, 1050, 84, "", fill=FILL_SOFT, dashed=True))
p.append(label(180, 722, "203.964 evaluaciones · 100 usuarios concurrentes · 566,5 peticiones/s · 0 % de fallos",
                size=T_CUERPO, anchor="start"))
p.append(label(180, 750, "Recorrido completo de la peticion: incluso el percentil 99 queda por debajo del presupuesto",
                size=T_CUERPO, anchor="start"))

svg = canvas(W, H, "Latencia observada frente al presupuesto", "\n".join(p))
out = Path(__file__).parent / "figura-10-latencia.svg"
out.write_text(svg, encoding="utf-8")
print(f"OK -> {out}")


# ══ Figura 11 · separacion de poblaciones ═════════════════════════════════════

W2, H2 = 1280, 760
q = []

AX0, AY, AW = 90, 470, 1100


def x_of(s):
    return AX0 + (s / 100) * AW


for a, b, fill, nombre, color in [
    (0, 33, FILL_GREEN, "ALLOW", GREEN),
    (33, 70, FILL_AMBER, "CHALLENGE", AMBER),
    (70, 100, FILL_RED, "BLOCK", RED),
]:
    q.append(f'<rect x="{x_of(a)}" y="{AY - 340}" width="{x_of(b) - x_of(a)}" height="340" '
             f'fill="{fill}" stroke="none" opacity="0.6"/>')
    q.append(label((x_of(a) + x_of(b)) / 2, AY - 306, nombre, size=T_CAJA + 2,
                    color=color, weight="600"))

q.append(f'<line x1="{AX0}" y1="{AY}" x2="{AX0 + AW}" y2="{AY}" stroke="{INK}" stroke-width="2"/>')
for s in range(0, 101, 10):
    xx = x_of(s)
    q.append(f'<line x1="{xx}" y1="{AY}" x2="{xx}" y2="{AY + 11}" stroke="{INK}" stroke-width="1.6"/>')
    q.append(label(xx, AY + 36, str(s), size=T_CUERPO))
q.append(label(AX0 + AW / 2, AY + 70, "Risk Score consolidado", size=T_CAJA, color=MUTED))

for s, txt in [(33, "umbral de desafio: 33"), (70, "umbral de bloqueo: 70")]:
    xx = x_of(s)
    q.append(f'<line x1="{xx}" y1="{AY - 340}" x2="{xx}" y2="{AY + 11}" stroke="{INK}" '
             f'stroke-width="2.6" stroke-dasharray="10 6"/>')
    q.append(label(xx, AY - 352, txt, size=T_CUERPO, color=INK, weight="600"))

poblaciones = [
    (20.75, 28.25, AY - 260, "Legitima  (n = 3)", GREEN),
    (34.00, 36.25, AY - 170, "Anomala por volumen  (n = 201.199)", AMBER),
    (75.00, 78.00, AY - 80, "Violacion determinista  (n = 3)", RED),
]
for lo, hi, yy, nombre, color in poblaciones:
    x1, x2 = x_of(lo), x_of(hi)
    q.append(f'<rect x="{x1}" y="{yy - 16}" width="{max(x2 - x1, 7)}" height="32" '
             f'fill="{color}" stroke="{color}" stroke-width="2" rx="3"/>')
    q.append(label(x2 + 20, yy + 8, nombre, size=T_CUERPO, color=INK, anchor="start",
                    weight="600", halo=True))
    q.append(label(x1 - 20, yy + 8, f"{lo:.2f}".replace(".", ",") + "–" + f"{hi:.2f}".replace(".", ","),
                    size=T_ETIQUETA, anchor="end"))

q.append(f'<line x1="{x_of(28.25)}" y1="{AY - 215}" x2="{x_of(34.0)}" y2="{AY - 215}" '
         f'stroke="{INK}" stroke-width="1.8"/>')
q.append(label((x_of(28.25) + x_of(34.0)) / 2, AY - 224, "hueco de 5,75 puntos",
                size=T_ETIQUETA, color=INK, halo=True))

q.append(box(90, 590, 1100, 132, "", fill=FILL_SOFT, dashed=True))
for i, ln in enumerate([
    "El umbral de desafio se situo en el punto medio del hueco entre la poblacion legitima y la anomala,",
    "no por conveniencia: deja unos 4,75 puntos de margen a cada lado. El de bloqueo bajo de 75 a 70 porque",
    "una violacion determinista con anomalia neutra da exactamente 75,00 y quedaba degradada a desafio.",
]):
    q.append(label(120, 626 + i * 30, ln, size=T_CUERPO, anchor="start"))

svg2 = canvas(W2, H2, "Separacion de poblaciones del Risk Score", "\n".join(q))
out2 = Path(__file__).parent / "figura-11-separacion-poblaciones.svg"
out2.write_text(svg2, encoding="utf-8")
print(f"OK -> {out2}")

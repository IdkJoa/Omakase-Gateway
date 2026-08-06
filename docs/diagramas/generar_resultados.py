#!/usr/bin/env python3
"""
Figuras 10 y 11 de la tesis, construidas con las mediciones de HU-033 y HU-034/035.

    Figura 10 · latencia observada frente al presupuesto de 50 ms
    Figura 11 · separacion de poblaciones del Risk Score y ubicacion de los umbrales

La Figura 11 sustituye a la curva ROC prevista en el diseno original del capitulo: el
modelo es RandomizedPCA, no supervisado, y el banco etiquetado tiene nueve casos, de modo
que un AUC no seria una medida sino una invencion. La separacion de poblaciones si es
medible y ademas justifica donde se situo cada umbral.

    python docs/diagramas/generar_resultados.py
"""

from pathlib import Path
from svgkit import (INK, BORDER, MUTED, LINE, GREEN, AMBER, RED, FILL_CORE, FILL_SOFT,
                    FILL_WHITE, FILL_GREEN, FILL_AMBER, FILL_RED, FONT, MONO,
                    box, label, arrow, canvas)

# ══ Figura 10 ═════════════════════════════════════════════════════════════════

W, H = 1500, 900
p = []

X0, Y0 = 200, 660          # origen de los ejes
PW, PH = 1120, 470         # area de trazado
MAXMS = 60                 # escala del eje Y

def y_of(ms):
    return Y0 - (ms / MAXMS) * PH

# rejilla y eje Y
for ms in range(0, MAXMS + 1, 10):
    yy = y_of(ms)
    p.append(f'<line x1="{X0}" y1="{yy}" x2="{X0 + PW}" y2="{yy}" stroke="#E3E9F0" stroke-width="1"/>')
    p.append(label(X0 - 14, yy + 5, f"{ms}", size=12, anchor="end"))
p.append(f'<line x1="{X0}" y1="{Y0}" x2="{X0 + PW}" y2="{Y0}" stroke="{INK}" stroke-width="1.4"/>')
p.append(f'<line x1="{X0}" y1="{y_of(MAXMS)}" x2="{X0}" y2="{Y0}" stroke="{INK}" stroke-width="1.4"/>')
p.append(f'<text x="70" y="{Y0 - PH / 2}" font-family="{FONT}" font-size="13" fill="{MUTED}" '
         f'text-anchor="middle" transform="rotate(-90 70 {Y0 - PH / 2})">Milisegundos</text>')

# presupuesto
yb = y_of(50)
p.append(f'<line x1="{X0}" y1="{yb}" x2="{X0 + PW}" y2="{yb}" stroke="{RED}" stroke-width="2" '
         f'stroke-dasharray="10 6"/>')
p.append(label(X0 + PW - 8, yb - 12, "Presupuesto de diseno: 50 ms (p95)", size=13, color=RED,
                anchor="end", weight="600"))

# barras: (etiqueta, valor, color de relleno, borde)
bars = [
    ("Media\nextremo a extremo", 13.59, FILL_CORE, BORDER),
    ("p90\nextremo a extremo",   19.27, FILL_CORE, BORDER),
    ("p95\nextremo a extremo",   27.72, FILL_GREEN, GREEN),
    ("p50 del motor\n(maximo obs.)", 4.90, FILL_SOFT, LINE),
    ("p95 del motor\n(maximo obs.)", 19.47, FILL_SOFT, LINE),
]
bw, gap = 130, 90
x = X0 + 80
for text, val, fill, stroke in bars:
    yy = y_of(val)
    p.append(f'<rect x="{x}" y="{yy}" width="{bw}" height="{Y0 - yy}" fill="{fill}" '
             f'stroke="{stroke}" stroke-width="1.6" rx="2"/>')
    p.append(label(x + bw / 2, yy - 12, f"{val:.2f}".replace(".", ","), size=14,
                    color=INK, weight="600"))
    for k, ln in enumerate(text.split("\n")):
        p.append(label(x + bw / 2, Y0 + 26 + k * 18, ln, size=12))
    x += bw + gap

p.append(box(200, 740, 1120, 100, "", fill=FILL_SOFT, stroke=BORDER, dashed=True))
for i, ln in enumerate([
    "201.217 evaluaciones con 100 usuarios virtuales concurrentes durante cinco minutos, a 558,9 peticiones por segundo,",
    "sin fallos de transporte. Las tres primeras barras miden el recorrido completo con k6 —cabeceras de seguridad, limite de tasa,",
    "autenticacion, evaluacion y reenvio al upstream—, por lo que acotan superiormente el overhead que exige el requisito.",
]):
    p.append(label(230, 772 + i * 22, ln, size=12, color=MUTED, anchor="start"))

svg = canvas(W, H, "Latencia observada frente al presupuesto de evaluacion", "\n".join(p))
out = Path(__file__).parent / "figura-10-latencia.svg"
out.write_text(svg, encoding="utf-8")
print(f"OK -> {out}")


# ══ Figura 11 ═════════════════════════════════════════════════════════════════

W2, H2 = 1560, 820
q = []

AX0, AY = 130, 430
AW = 1300

def x_of(score):
    return AX0 + (score / 100) * AW

# eje
q.append(f'<line x1="{AX0}" y1="{AY}" x2="{AX0 + AW}" y2="{AY}" stroke="{INK}" stroke-width="1.6"/>')
for s in range(0, 101, 10):
    xx = x_of(s)
    q.append(f'<line x1="{xx}" y1="{AY}" x2="{xx}" y2="{AY + 9}" stroke="{INK}" stroke-width="1.2"/>')
    q.append(label(xx, AY + 30, str(s), size=12))
q.append(label(AX0 + AW / 2, AY + 62, "Risk Score consolidado (0 a 100)", size=13, color=MUTED))

# zonas de veredicto
for a, b, fill, name, color in [
    (0, 33, FILL_GREEN, "ALLOW", GREEN),
    (33, 70, FILL_AMBER, "CHALLENGE", AMBER),
    (70, 100, FILL_RED, "BLOCK", RED),
]:
    q.append(f'<rect x="{x_of(a)}" y="{AY - 300}" width="{x_of(b) - x_of(a)}" height="300" '
             f'fill="{fill}" stroke="none" opacity="0.55"/>')
    q.append(label((x_of(a) + x_of(b)) / 2, AY - 272, name, size=15, color=color, weight="600"))

# umbrales
for s, txt in [(33, "umbral de desafio: 33"), (70, "umbral de bloqueo: 70")]:
    xx = x_of(s)
    q.append(f'<line x1="{xx}" y1="{AY - 300}" x2="{xx}" y2="{AY + 9}" stroke="{INK}" '
             f'stroke-width="2" stroke-dasharray="8 5"/>')
    q.append(label(xx, AY - 312, txt, size=12.5, color=INK, weight="600"))

# poblaciones medidas
pops = [
    (20.75, 28.25, AY - 240, "Legitima con perfil establecido", "n = 3  ·  banco de casos etiquetados", GREEN),
    (34.00, 36.25, AY - 160, "Anomala por volumen", "n = 201.199  ·  prueba de carga", AMBER),
    (75.00, 78.00, AY - 80,  "Violacion determinista", "n = 3  ·  viaje imposible y combinacion", RED),
]
for lo, hi, yy, name, sub, color in pops:
    x1, x2 = x_of(lo), x_of(hi)
    q.append(f'<rect x="{x1}" y="{yy - 14}" width="{max(x2 - x1, 6)}" height="28" fill="{color}" '
             f'stroke="{color}" stroke-width="1.4" rx="3"/>')
    q.append(f'<line x1="{x1}" y1="{yy}" x2="{x1 - 10}" y2="{yy}" stroke="{color}" stroke-width="1.4"/>')
    q.append(f'<line x1="{x2}" y1="{yy}" x2="{x2 + 10}" y2="{yy}" stroke="{color}" stroke-width="1.4"/>')
    q.append(label(x2 + 22, yy - 2, name, size=13, color=INK, anchor="start", weight="600"))
    q.append(label(x2 + 22, yy + 16, sub, size=11.5, color=MUTED, anchor="start"))
    q.append(label(x1 - 18, yy + 4, f"{lo:.2f}".replace(".", ",") + " – " + f"{hi:.2f}".replace(".", ","),
                    size=11.5, color=MUTED, anchor="end"))

# hueco entre poblaciones
q.append(f'<line x1="{x_of(28.25)}" y1="{AY - 192}" x2="{x_of(34.0)}" y2="{AY - 192}" '
         f'stroke="{INK}" stroke-width="1.2"/>')
q.append(label((x_of(28.25) + x_of(34.0)) / 2, AY - 199,
                "hueco de 5,75 puntos", size=11.5, color=INK))

q.append(box(130, 530, 1300, 116, "", fill=FILL_SOFT, stroke=BORDER, dashed=True))
for i, ln in enumerate([
    "El umbral de desafio se situo en 33 por ser el punto medio del hueco entre la poblacion legitima y la anomala, no por conveniencia:",
    "deja aproximadamente 4,75 puntos de margen a cada lado. El de bloqueo bajo de 75 a 70 porque, con los pesos igualados, una violacion",
    "determinista al 100 % con anomalia neutra da exactamente 75,00 —el borde exacto— y quedaba degradada a desafio. Una regla determinista",
    "que dispara no es una probabilidad sino una certeza, de modo que debe denegar.",
]):
    q.append(label(160, 562 + i * 22, ln, size=12, color=MUTED, anchor="start"))

svg2 = canvas(W2, H2, "Separacion de poblaciones del Risk Score y ubicacion de los umbrales", "\n".join(q))
out2 = Path(__file__).parent / "figura-11-separacion-poblaciones.svg"
out2.write_text(svg2, encoding="utf-8")
print(f"OK -> {out2}")

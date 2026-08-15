"""
Primitivas compartidas por los generadores de figuras de la tesis.

PRESUPUESTO DE LEGIBILIDAD — leer antes de tocar cualquier generador.

Una figura se inserta en la pagina a un ancho fisico fijo (8,2 pulgadas en apaisado,
6 en vertical). Lo que decide si el texto se lee no es la resolucion del PNG sino la
razon entre el ancho del lienzo y el tamano de la letra:

    px_en_pagina = tamano_de_letra x (ancho_fisico_en_px / ancho_del_lienzo)

A 8,2 pulgadas y 96 ppp el ancho fisico es 787 px. Para que el cuerpo de texto llegue
a los 9-11 px en pantalla —el minimo para leerse en un documento impreso— el lienzo no
puede pasar de unos 1400 px con letra de 20.

De ahi las constantes de abajo. La consecuencia practica es que estas figuras admiten
mucho menos texto del que uno querria meter: el detalle va en el cuerpo del capitulo y
en la nota al pie de la figura, no dentro de las cajas.

Verificacion obligatoria tras regenerar: rasterizar a 787 px de ancho y mirarlo.
"""

FONT = "'Segoe UI', 'Calibri', Arial, sans-serif"
MONO = "'Consolas', 'Cascadia Mono', monospace"

# Tamanos de letra del presupuesto (lienzo de ~1400 px de ancho)
T_TITULO   = 30    # titulo de la figura
T_CAJA     = 21    # titulo de una caja
T_CUERPO   = 17    # texto dentro de una caja
T_ETIQUETA = 16    # etiquetas sobre los conectores
T_NOTA     = 15    # recuadros de apoyo

INK        = "#1F3864"
BORDER     = "#4472A8"
TEXT       = "#1A1A1A"
MUTED      = "#4A5568"
LINE       = "#4A6D94"
FILL_CORE  = "#DCE6F1"
FILL_SOFT  = "#F2F6FB"
FILL_WHITE = "#FFFFFF"
GREEN      = "#1F6B45"
AMBER      = "#96610A"
RED        = "#A02015"
FILL_GREEN = "#E3F1E9"
FILL_AMBER = "#FBF0DC"
FILL_RED   = "#F9E6E3"


def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def label(x, y, text, size=T_CUERPO, color=MUTED, anchor="middle", weight="400",
          italic=False, font=None, halo=False):
    style = ' font-style="italic"' if italic else ""
    if halo:
        return (f'<text x="{x}" y="{y}" font-family="{font or FONT}" font-size="{size}" '
                f'font-weight="{weight}" text-anchor="{anchor}" stroke="#FFFFFF" '
                f'stroke-width="5" paint-order="stroke"{style} fill="{color}">{esc(text)}</text>')
    return (f'<text x="{x}" y="{y}" font-family="{font or FONT}" font-size="{size}" '
            f'font-weight="{weight}" fill="{color}" text-anchor="{anchor}"{style}>{esc(text)}</text>')


def box(x, y, w, h, title, lines=(), fill=FILL_WHITE, stroke=BORDER, title_color=INK,
        dashed=False, rx=5, title_size=T_CAJA, body_size=T_CUERPO, mono=False,
        title_dy=32, line_h=23):
    dash = ' stroke-dasharray="8 5"' if dashed else ""
    cx = x + w / 2
    out = [f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{fill}" stroke="{stroke}" '
           f'stroke-width="2" rx="{rx}"{dash}/>']
    if title:
        out.append(label(cx, y + title_dy, title, size=title_size, color=title_color,
                         weight="600"))
    y0 = y + title_dy + (line_h + 3 if title else -6)
    for i, ln in enumerate(lines):
        out.append(label(cx, y0 + i * line_h, ln, size=body_size, color=MUTED,
                         font=MONO if mono else None))
    return "\n".join(out)


def _head(tip, prev, color, size=13):
    """
    Punta de flecha como triangulo explicito.

    Se dibuja a mano en vez de con <marker> porque el rasterizador que convierte el SVG
    a PNG para insertarlo en Word no implementa marker-end, y una flecha sin punta
    convierte un diagrama de flujo en un diagrama de cajas.
    """
    (tx, ty), (px, py) = tip, prev
    dx, dy = tx - px, ty - py
    n = (dx * dx + dy * dy) ** 0.5 or 1
    ux, uy = dx / n, dy / n
    nx, ny = -uy, ux
    bx, by = tx - ux * size, ty - uy * size
    w = size * 0.45
    return (f'<path d="M {tx} {ty} L {bx + nx * w} {by + ny * w} '
            f'L {bx - nx * w} {by - ny * w} z" fill="{color}" stroke="none"/>')


def arrow(points, color=LINE, width=2.4, dashed=False, head=True, head_at="end"):
    d = "M " + " L ".join(f"{px} {py}" for px, py in points)
    dash = ' stroke-dasharray="8 5"' if dashed else ""
    out = [f'<path d="{d}" fill="none" stroke="{color}" stroke-width="{width}"'
           f'{dash} stroke-linejoin="round"/>']
    if head:
        out.append(_head(points[-1], points[-2], color) if head_at == "end"
                   else _head(points[0], points[1], color))
    return "\n".join(out)


def canvas(w, h, title, body):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" '
            f'width="{w}" height="{h}">\n'
            f'<rect width="{w}" height="{h}" fill="#FFFFFF"/>\n'
            f'{label(w / 2, 46, title, size=T_TITULO, color=INK, weight="600")}\n'
            f'{body}\n</svg>\n')

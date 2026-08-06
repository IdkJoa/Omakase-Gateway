"""Primitivas compartidas por los generadores de figuras de la tesis."""

FONT = "'Segoe UI', 'Calibri', Arial, sans-serif"
MONO = "'Consolas', 'Cascadia Mono', monospace"

# Paleta: azules del ER + acentos semanticos de veredicto (verde/ambar/rojo del SRS §4.2)
INK        = "#1F3864"
BORDER     = "#4472A8"
TEXT       = "#1A1A1A"
MUTED      = "#5A6472"
LINE       = "#5B7A9E"
FILL_CORE  = "#DCE6F1"
FILL_SOFT  = "#F2F6FB"
FILL_WHITE = "#FFFFFF"
GREEN      = "#2E7D4F"
AMBER      = "#B47600"
RED        = "#B3261E"
FILL_GREEN = "#E6F3EB"
FILL_AMBER = "#FDF2DE"
FILL_RED   = "#FBE9E7"


def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def box(x, y, w, h, title, lines=(), fill=FILL_WHITE, stroke=BORDER, title_color=INK,
        dashed=False, rx=4, title_size=14, body_size=11.5, mono=False, title_dy=24):
    """Caja con titulo y cuerpo opcional, centrada horizontalmente."""
    dash = ' stroke-dasharray="7 4"' if dashed else ""
    cx = x + w / 2
    out = [
        f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{fill}" stroke="{stroke}" '
        f'stroke-width="1.4" rx="{rx}"{dash}/>',
        f'<text x="{cx}" y="{y + title_dy}" font-family="{FONT}" font-size="{title_size}" '
        f'font-weight="600" fill="{title_color}" text-anchor="middle">{esc(title)}</text>',
    ]
    for i, ln in enumerate(lines):
        out.append(
            f'<text x="{cx}" y="{y + title_dy + 19 + i * 16}" font-family="{MONO if mono else FONT}" '
            f'font-size="{body_size}" fill="{MUTED}" text-anchor="middle">{esc(ln)}</text>'
        )
    return "\n".join(out)


def label(x, y, text, size=12, color=MUTED, anchor="middle", weight="400", italic=False,
          font=None, halo=False):
    style = ' font-style="italic"' if italic else ""
    out = []
    if halo:
        out.append(
            f'<text x="{x}" y="{y}" font-family="{font or FONT}" font-size="{size}" '
            f'font-weight="{weight}" text-anchor="{anchor}" stroke="#FFFFFF" stroke-width="4" '
            f'paint-order="stroke"{style} fill="{color}">{esc(text)}</text>'
        )
    else:
        out.append(
            f'<text x="{x}" y="{y}" font-family="{font or FONT}" font-size="{size}" '
            f'font-weight="{weight}" fill="{color}" text-anchor="{anchor}"{style}>{esc(text)}</text>'
        )
    return "\n".join(out)


def arrow(points, color=LINE, width=1.6, dashed=False, head=True, head_at="end"):
    """Polilinea con punta de flecha. points = [(x, y), ...]."""
    d = "M " + " L ".join(f"{px} {py}" for px, py in points)
    dash = ' stroke-dasharray="6 4"' if dashed else ""
    marker = f' marker-{head_at}="url(#arrow-{color.lstrip("#")})"' if head else ""
    return (f'<path d="{d}" fill="none" stroke="{color}" stroke-width="{width}"'
            f'{dash} stroke-linejoin="round"{marker}/>')


def defs(colors):
    """Marcadores de flecha, uno por color usado."""
    out = ["<defs>"]
    for c in colors:
        out.append(
            f'<marker id="arrow-{c.lstrip("#")}" viewBox="0 0 10 10" refX="9" refY="5" '
            f'markerWidth="7" markerHeight="7" orient="auto-start-reverse">'
            f'<path d="M 0 0 L 10 5 L 0 10 z" fill="{c}"/></marker>'
        )
    out.append("</defs>")
    return "\n".join(out)


def canvas(w, h, title, body, colors=(LINE, GREEN, AMBER, RED)):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}">\n'
            f'<rect width="{w}" height="{h}" fill="#FFFFFF"/>\n'
            f'{defs(colors)}\n'
            f'{label(w / 2, 36, title, size=20, color=INK, weight="600")}\n'
            f'{body}\n</svg>\n')

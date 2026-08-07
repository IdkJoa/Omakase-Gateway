#!/usr/bin/env python3
"""
Figura 8: modelo de datos relacional de Omakase-Gateway.

Alcance deliberado. La figura muestra las diez entidades con su clave primaria, sus
claves foraneas y unicas, los atributos que las distinguen, las cardinalidades y las
reglas ON DELETE. NO reproduce las noventa columnas del esquema: eso es un diccionario
de datos, cabe en la seccion 7 del SRS —a la que remite la nota al pie— y a esta escala
resultaria ilegible. Lo que el capitulo afirma que el diagrama detalla, las cardinalidades
y la integridad referencial, esta todo aqui.

Nombres, tipos y reglas se transcriben de las configuraciones de EF Core en
Infrastructure/Configurations, no del SRS, para que el diagrama sea verificable contra la
base de datos real.

    python docs/diagramas/generar_er.py
"""

from pathlib import Path
from svgkit import (INK, BORDER, MUTED, LINE, FILL_CORE, FILL_SOFT, FILL_WHITE, FONT, MONO,
                    T_CAJA, T_CUERPO, T_ETIQUETA, T_NOTA, label, canvas, esc)

W, H = 1480, 1000

HEADER_H = 36
ROW_H = 27
COL_W = 420
GAP_X = 70
XS = [40, 530, 1020]          # tres columnas de entidades


class Entidad:
    def __init__(self, nombre, x, y, filas):
        self.nombre, self.x, self.y, self.filas = nombre, x, y, filas

    @property
    def h(self):
        return HEADER_H + ROW_H * len(self.filas)

    @property
    def cx(self):
        return self.x + COL_W / 2

    @property
    def right(self):
        return self.x + COL_W

    @property
    def bottom(self):
        return self.y + self.h

    def render(self):
        out = [
            f'<rect x="{self.x}" y="{self.y}" width="{COL_W}" height="{self.h}" '
            f'fill="#FFFFFF" stroke="{BORDER}" stroke-width="2" rx="3"/>',
            f'<rect x="{self.x}" y="{self.y}" width="{COL_W}" height="{HEADER_H}" '
            f'fill="{FILL_CORE}" stroke="{BORDER}" stroke-width="2" rx="3"/>',
            label(self.cx, self.y + 25, self.nombre, size=T_CAJA, color=INK, weight="600"),
        ]
        for i, (tipo, col, clave) in enumerate(self.filas):
            ry = self.y + HEADER_H + i * ROW_H
            out.append(f'<line x1="{self.x}" y1="{ry}" x2="{self.right}" y2="{ry}" '
                       f'stroke="#C6D3E3" stroke-width="1"/>')
            ty = ry + 19
            out.append(label(self.x + 12, ty, tipo, size=T_CUERPO, color=MUTED, anchor="start"))
            out.append(label(self.x + 148, ty, col, size=T_CUERPO, color="#1A1A1A",
                             anchor="start", weight="600"))
            if clave:
                out.append(label(self.right - 14, ty, clave, size=T_CUERPO, color=INK,
                                 anchor="end", weight="600"))
        return "\n".join(out)


# ── Entidades ─────────────────────────────────────────────────────────────────

users = Entidad("users", XS[0], 125, [
    ("UUID",         "id",           "PK"),
    ("varchar(150)", "username",     "UK"),
    ("varchar(20)",  "user_type",    ""),
    ("varchar(255)", "keycloak_sub", "UK"),
    ("boolean",      "is_active",    ""),
    ("boolean",      "mfa_enabled",  ""),
])
user_roles = Entidad("user_roles", XS[0], 383, [
    ("UUID",        "id",          "PK"),
    ("UUID",        "user_id",     "FK"),
    ("UUID",        "role_id",     "FK"),
    ("timestamptz", "assigned_at", ""),
])
roles = Entidad("roles", XS[0], 587, [
    ("UUID",        "id",        "PK"),
    ("varchar(50)", "name",      "UK"),
    ("boolean",     "is_active", ""),
])

perfiles = Entidad("user_behavior_profiles", XS[1], 125, [
    ("UUID",    "id",             "PK"),
    ("UUID",    "user_id",        "FK, UK"),
    ("jsonb",   "feature_vector", ""),
    ("integer", "access_count",   ""),
    ("boolean", "is_cold_start",  ""),
])
refresh = Entidad("refresh_tokens", XS[1], 340, [
    ("UUID",         "id",         "PK"),
    ("UUID",         "user_id",    "FK"),
    ("varchar(255)", "token_hash", "UK"),
    ("timestamptz",  "expires_at", ""),
    ("boolean",      "is_revoked", ""),
])
audit = Entidad("audit_logs", XS[1], 555, [
    ("UUID",         "id",              "PK"),
    ("UUID",         "evaluation_id",   "UK"),
    ("UUID",         "user_id",         "FK"),
    ("UUID",         "service_id",      "FK"),
    ("numeric(5,2)", "risk_score",      ""),
    ("varchar(10)",  "verdict",         ""),
    ("jsonb",        "triggered_rules", ""),
    ("timestamptz",  "evaluated_at",    ""),
])

servicios = Entidad("protected_services", XS[2], 125, [
    ("UUID",         "id",            "PK"),
    ("varchar(150)", "name",          ""),
    ("varchar(500)", "upstream_url",  ""),
    ("boolean",      "requires_auth", ""),
    ("boolean",      "is_active",     ""),
])
politicas = Entidad("access_policies", XS[2], 340, [
    ("UUID",         "id",         "PK"),
    ("varchar(150)", "name",       ""),
    ("varchar(30)",  "type",       ""),
    ("jsonb",        "config",     ""),
    ("numeric(4,3)", "weight",     ""),
    ("UUID",         "created_by", "FK"),
])
svc_pol = Entidad("service_policies", XS[2], 584, [
    ("UUID",    "id",         "PK"),
    ("UUID",    "service_id", "FK"),
    ("UUID",    "policy_id",  "FK"),
    ("boolean", "is_enabled", ""),
])
config = Entidad("risk_score_config", XS[2], 766, [
    ("UUID",         "id",                  "PK"),
    ("numeric(5,4)", "policy_weight",       ""),
    ("numeric(5,4)", "anomaly_weight",      ""),
    ("numeric(5,2)", "challenge_threshold", ""),
    ("numeric(5,2)", "block_threshold",     ""),
])

entidades = [users, user_roles, roles, perfiles, refresh, audit,
             servicios, politicas, svc_pol, config]

# ── Cardinalidades ────────────────────────────────────────────────────────────

def pata(x, y, dx, dy, kind):
    """
    Simbolo de cardinalidad en (x, y). (dx, dy) es el vector unitario con el que la
    linea llega a la entidad, de modo que el simbolo se orienta solo tanto si el
    conector entra en horizontal como en vertical.
    """
    nx, ny = -dy, dx                       # normal
    bx, by = x - dx * 16, y - dy * 16      # retrocede sobre la linea
    out = []
    if kind == "uno":
        out.append(f'<line x1="{bx + nx * 11}" y1="{by + ny * 11}" '
                   f'x2="{bx - nx * 11}" y2="{by - ny * 11}" '
                   f'stroke="{LINE}" stroke-width="2.2"/>')
    else:                                   # muchos
        for s in (11, 0, -11):
            out.append(f'<line x1="{x}" y1="{y}" x2="{bx + nx * s}" y2="{by + ny * s}" '
                       f'stroke="{LINE}" stroke-width="2.2"/>')
    return "\n".join(out)


def conector(puntos, etiqueta, card_ini="uno", card_fin="muchos", label_pos=None):
    """
    Devuelve (trazo, etiqueta) por separado: el trazo se pinta debajo de las entidades y
    la etiqueta encima. Si se pintaran juntos, cualquier etiqueta que caiga sobre una
    caja quedaria oculta bajo ella.
    """
    d = "M " + " L ".join(f"{x} {y}" for x, y in puntos)
    out = [f'<path d="{d}" fill="none" stroke="{LINE}" stroke-width="2.2" '
           f'stroke-linejoin="round"/>']

    def unit(a, b):
        dx, dy = b[0] - a[0], b[1] - a[1]
        n = (dx * dx + dy * dy) ** 0.5 or 1
        return dx / n, dy / n

    dx0, dy0 = unit(puntos[1], puntos[0])
    out.append(pata(*puntos[0], dx0, dy0, card_ini))
    dx1, dy1 = unit(puntos[-2], puntos[-1])
    out.append(pata(*puntos[-1], dx1, dy1, card_fin))

    lx, ly = label_pos or ((puntos[0][0] + puntos[-1][0]) / 2,
                           (puntos[0][1] + puntos[-1][1]) / 2)
    return ("\n".join(out),
            label(lx, ly, etiqueta, size=T_ETIQUETA, color=INK, italic=True, halo=True))


_conexiones = [
    # dentro de la columna de identidad
    conector([(users.cx - 120, users.bottom), (users.cx - 120, user_roles.y)],
             "1:N  CASCADE", label_pos=(users.cx - 120, 358)),
    conector([(roles.cx + 110, roles.y), (roles.cx + 110, user_roles.bottom)],
             "1:N  CASCADE", label_pos=(roles.cx + 110, 562)),
    # users hacia sus dependientes
    conector([(users.right, users.y + 60), (perfiles.x, perfiles.y + 60)],
             "1:1", card_fin="uno", label_pos=(495, users.y + 46)),
    conector([(users.right, users.y + 130), (492, users.y + 130), (492, refresh.y + 80),
              (refresh.x, refresh.y + 80)],
             "1:N", label_pos=(497, 330)),
    conector([(users.right, users.y + 165), (505, users.y + 165), (505, audit.y + 90),
              (audit.x, audit.y + 90)],
             "1:N  SET NULL", label_pos=(492, 540)),
    # users autor de politicas: pasillo superior, por encima de todas las entidades
    conector([(users.cx + 90, users.y), (users.cx + 90, 92), (politicas.cx, 92),
              (politicas.cx, politicas.y)],
             "1:N  RESTRICT", label_pos=(760, 82)),
    # servicios y politicas
    conector([(servicios.x, servicios.y + 60), (990, servicios.y + 60),
              (990, audit.y + 130), (audit.right, audit.y + 130)],
             "1:N", label_pos=(985, 452), card_fin="muchos"),
    conector([(servicios.right, servicios.y + 110), (1455, servicios.y + 110),
              (1455, svc_pol.y + 60), (svc_pol.right, svc_pol.y + 60)],
             "1:N", label_pos=(1400, 250)),
    conector([(politicas.cx - 120, politicas.bottom), (politicas.cx - 120, svc_pol.y)],
             "1:N", label_pos=(politicas.cx - 120, 566)),
]

trazos    = [c[0] for c in _conexiones]
etiquetas = [c[1] for c in _conexiones]

# ── Paneles ───────────────────────────────────────────────────────────────────

def panel(x, y, w, titulo, lineas, mono=True, lh=24):
    h = 48 + lh * len(lineas)
    out = [f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{FILL_SOFT}" '
           f'stroke="{BORDER}" stroke-width="1.6" stroke-dasharray="8 5" rx="4"/>',
           label(x + 16, y + 30, titulo, size=T_CAJA, color=INK, anchor="start", weight="600")]
    for i, ln in enumerate(lineas):
        out.append(f'<text x="{x + 16}" y="{y + 56 + i * lh}" font-family="{MONO if mono else FONT}" '
                   f'font-size="{T_NOTA}" fill="{MUTED}" xml:space="preserve">{esc(ln)}</text>')
    return "\n".join(out)


paneles = [
    panel(XS[0], 740, COL_W, "Estado efimero en Redis", [
        "session:{userId}          15 min",
        "blacklist:{jti}           hasta expirar",
        "profile:{userId}          1 h",
        "fingerprint:{userId}      24 h",
        "ratelimit:{ip}            60 s",
        "challenge:{challengeId}   2-5 min",
        "stepup:{userId}           10 min",
    ], lh=22),
    panel(XS[1], 820, COL_W, "Leyenda", [
        "PK  primaria    FK  foranea    UK  unica",
        "CASCADE   el hijo no existe sin su padre",
        "SET NULL  la auditoria sobrevive al borrado",
        "RESTRICT  no se borra un autor con politicas",
    ], mono=False),
]

nota_config = label(config.cx, config.y - 16,
                    "fila unica global, sin claves foraneas",
                    size=T_ETIQUETA, italic=True)

cuerpo = "\n".join(trazos + [e.render() for e in entidades] + etiquetas
                   + paneles + [nota_config])
svg = canvas(W, H, "Modelo de datos relacional (PostgreSQL, 10 entidades)", cuerpo)
out = Path(__file__).parent / "figura-08-modelo-de-datos.svg"
out.write_text(svg, encoding="utf-8")
print(f"OK -> {out}")

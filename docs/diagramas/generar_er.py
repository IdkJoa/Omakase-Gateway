#!/usr/bin/env python3
"""
Genera la Figura 8 de la tesis: diagrama entidad-relacion de Omakase-Gateway.

La fuente de verdad son las configuraciones de EF Core en Infrastructure/Configurations:
tipos, nulabilidad, claves, indices y reglas ON DELETE se transcriben de ahi, no del SRS,
para que el diagrama sea verificable contra la base de datos real.

    python docs/diagramas/generar_er.py

Salida: docs/diagramas/figura-08-modelo-de-datos.svg (vectorial, insertable en Word).
"""

from pathlib import Path

# ── Estilo ────────────────────────────────────────────────────────────────────

HEADER_FILL = "#DCE6F1"
BORDER      = "#4472A8"
TITLE_COLOR = "#1F3864"
TEXT_COLOR  = "#1A1A1A"
NOTE_COLOR  = "#5A6472"
ROW_LINE    = "#C6D3E3"
LINE_COLOR  = "#5B7A9E"
FONT        = "'Segoe UI', 'Calibri', Arial, sans-serif"

HEADER_H = 32
ROW_H    = 26
PAD_X    = 8


class Table:
    """Una entidad del modelo: cabecera + filas de [tipo, columna, clave, restricciones]."""

    def __init__(self, name, x, y, w, rows, col_ratios=(0.24, 0.30, 0.08, 0.38)):
        self.name = name
        self.x, self.y, self.w = x, y, w
        self.rows = rows
        self.col_ratios = col_ratios

    @property
    def h(self):
        return HEADER_H + ROW_H * len(self.rows)

    @property
    def cx(self):
        return self.x + self.w / 2

    @property
    def bottom(self):
        return self.y + self.h

    def col_x(self, i):
        """Borde izquierdo de la columna i."""
        return self.x + sum(self.col_ratios[:i]) * self.w

    def render(self):
        out = [
            f'<rect x="{self.x}" y="{self.y}" width="{self.w}" height="{self.h}" '
            f'fill="#FFFFFF" stroke="{BORDER}" stroke-width="1.2" rx="2"/>',
            f'<rect x="{self.x}" y="{self.y}" width="{self.w}" height="{HEADER_H}" '
            f'fill="{HEADER_FILL}" stroke="{BORDER}" stroke-width="1.2" rx="2"/>',
            f'<text x="{self.cx}" y="{self.y + 21}" font-family="{FONT}" font-size="15" '
            f'font-weight="600" fill="{TITLE_COLOR}" text-anchor="middle">{esc(self.name)}</text>',
        ]

        for i, row in enumerate(self.rows):
            ry = self.y + HEADER_H + i * ROW_H
            out.append(
                f'<line x1="{self.x}" y1="{ry}" x2="{self.x + self.w}" y2="{ry}" '
                f'stroke="{ROW_LINE}" stroke-width="0.8"/>'
            )
            ty = ry + 17
            for c, cell in enumerate(row):
                if not cell:
                    continue
                cx = self.col_x(c) + PAD_X
                bold = ' font-weight="600"' if c == 1 else ""
                color = TEXT_COLOR if c < 3 else NOTE_COLOR
                size = 12 if c < 3 else 11
                out.append(
                    f'<text x="{cx}" y="{ty}" font-family="{FONT}" font-size="{size}"{bold} '
                    f'fill="{color}">{esc(cell)}</text>'
                )

        # separadores verticales de las tres primeras columnas
        for c in range(1, 4):
            vx = self.col_x(c)
            out.append(
                f'<line x1="{vx}" y1="{self.y + HEADER_H}" x2="{vx}" y2="{self.bottom}" '
                f'stroke="{ROW_LINE}" stroke-width="0.8"/>'
            )

        return "\n".join(out)


def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


# ── Cardinalidades (pata de gallo) ────────────────────────────────────────────

def marker(x, y, kind, facing):
    """
    Dibuja la cardinalidad en (x, y) sobre una linea vertical.
    facing = 'down' si la linea continua hacia abajo desde el simbolo, 'up' si hacia arriba.
    """
    d = 1 if facing == "down" else -1
    p = []
    if kind in ("one", "zero_or_one"):
        p.append(f'<line x1="{x - 9}" y1="{y + d * 10}" x2="{x + 9}" y2="{y + d * 10}" '
                 f'stroke="{LINE_COLOR}" stroke-width="1.4"/>')
        if kind == "zero_or_one":
            p.append(f'<circle cx="{x}" cy="{y + d * 20}" r="4.5" fill="#FFFFFF" '
                     f'stroke="{LINE_COLOR}" stroke-width="1.4"/>')
    else:  # many / zero_or_many
        tip, base = y, y + d * 13
        p.append(f'<line x1="{x}" y1="{tip}" x2="{x - 9}" y2="{base}" stroke="{LINE_COLOR}" stroke-width="1.4"/>')
        p.append(f'<line x1="{x}" y1="{tip}" x2="{x}"     y2="{base}" stroke="{LINE_COLOR}" stroke-width="1.4"/>')
        p.append(f'<line x1="{x}" y1="{tip}" x2="{x + 9}" y2="{base}" stroke="{LINE_COLOR}" stroke-width="1.4"/>')
        if kind == "zero_or_many":
            p.append(f'<circle cx="{x}" cy="{y + d * 22}" r="4.5" fill="#FFFFFF" '
                     f'stroke="{LINE_COLOR}" stroke-width="1.4"/>')
    return "\n".join(p)


def connect(parent, child, label, bus_y, px_off=0.5, cx_off=0.5,
            parent_card="one", child_card="zero_or_many",
            via_x=None, lane_y=None, label_x=None):
    """
    Conector ortogonal: sale del borde inferior del padre, baja al carril bus_y,
    corre en horizontal y baja al borde superior del hijo.

    via_x/lane_y insertan un corredor vertical intermedio para bordear las tablas
    que quedan entre el padre y el hijo, en vez de atravesarlas.
    """
    x1 = parent.x + parent.w * px_off
    x2 = child.x + child.w * cx_off
    y1, y2 = parent.bottom, child.y

    if via_x is None:
        path = f"M {x1} {y1} L {x1} {bus_y} L {x2} {bus_y} L {x2} {y2}"
    else:
        path = (f"M {x1} {y1} L {x1} {lane_y} L {via_x} {lane_y} "
                f"L {via_x} {bus_y} L {x2} {bus_y} L {x2} {y2}")

    out = [f'<path d="{path}" fill="none" stroke="{LINE_COLOR}" stroke-width="1.4"/>']
    out.append(marker(x1, y1, parent_card, "down"))
    out.append(marker(x2, y2, child_card, "up"))

    lx = label_x if label_x is not None else (x1 + x2) / 2
    out.append(
        f'<rect x="{lx - len(label) * 3.6 - 6}" y="{bus_y - 10}" width="{len(label) * 7.2 + 12}" '
        f'height="17" fill="#FFFFFF" stroke="none"/>'
    )
    out.append(
        f'<text x="{lx}" y="{bus_y + 3}" font-family="{FONT}" font-size="12" '
        f'fill="{NOTE_COLOR}" text-anchor="middle" font-style="italic">{esc(label)}</text>'
    )
    return "\n".join(out)


# ── Modelo ────────────────────────────────────────────────────────────────────

W, H = 2620, 1790

users = Table("users", 700, 60, 620, [
    ("UUID",         "id",              "PK", ""),
    ("varchar(150)", "username",        "UK", "not null"),
    ("varchar(20)",  "user_type",       "",   "not null; SECURITY_OFFICER | CLIENT_USER"),
    ("varchar(255)", "password_hash",   "",   "nullable; bcrypt (factor >= 12)"),
    ("varchar(255)", "keycloak_sub",    "UK", "nullable; unique parcial (IS NOT NULL)"),
    ("boolean",      "is_active",       "",   "not null"),
    ("integer",      "failed_attempts", "",   "not null; bloqueo a los 5 intentos"),
    ("timestamptz",  "locked_until",    "",   "nullable"),
    ("timestamptz",  "created_at",      "",   "not null"),
    ("timestamptz",  "updated_at",      "",   "nullable"),
    ("varchar(255)", "totp_secret",     "",   "nullable; cifrado AES-GCM (Key Vault)"),
    ("boolean",      "mfa_enabled",     "",   "not null; default false"),
    ("boolean",      "is_interactive",  "",   "not null; default true"),
])

roles = Table("roles", 60, 60, 560, [
    ("UUID",         "id",          "PK", ""),
    ("varchar(50)",  "name",        "UK", "not null; ADMIN | VIEWER | ..."),
    ("varchar(255)", "description", "",   "nullable"),
    ("boolean",      "is_active",   "",   "not null"),
    ("timestamptz",  "created_at",  "",   "not null"),
    ("timestamptz",  "updated_at",  "",   "nullable"),
])

protected_services = Table("protected_services", 1400, 60, 600, [
    ("UUID",         "id",            "PK", ""),
    ("varchar(150)", "name",          "",   "not null; clusterId de YARP"),
    ("varchar(500)", "upstream_url",  "",   "not null"),
    ("boolean",      "requires_auth", "",   "not null; exige JWT antes de evaluar"),
    ("boolean",      "is_active",     "",   "not null"),
    ("timestamptz",  "created_at",    "",   "not null"),
    ("timestamptz",  "updated_at",    "",   "nullable"),
])

risk_score_config = Table("risk_score_config", 2080, 60, 480, [
    ("UUID",         "id",                  "PK", ""),
    ("numeric(5,4)", "policy_weight",       "",   "not null; Wp = 0.5"),
    ("numeric(5,4)", "anomaly_weight",      "",   "not null; Wa = 0.5"),
    ("numeric(5,2)", "challenge_threshold", "",   "not null; techo ALLOW = 33"),
    ("numeric(5,2)", "block_threshold",     "",   "not null; techo CHALLENGE = 70"),
    ("numeric(5,2)", "cold_start_penalty",  "",   "not null; 30"),
    ("integer",      "cold_start_n",        "",   "not null; 10"),
    ("timestamptz",  "updated_at",          "",   "not null"),
], col_ratios=(0.24, 0.34, 0.07, 0.35))

user_roles = Table("user_roles", 60, 700, 560, [
    ("UUID",        "id",          "PK", ""),
    ("UUID",        "user_id",     "FK", "not null; -> users.id; CASCADE"),
    ("UUID",        "role_id",     "FK", "not null; -> roles.id; CASCADE"),
    ("timestamptz", "assigned_at", "",   "not null; UNIQUE (user_id, role_id)"),
])

user_behavior_profiles = Table("user_behavior_profiles", 700, 700, 600, [
    ("UUID",         "id",                "PK",     ""),
    ("UUID",         "user_id",           "FK, UK", "not null; 1:1 -> users.id; CASCADE"),
    ("jsonb",        "feature_vector",    "",       "nullable; hora sin/cos, frec., diversidad"),
    ("integer",      "access_count",      "",       "not null"),
    ("boolean",      "is_cold_start",     "",       "not null; true si access_count < N"),
    ("numeric(5,2)", "base_risk_penalty", "",       "not null"),
    ("timestamptz",  "last_trained_at",   "",       "nullable"),
], col_ratios=(0.20, 0.30, 0.11, 0.39))

refresh_tokens = Table("refresh_tokens", 1400, 700, 600, [
    ("UUID",         "id",          "PK", ""),
    ("UUID",         "user_id",     "FK", "not null; -> users.id; CASCADE"),
    ("varchar(255)", "token_hash",  "UK", "not null; nunca en claro"),
    ("varchar(255)", "device_info", "",   "nullable"),
    ("timestamptz",  "expires_at",  "",   "not null; emision + 7 dias"),
    ("boolean",      "is_revoked",  "",   "not null; rotacion en cada renovacion"),
    ("timestamptz",  "created_at",  "",   "not null"),
])

audit_logs = Table("audit_logs", 2080, 700, 480, [
    ("UUID",         "id",               "PK", ""),
    ("UUID",         "evaluation_id",    "UK", "not null"),
    ("UUID",         "user_id",          "FK", "nullable; SET NULL"),
    ("UUID",         "service_id",       "FK", "nullable; SET NULL"),
    ("varchar(45)",  "source_ip",        "",   "not null; IPv4/IPv6"),
    ("jsonb",        "geo",              "",   "nullable; pais, ciudad"),
    ("text",         "user_agent",       "",   "nullable; sanitizado"),
    ("varchar(64)",  "fingerprint_hash", "",   "nullable"),
    ("numeric(5,2)", "policy_score",     "",   "not null"),
    ("numeric(5,2)", "anomaly_score",    "",   "not null"),
    ("numeric(5,2)", "risk_score",       "",   "not null; 0-100"),
    ("varchar(10)",  "verdict",          "",   "not null; ALLOW|CHALLENGE|BLOCK"),
    ("jsonb",        "triggered_rules",  "",   "nullable; denormalizado"),
    ("timestamptz",  "evaluated_at",     "",   "not null"),
], col_ratios=(0.24, 0.33, 0.08, 0.35))

access_policies = Table("access_policies", 700, 1260, 700, [
    ("UUID",         "id",         "PK", ""),
    ("varchar(150)", "name",       "",   "not null"),
    ("varchar(30)",  "type",       "",   "GEOFENCE | TIME_WINDOW | FINGERPRINT | IMPOSSIBLE_TRAVEL"),
    ("jsonb",        "config",     "",   "not null; parametros de la regla"),
    ("numeric(4,3)", "weight",     "",   "not null; peso en el Policy Score"),
    ("boolean",      "is_active",  "",   "not null; soft-delete"),
    ("UUID",         "created_by", "FK", "not null; -> users.id; RESTRICT"),
    ("timestamptz",  "created_at", "",   "not null"),
    ("timestamptz",  "updated_at", "",   "nullable"),
], col_ratios=(0.19, 0.24, 0.06, 0.51))

service_policies = Table("service_policies", 1450, 1600, 560, [
    ("UUID",    "id",         "PK", ""),
    ("UUID",    "service_id", "FK", "not null; -> protected_services.id"),
    ("UUID",    "policy_id",  "FK", "not null; -> access_policies.id"),
    ("boolean", "is_enabled", "",   "not null; UNIQUE (service_id, policy_id)"),
])

tables = [users, roles, protected_services, risk_score_config, user_roles,
          user_behavior_profiles, refresh_tokens, audit_logs, access_policies, service_policies]

connections = [
    # users y roles -> RBAC
    connect(roles, user_roles,             "grants",    665, px_off=0.06, cx_off=0.06, child_card="many"),
    connect(users, user_roles,             "assigned",  660, px_off=0.10, cx_off=0.62, child_card="many"),
    connect(users, user_behavior_profiles, "has",       640, px_off=0.34, cx_off=0.35, child_card="zero_or_one"),
    connect(users, refresh_tokens,         "owns",      540, px_off=0.62, cx_off=0.28),
    connect(users, audit_logs,             "generates", 620, px_off=0.88, cx_off=0.30),
    # users -> access_policies: bordea user_behavior_profiles por el corredor derecho
    connect(users, access_policies, "authors", 1200, px_off=0.98, cx_off=0.15,
            via_x=1360, lane_y=470, label_x=1050),
    # servicios y politicas
    connect(protected_services, audit_logs, "targets", 580, px_off=0.75, cx_off=0.75),
    # protected_services -> service_policies: baja por el hueco entre refresh_tokens y audit_logs
    connect(protected_services, service_policies, "scoped by", 1540, px_off=0.97, cx_off=0.85,
            via_x=2040, lane_y=500, label_x=2000),
    connect(access_policies, service_policies, "applied via", 1560, px_off=0.90, cx_off=0.22),
]

# ── Recuadros de apoyo ────────────────────────────────────────────────────────

def panel(x, y, w, title, lines, line_h=21, mono=True):
    """
    Recuadro de apoyo. Con mono=True el cuerpo usa tipografia monoespaciada y conserva
    los espacios (xml:space), que es lo que mantiene alineadas las columnas de las
    listas de claves Redis e indices; SVG los colapsaria por defecto.
    """
    h = 44 + line_h * len(lines)
    body_font = "'Consolas', 'Cascadia Mono', monospace" if mono else FONT
    body_size = 11 if mono else 11.5
    out = [
        f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="#FBFCFE" '
        f'stroke="{BORDER}" stroke-width="1.2" stroke-dasharray="7 4" rx="3"/>',
        f'<text x="{x + 14}" y="{y + 26}" font-family="{FONT}" font-size="14" font-weight="600" '
        f'fill="{TITLE_COLOR}">{esc(title)}</text>',
    ]
    for i, ln in enumerate(lines):
        out.append(
            f'<text x="{x + 14}" y="{y + 48 + i * line_h}" font-family="{body_font}" '
            f'font-size="{body_size}" fill="{NOTE_COLOR}" xml:space="preserve">{esc(ln)}</text>'
        )
    return "\n".join(out)


redis_panel = panel(60, 940, 560, "Estado efimero (Redis) — fuera del modelo relacional", [
    "session:{userId}         15 min   validacion rapida del token activo",
    "blacklist:{jti}          hasta    access tokens revocados",
    "                         exp.",
    "profile:{userId}         1 h      cache del perfil para ML.NET",
    "fingerprint:{userId}     24 h     huellas de dispositivo conocidas",
    "ratelimit:{ip}           60 s     contador de frecuencia por IP",
    "challenge:{challengeId}  2-5 min  desafio MFA pendiente",
    "stepup:{userId}          10 min   ventana de step-up satisfecho",
    "mfaattempts:{userId}     ventana  anti fuerza bruta del TOTP",
])

index_panel = panel(60, 1280, 560, "Indices", [
    "ix_users_username                UNIQUE (username)",
    "ix_users_keycloak_sub            UNIQUE parcial (IS NOT NULL)",
    "ix_roles_name                    UNIQUE (name)",
    "ix_user_roles_user_role          UNIQUE (user_id, role_id)",
    "ix_service_policies_svc_policy   UNIQUE (service_id, policy_id)",
    "ix_user_behavior_profiles_user   UNIQUE (user_id) -> fuerza el 1:1",
    "ix_refresh_tokens_token_hash     UNIQUE (token_hash)",
    "ix_refresh_tokens_user_active    parcial WHERE is_revoked = false",
    "ix_audit_logs_evaluation_id      UNIQUE (evaluation_id)",
    "ix_audit_logs_evaluated_at       B-tree (evaluated_at)",
    "ix_audit_logs_triggered_rules    GIN (triggered_rules)",
])

legend = panel(150, 300, 470, "Leyenda", [
    "PK  clave primaria    FK  clave foranea    UK  unica",
    "",
    "CASCADE   el hijo carece de sentido sin su padre",
    "SET NULL  la auditoria sobrevive al borrado del padre",
    "RESTRICT  no se borra un autor con politicas vigentes",
    "",
    "audit_logs es append-only: no se actualiza ni se borra.",
    "access_policies <-> audit_logs es N:M denormalizada en",
    "triggered_rules (jsonb), sin tabla de union, por rendimiento.",
    "risk_score_config es una fila unica global, sin claves foraneas.",
])

nota_umbrales = panel(2080, 1180, 480, "Nota sobre los umbrales", [
    "El SRS §7.6 nombra los umbrales por el veredicto",
    "que abren y la entidad por el que cierra. Mapeo 1:1:",
    "",
    "SRS allow_threshold     = challenge_threshold",
    "SRS challenge_threshold = block_threshold",
])

title = (
    f'<text x="{W/2}" y="36" font-family="{FONT}" font-size="20" font-weight="600" '
    f'fill="{TITLE_COLOR}" text-anchor="middle">'
    f'Omakase-Gateway — Modelo de datos relacional (PostgreSQL, 10 entidades)</text>'
)

svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}">
<rect width="{W}" height="{H}" fill="#FFFFFF"/>
{title}
{chr(10).join(connections)}
{chr(10).join(t.render() for t in tables)}
{redis_panel}
{index_panel}
{legend}
{nota_umbrales}
</svg>
'''

out = Path(__file__).parent / "figura-08-modelo-de-datos.svg"
out.write_text(svg, encoding="utf-8")
print(f"OK -> {out}  ({len(svg)} bytes)")

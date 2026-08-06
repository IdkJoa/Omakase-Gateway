"""
Edita la tesis: inserta las Figuras 6 a 11, completa las Tablas 3, 4 y 5 con los datos
medidos, convierte los pies de tabla y figura en epigrafes numerados automaticamente
(campos SEQ) y corrige las inconsistencias detectadas.

Opera sobre unpacked/ (ya pasada por merge_runs.py).
Todas las referencias a elementos se capturan ANTES de mutar el arbol, porque cada
insercion o borrado desplaza los indices de los parrafos siguientes.
"""
import shutil, struct
from pathlib import Path
from lxml import etree

NS = {
    'w':  'http://schemas.openxmlformats.org/wordprocessingml/2006/main',
    'r':  'http://schemas.openxmlformats.org/officeDocument/2006/relationships',
    'wp': 'http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing',
    'a':  'http://schemas.openxmlformats.org/drawingml/2006/main',
    'pic': 'http://schemas.openxmlformats.org/drawingml/2006/picture',
}
W = '{%s}' % NS['w']
XMLSPACE = '{http://www.w3.org/XML/1998/namespace}space'
UNP = Path('unpacked')
DIAG = Path('C:/Omakase-Gateway/docs/diagramas')
TEXT_EMU = 5486400          # 6" de ancho util (12240 - 2160 - 1440 twips)

tree = etree.parse(str(UNP / 'word/document.xml'))
body = tree.getroot().find(W + 'body')

# ══ Constructores ═════════════════════════════════════════════════════════════

def run(text=None, bold=False, italic=False, sz=None, brk=False):
    r = etree.Element(W + 'r')
    if bold or italic or sz:
        rpr = etree.SubElement(r, W + 'rPr')
        if bold:   etree.SubElement(rpr, W + 'b')
        if italic: etree.SubElement(rpr, W + 'i')
        if sz:     etree.SubElement(rpr, W + 'sz').set(W + 'val', str(sz))
    if brk:
        etree.SubElement(r, W + 'br')
    if text is not None:
        t = etree.SubElement(r, W + 't')
        t.text = text
        t.set(XMLSPACE, 'preserve')
    return r


def para(runs=(), style=None, keep_next=False, center=False, tight=False):
    p = etree.Element(W + 'p')
    if style or keep_next or center or tight:
        ppr = etree.SubElement(p, W + 'pPr')
        if style:
            etree.SubElement(ppr, W + 'pStyle').set(W + 'val', style)
        if keep_next:
            etree.SubElement(ppr, W + 'keepNext')
        if tight:
            sp = etree.SubElement(ppr, W + 'spacing')
            sp.set(W + 'line', '240'); sp.set(W + 'lineRule', 'auto')
        if center:
            etree.SubElement(ppr, W + 'jc').set(W + 'val', 'center')
    for rr in runs:
        p.append(rr)
    return p


def caption(kind, title):
    """
    Epigrafe APA en un solo parrafo: 'Tabla N', salto de linea, titulo en cursiva.

    Va en un unico parrafo y no en dos porque el indice de ilustraciones de Word recoge
    el parrafo completo que contiene el campo SEQ; separarlos produciria una entrada con
    el numero y otra con el titulo.
    """
    p = para(style='Caption', keep_next=True)
    p.append(run(f'{kind} ', bold=True))
    fld = etree.SubElement(p, W + 'fldSimple')
    fld.set(W + 'instr', f' SEQ {kind} \\* ARABIC ')
    fld.append(run('1', bold=True))
    p.append(run(brk=True))
    p.append(run(title, italic=True))
    return p


def numerar_epigrafes(body):
    """
    Escribe en cada campo SEQ el numero que le corresponde por su posicion.

    Word recalcula los campos al actualizarlos, pero hasta entonces muestra el valor
    almacenado: sin este paso el documento se abriria con todos los epigrafes diciendo
    «Tabla 1» y «Figura 1».
    """
    cuenta = {}
    for p in body.iter(W + 'p'):
        fld = p.find(W + 'fldSimple')
        if fld is None:
            continue
        instr = fld.get(W + 'instr') or ''
        if ' SEQ ' not in instr:
            continue
        kind = instr.split()[1]
        cuenta[kind] = cuenta.get(kind, 0) + 1
        for t in fld.iter(W + 't'):
            t.text = str(cuenta[kind])
    return cuenta


def nota(texto):
    return para([run('Nota. ', italic=True, sz=20), run(texto, sz=20)], tight=True)


def cuerpo(texto):
    return para([run(texto)])


def vacio():
    return para()


def toc(label):
    p = etree.Element(W + 'p')
    r1 = etree.SubElement(p, W + 'r')
    fc = etree.SubElement(r1, W + 'fldChar')
    fc.set(W + 'fldCharType', 'begin'); fc.set(W + 'dirty', 'true')
    r2 = etree.SubElement(p, W + 'r')
    it = etree.SubElement(r2, W + 'instrText')
    it.set(XMLSPACE, 'preserve')
    it.text = f' TOC \\h \\z \\c "{label}" '
    r3 = etree.SubElement(p, W + 'r')
    etree.SubElement(r3, W + 'fldChar').set(W + 'fldCharType', 'separate')
    p.append(run(f'Sitúe el cursor aquí y pulse F9 (o clic derecho › Actualizar campos) '
                 f'para generar el índice de {label.lower()}s.', italic=True, sz=20))
    r5 = etree.SubElement(p, W + 'r')
    etree.SubElement(r5, W + 'fldChar').set(W + 'fldCharType', 'end')
    return p


def png_size(path):
    return struct.unpack('>II', Path(path).read_bytes()[16:24])


def tabla(headers, rows, widths):
    tbl = etree.Element(W + 'tbl')
    pr = etree.SubElement(tbl, W + 'tblPr')
    tw = etree.SubElement(pr, W + 'tblW'); tw.set(W + 'w', '0'); tw.set(W + 'type', 'auto')
    bs = etree.SubElement(pr, W + 'tblBorders')
    for side in ('top', 'bottom'):
        b = etree.SubElement(bs, W + side)
        b.set(W + 'val', 'single'); b.set(W + 'sz', '6'); b.set(W + 'space', '0'); b.set(W + 'color', '000000')
    etree.SubElement(pr, W + 'tblLayout').set(W + 'type', 'fixed')
    lk = etree.SubElement(pr, W + 'tblLook')
    for k, v in [('val', '04A0'), ('firstRow', '1'), ('lastRow', '0'),
                 ('firstColumn', '1'), ('lastColumn', '0'), ('noHBand', '0'), ('noVBand', '1')]:
        lk.set(W + k, v)
    grid = etree.SubElement(tbl, W + 'tblGrid')
    for wd in widths:
        etree.SubElement(grid, W + 'gridCol').set(W + 'w', str(wd))

    def fila(cells, header=False):
        tr = etree.SubElement(tbl, W + 'tr')
        for i, txt in enumerate(cells):
            tc = etree.SubElement(tr, W + 'tc')
            tcpr = etree.SubElement(tc, W + 'tcPr')
            cw = etree.SubElement(tcpr, W + 'tcW'); cw.set(W + 'w', str(widths[i])); cw.set(W + 'type', 'dxa')
            if header:
                tb = etree.SubElement(tcpr, W + 'tcBorders')
                bb = etree.SubElement(tb, W + 'bottom')
                bb.set(W + 'val', 'single'); bb.set(W + 'sz', '6'); bb.set(W + 'space', '0'); bb.set(W + 'color', '000000')
            tc.append(para([run(txt, bold=header, sz=22)], center=(i > 0), tight=True))
    fila(headers, header=True)
    for rw in rows:
        fila(rw)
    return tbl


DRAWING = (
    '<w:p xmlns:w="{w}" xmlns:r="{r}" xmlns:wp="{wp}" xmlns:a="{a}" xmlns:pic="{pic}">'
    '<w:pPr><w:keepNext/><w:spacing w:line="240" w:lineRule="auto"/><w:jc w:val="center"/></w:pPr>'
    '<w:r><w:rPr><w:noProof/></w:rPr><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0">'
    '<wp:extent cx="{cx}" cy="{cy}"/><wp:effectExtent l="0" t="0" r="0" b="0"/>'
    '<wp:docPr id="{did}" name="Figura {num}"/>'
    '<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect="1"/></wp:cNvGraphicFramePr>'
    '<a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">'
    '<pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="{fname}"/><pic:cNvPicPr/></pic:nvPicPr>'
    '<pic:blipFill><a:blip r:embed="{rid}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>'
    '<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm>'
    '<a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic>'
    '</a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>')

FIGS = {
    6:  'figura-06-arquitectura.png',
    7:  'figura-07-flujo-de-evaluacion.png',
    8:  'figura-08-modelo-de-datos.png',
    9:  'figura-09-despliegue.png',
    10: 'figura-10-latencia.png',
    11: 'figura-11-separacion-poblaciones.png',
}
RID = {n: f'rIdFig{n}' for n in FIGS}


def imagen(num):
    w, h = png_size(DIAG / FIGS[num])
    cy = int(TEXT_EMU * h / w)
    return etree.fromstring(DRAWING.format(
        w=NS['w'], r=NS['r'], wp=NS['wp'], a=NS['a'], pic=NS['pic'],
        cx=TEXT_EMU, cy=cy, did=90000 + num, num=num, fname=FIGS[num], rid=RID[num]))


def sustituir_texto(p, viejo, nuevo):
    """
    Sustituye una cadena dentro de un parrafo, incluso cuando Word la ha repartido entre
    varios runs (lo hace por marcas de revision o de correccion ortografica, de modo que
    una frase visible en pantalla no existe como cadena contigua en el XML).

    El texto nuevo se deposita en el primer run afectado, que es el que aporta el formato
    dominante del fragmento; de los demas se recorta solo la parte cubierta.
    """
    ts = [t for t in p.iter(W + 't')]
    full, spans, pos = '', [], 0
    for t in ts:
        s = t.text or ''
        spans.append((t, pos, pos + len(s)))
        full += s
        pos += len(s)
    i = full.find(viejo)
    if i < 0:
        return False
    j = i + len(viejo)
    primero = True
    for t, a, b in spans:
        if b <= i or a >= j:
            continue
        s = t.text or ''
        ini, fin = max(i, a) - a, min(j, b) - a
        t.text = s[:ini] + (nuevo if primero else '') + s[fin:]
        t.set(XMLSPACE, 'preserve')
        primero = False
    return True


def insertar_tras(ref, elementos):
    padre = ref.getparent()
    pos = list(padre).index(ref)
    for k, e in enumerate(elementos, start=1):
        padre.insert(pos + k, e)


def reemplazar(ref, elementos):
    """Sustituye ref por los elementos dados y devuelve el ultimo, util como ancla
    para insertar despues: la referencia original ya no esta en el arbol."""
    padre = ref.getparent()
    pos = list(padre).index(ref)
    for k, e in enumerate(elementos):
        padre.insert(pos + k, e)
    padre.remove(ref)
    return elementos[-1]


# ══ Referencias capturadas antes de mutar ═════════════════════════════════════

P = {i: body[i] for i in [
    42, 44, 63, 67, 83, 229,
    237, 239, 241, 243, 245, 246, 247,
    250, 251, 255, 256, 260, 261,
    263, 265, 266, 268, 270, 271,
]}
CAPS = [(body[i], body[i + 1], kind) for i, kind in [
    (182, 'Tabla'), (189, 'Figura'), (194, 'Figura'), (199, 'Tabla'),
    (206, 'Figura'), (214, 'Figura'), (220, 'Figura'),
    (248, 'Tabla'), (253, 'Tabla'), (258, 'Tabla'),
    (263, 'Figura'), (268, 'Figura'),
    (285, 'Tabla'), (292, 'Tabla'),
]]

# ══ 1. Estilo Caption ═════════════════════════════════════════════════════════

st_path = UNP / 'word/styles.xml'
st_tree = etree.parse(str(st_path))
st_root = st_tree.getroot()
if not st_root.xpath('//w:style[@w:styleId="Caption"]', namespaces=NS):
    s = etree.SubElement(st_root, W + 'style')
    s.set(W + 'type', 'paragraph'); s.set(W + 'styleId', 'Caption')
    etree.SubElement(s, W + 'name').set(W + 'val', 'caption')
    etree.SubElement(s, W + 'basedOn').set(W + 'val', 'Normal')
    etree.SubElement(s, W + 'next').set(W + 'val', 'Normal')
    etree.SubElement(s, W + 'uiPriority').set(W + 'val', '35')
    etree.SubElement(s, W + 'qFormat')
    etree.SubElement(etree.SubElement(s, W + 'pPr'), W + 'keepNext')
    st_tree.write(str(st_path), xml_declaration=True, encoding='UTF-8', standalone=True)
    print('  + estilo Caption')

# ══ 2. Imagenes y relaciones ══════════════════════════════════════════════════

rels_path = UNP / 'word/_rels/document.xml.rels'
rels = etree.parse(str(rels_path))
RNS = 'http://schemas.openxmlformats.org/package/2006/relationships'
for i, num in enumerate(sorted(FIGS), start=1):
    dest = f'image{6 + i}.png'
    shutil.copy(DIAG / FIGS[num], UNP / 'word/media' / dest)
    rel = etree.SubElement(rels.getroot(), '{%s}Relationship' % RNS)
    rel.set('Id', RID[num])
    rel.set('Type', 'http://schemas.openxmlformats.org/officeDocument/2006/relationships/image')
    rel.set('Target', f'media/{dest}')
rels.write(str(rels_path), xml_declaration=True, encoding='UTF-8', standalone=True)
print(f'  + {len(FIGS)} imagenes')

# ══ 3. Indices automaticos ════════════════════════════════════════════════════

reemplazar(P[42], [toc('Tabla')])
reemplazar(P[44], [toc('Figura')])
print('  + campos TOC de tablas y figuras')

# ══ 4. Correcciones de redaccion ══════════════════════════════════════════════

corr = 0
for idx, viejo, nuevo in [
    (63, 'huella de dispositivo, viaje imposible y límite de tasa',
         'huella de dispositivo y viaje imposible, con un límite de tasa por dirección IP como '
         'control previo a la evaluación'),
    (67, 'impossible travel and rate limiting',
         'and impossible travel, with per-IP rate limiting as a pre-evaluation control'),
    (83, 'huella de dispositivo, viaje imposible y límite de tasa',
         'huella de dispositivo y viaje imposible, junto con un límite de tasa por dirección IP '
         'aplicado antes de la evaluación'),
    (229, 'huella de dispositivo, viaje imposible y límite de tasa',
          'huella de dispositivo y viaje imposible, precedidas de un límite de tasa por dirección IP'),
]:
    if sustituir_texto(P[idx], viejo, nuevo):
        corr += 1
    else:
        print(f'  ! no se pudo sustituir en el parrafo {idx}: {viejo[:40]}...')
print(f'  + {corr}/4 correcciones de «límite de tasa»')

# ══ 5. Figuras 6 a 9 en el apartado 4.2.4 ═════════════════════════════════════

NOTAS_FIG = {
    6: 'La línea continua representa la ruta de la petición y las llamadas activas; la '
       'discontinua, las dependencias de soporte y la redirección de identidad. El motor de '
       'reglas y el modelo de anomalías se ejecutan dentro del mismo proceso que la pasarela, '
       'de modo que la evaluación no incurre en latencia de red entre componentes. '
       'Elaboración propia.',
    7: 'Los umbrales representados (33 y 70) y los pesos (0,5 y 0,5) son los valores calibrados '
       'empíricamente y residen en la tabla risk_score_config, no en el código. El step-up '
       'mediante segundo factor ocurre fuera de banda y, por tanto, no se contabiliza en el '
       'presupuesto de latencia de la evaluación. Elaboración propia.',
    8: 'Los tipos, la nulabilidad, las claves y las reglas de integridad referencial se '
       'transcriben de la configuración de persistencia del sistema, de modo que el diagrama '
       'es verificable contra la base de datos real. Los umbrales de risk_score_config figuran '
       'con el nombre de la columna; su equivalencia con la nomenclatura de la especificación de '
       'requisitos se indica en el propio diagrama. Elaboración propia.',
    9: 'La línea continua representa el tráfico de peticiones y la discontinua las dependencias '
       'internas y la telemetría. Las réplicas insinuadas del contenedor de la pasarela ilustran '
       'su escalabilidad horizontal: como el estado compartido reside en Redis y PostgreSQL y no '
       'en la memoria de cada instancia, las réplicas no necesitan coordinarse entre sí. '
       'Elaboración propia.',
}
TITULOS_FIG = {
    6: 'Arquitectura general de la solución',
    7: 'Flujo de evaluación de una petición, de la intercepción al veredicto',
    8: 'Modelo de datos relacional del sistema',
    9: 'Vista de despliegue de los componentes en tiempo de ejecución',
}
for num, ref in [(6, P[237]), (7, P[239]), (8, P[241]), (9, P[243])]:
    insertar_tras(ref, [caption('Figura', TITULOS_FIG[num]), imagen(num),
                        nota(NOTAS_FIG[num]), vacio()])
print('  + Figuras 6 a 9 insertadas')

# ══ 6. Apartado 4.2.5 ═════════════════════════════════════════════════════════

sustituir_texto(
    P[245],
    'La validación de estos aspectos técnicos, el cumplimiento del presupuesto de latencia '
    '(p95 ≤ 50 ms), las tasas de detección de anomalías del modelo y el comportamiento '
    'fail-closed ante fallos, se presentará en este mismo apartado una vez concluidas las '
    'pruebas con el banco de casos etiquetados.',
    'La validación de estos aspectos técnicos —el cumplimiento del presupuesto de latencia '
    '(p95 ≤ 50 ms), el desempeño de la detección sobre el banco de casos etiquetados y el '
    'comportamiento del sistema ante el fallo de sus dependencias— se presenta a continuación.')

sustituir_texto(
    P[246],
    'el desempeño del modelo de detección de anomalías sobre un banco de casos etiquetados y el '
    'comportamiento del sistema ante la caída de sus componentes (principio fail-closed). '
    'A continuación se presenta el formato definitivo en que se reportarán estos resultados.',
    'el desempeño de la detección sobre un banco de casos etiquetados y el comportamiento del '
    'sistema ante la caída de sus componentes (principio fail-closed). A continuación se '
    'presentan los resultados obtenidos para cada uno de ellos.')

# El parrafo «(Seccion pendiente...)» se sustituye por la narrativa de resultados.
reemplazar(P[247], [cuerpo(
    'El presupuesto de latencia se cumple con margen: el percentil 95 del recorrido completo se '
    'situó en 27,72 ms frente a los 50 ms exigidos, sobre 201.217 evaluaciones realizadas con '
    'cien usuarios virtuales concurrentes durante cinco minutos y sin fallos de transporte. '
    'Alcanzar esa cifra, sin embargo, no fue inmediato. La primera corrida instrumentada arrojó '
    'un percentil 95 de 103,56 ms e incumplió el requisito, lo que obligó a medir el coste de '
    'cada fase de la evaluación. El desglose mostró que el 83 % del presupuesto se consumía en '
    'operaciones de entrada y salida —dos consultas a la base de datos por petición para '
    'resolver las políticas del servicio, otra para la configuración del motor y una doble '
    'resolución del perfil de comportamiento dentro de la misma evaluación—, mientras que la '
    'inferencia del modelo de aprendizaje automático representaba apenas el 16,7 %. El hallazgo '
    'merece subrayarse porque contradice la intuición: el componente de aprendizaje automático, '
    'candidato natural a ser el cuello de botella, no lo era. Corregidas las lecturas repetidas '
    'mediante cachés de vigencia breve y una memorización del perfil por petición, el percentil '
    '95 descendió a 27,72 ms, una reducción del 73 %, y el rendimiento sostenido aumentó un 23 % '
    'con la misma carga. La Tabla 3 resume las mediciones y la Figura 10 las contrasta con el '
    'presupuesto de diseño.')])

# Tabla 3
reemplazar(P[250], [tabla(
    ['Métrica', 'Objetivo de diseño', 'Valor medido'],
    [
        ['Overhead extremo a extremo (p95)', '≤ 50 ms', '27,72 ms'],
        ['Overhead extremo a extremo (p90)', '—', '19,27 ms'],
        ['Overhead extremo a extremo (media)', '—', '13,59 ms'],
        ['Latencia del motor de evaluación (p95)', '—', '5,28 – 19,47 ms'],
        ['Latencia del motor de evaluación (p50)', '—', '3,69 – 4,90 ms'],
        ['Peticiones que exceden el presupuesto', '—', '0,33 % (660 de 201.217)'],
        ['Rendimiento sostenido', '—', '558,9 peticiones/s'],
        ['Eventos de auditoría descartados', '0', '0 de 201.217'],
    ],
    [3312, 2016, 2736])])
reemplazar(P[251], [nota(
    'Medición realizada el 6 de agosto de 2026 sobre 201.217 evaluaciones, con cien usuarios '
    'virtuales concurrentes durante cinco minutos. La cifra del generador de carga comprende el '
    'recorrido completo de la petición —cabeceras de seguridad, límite de tasa, autenticación, '
    'evaluación y reenvío al servicio de destino—, por lo que acota superiormente el overhead de '
    'evaluación que exige el requisito. La latencia del motor se obtiene de la telemetría y se '
    'expresa como rango porque varía entre las ventanas observadas. Elaboración propia.')])

# Tabla 4
reemplazar(P[255], [tabla(
    ['Resultado', 'Valor', 'Cálculo'],
    [
        ['Verdaderos positivos (maliciosos no permitidos)', '6', '—'],
        ['Falsos negativos (maliciosos permitidos)', '0', '—'],
        ['Verdaderos negativos (legítimos permitidos)', '3', '—'],
        ['Falsos positivos (legítimos no permitidos)', '0', '—'],
        ['Tasa de detección (sensibilidad)', '100 %', '6 / 6'],
        ['Tasa de falsos negativos', '0 %', '0 / 6'],
        ['Tasa de falsos positivos', '0 %', '0 / 3'],
        ['Precisión del veredicto (exactitud)', '100 %', '9 / 9'],
    ],
    [4032, 1728, 2304])])
nota4 = reemplazar(P[256], [nota(
    'Resultados de la ejecución controlada de los cinco escenarios de ataque y de los casos '
    'legítimos de contraste, con la configuración calibrada (peso de política 0,5; peso de '
    'anomalía 0,5; umbral de desafío 33; umbral de bloqueo 70). Elaboración propia.')])

# Tabla 5
reemplazar(P[260], [tabla(
    ['Escenario de fallo', 'Comportamiento requerido', 'Estado'],
    [
        ['Indisponibilidad de Redis', 'Denegar la petición y registrar el evento (fail-closed)',
         'Pendiente de verificación'],
        ['Indisponibilidad de PostgreSQL', 'Denegar la petición y registrar el evento (fail-closed)',
         'Pendiente de verificación'],
        ['Fallo de inferencia del modelo de ML.NET',
         'Operar solo con la capa determinista; puntaje de anomalía neutro de 50',
         'Cumple (prueba automatizada)'],
        ['Timeout del servicio de geolocalización',
         'Omitir la regla de viaje imposible y elevar el puntaje de política base',
         'Cumple (prueba automatizada)'],
        ['Indisponibilidad de Keycloak',
         'Los administradores no pueden autenticarse; el flujo de usuarios cliente sigue operativo',
         'Pendiente de verificación'],
        ['Redis no responde al verificar el step-up',
         'El desafío no puede completarse: escala a bloqueo (fail-closed)',
         'Pendiente de verificación'],
        ['Usuario cliente no interactivo con veredicto de desafío',
         'Escala a bloqueo, registrado de forma distinguible',
         'Cumple (prueba automatizada)'],
        ['Azure Key Vault no responde en el arranque',
         'El sistema no arranca; el fallo se registra de forma explícita',
         'Pendiente de verificación'],
    ],
    [2448, 3600, 2016])])
nota5 = reemplazar(P[261], [nota(
    'El comportamiento requerido corresponde a la política de degradación segura del sistema. '
    'Las filas marcadas como cumplidas están respaldadas por pruebas automatizadas que se '
    'ejecutan en la integración continua; las restantes quedan pendientes de la verificación '
    'sobre el entorno desplegado. Elaboración propia.')])

# Figura 10
reemplazar(P[265], [imagen(10)])
reemplazar(P[266], [nota(
    'Las tres primeras barras corresponden al recorrido completo de la petición medido con el '
    'generador de carga; las dos últimas, al tiempo propio del motor de evaluación obtenido de '
    'la telemetría, para el que se representa el extremo superior del rango observado. '
    'Elaboración propia.')])

# Figura 11 (sustituye a la curva ROC prevista en el diseno original)
reemplazar(P[270], [imagen(11)])
nota11 = reemplazar(P[271], [nota(
    'El modelo de detección de anomalías es no supervisado y el banco de casos etiquetados '
    'consta de nueve observaciones, condiciones bajo las cuales una curva ROC y su área bajo la '
    'curva no serían una medida sino una extrapolación sin respaldo. En su lugar se representa '
    'la separación efectivamente observada entre las poblaciones de puntajes, que es medible y '
    'que además justifica la ubicación de cada umbral. Elaboración propia.')])

print('  + apartado 4.2.5 completado')

# ══ 7. Parrafos anadidos al 4.2.5 ═════════════════════════════════════════════

insertar_tras(nota4, [vacio(), cuerpo(
    'Estas cifras deben leerse con la cautela que impone su origen. Corresponden a una ejecución '
    'controlada de cada escenario y no a un muestreo poblacional: con nueve casos, el intervalo '
    'de confianza de una proporción del cien por ciento es demasiado amplio para sostener una '
    'afirmación sobre población. La lectura correcta es que, en la ejecución controlada de los '
    'cinco escenarios, el sistema detectó la totalidad de los accesos maliciosos sin producir '
    'falsos positivos, lo que evidencia que el mecanismo discrimina en los casos previstos y '
    'sustenta la calibración adoptada, pero no que esa tasa se mantenga en operación real.')])

insertar_tras(nota11, [vacio(), cuerpo(
    'La validación dejó al descubierto cuatro limitaciones que conviene declarar. La primera es '
    'que el propio mecanismo de aprendizaje admite una vía de evasión por sondeo progresivo: '
    'como las primeras peticiones de una ráfaga se permiten y alimentan el perfil, repetir el '
    'mismo ataque lo vuelve progresivamente menos anómalo; a lo largo de una sesión de pruebas '
    'el puntaje de anomalía de una misma ráfaga descendió de 98 a 68,5. La segunda es de método: '
    'un modelo que aprende el comportamiento de cada usuario no admite una prueba de estrés en '
    'sentido estricto, porque cualquier carga superior a una petición por minuto y por usuario '
    'es, por construcción, máximamente anómala para ese usuario; no existe, por tanto, tráfico '
    'legítimo de alto volumen contra el que contrastar. La tercera afecta a la medición: el '
    'límite de tasa se ejecuta antes del motor, de modo que una prueba que no lo eleve mide el '
    'limitador y no la evaluación; una corrida preliminar confirmó que el 94,67 % del tráfico '
    'se detenía antes de ser evaluado. La cuarta es que las cachés introducidas para cumplir el '
    'presupuesto de latencia abren una ventana de hasta cinco segundos hasta que un cambio '
    'administrativo surte efecto, parámetro configurable y desactivable. Estas limitaciones no '
    'invalidan los resultados; delimitan su alcance y orientan las mejoras futuras.')])

print('  + parrafos de cautela y limitaciones')

# ══ 8. Epigrafes existentes -> campo SEQ ══════════════════════════════════════

# La Tabla 4 pasa a recoger el desempeno de la deteccion completa —no solo del modelo— y
# la Figura 11 sustituye la curva ROC prevista por la separacion de poblaciones observada.
RETITULAR = {
    253: 'Desempeño de la detección sobre el banco de casos etiquetados',
    263: 'Latencia observada frente al presupuesto de evaluación',
    268: 'Separación de poblaciones del Risk Score y ubicación de los umbrales',
}
for (p_num, p_tit, kind), (idx, _) in zip(CAPS, [(i, k) for i, k in [
        (182, 'Tabla'), (189, 'Figura'), (194, 'Figura'), (199, 'Tabla'),
        (206, 'Figura'), (214, 'Figura'), (220, 'Figura'),
        (248, 'Tabla'), (253, 'Tabla'), (258, 'Tabla'),
        (263, 'Figura'), (268, 'Figura'),
        (285, 'Tabla'), (292, 'Tabla')]]):
    titulo = RETITULAR.get(idx) or ''.join(p_tit.itertext()).strip()
    reemplazar(p_num, [caption(kind, titulo)])
    p_tit.getparent().remove(p_tit)
print(f'  + {len(CAPS)} epígrafes existentes convertidos a campo SEQ')

conteo = numerar_epigrafes(body)
print('  + numeración cacheada: ' + ', '.join(f'{k} 1..{v}' for k, v in conteo.items()))

# ══ Guardar ═══════════════════════════════════════════════════════════════════

tree.write(str(UNP / 'word/document.xml'), xml_declaration=True, encoding='UTF-8', standalone=True)
print('LISTO')

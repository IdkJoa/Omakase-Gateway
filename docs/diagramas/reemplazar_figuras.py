"""
Sustituye las seis figuras del capitulo IV por las versiones rediseniadas y coloca en
pagina apaisada las cuatro del apartado 4.2.4.

Motivo del rediseno: las primeras versiones se dibujaron en lienzos de 2100 a 2620 px
con letra de 11-12 px. Al reducirlas al ancho de la caja de texto la letra quedaba por
debajo de los 5 px y no se leia. Las nuevas usan lienzos de ~1400 px con letra de 17-21,
verificadas rasterizando al ancho fisico real de la pagina.

La Figura 8 ya tenia su seccion apaisada; aqui se anaden las de las Figuras 6, 7 y 9.
Las Figuras 10 y 11 se quedan en vertical: son graficos sencillos y ya se comprobo que
se leen a 6 pulgadas.
"""
import shutil, struct
from pathlib import Path
from lxml import etree

NS = {'w': 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
W = '{%s}' % NS['w']
WP = '{http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing}'
A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'
UNP = Path('unpacked')
DIAG = Path('C:/Omakase-Gateway/docs/diagramas')

ANCHO_APAISADO = 7498080     # 8,2"
ANCHO_VERTICAL = 5486400     # 6,0"

FIGS = {
    6:  ('figura-06-arquitectura.png',            'image7.png',  True),
    7:  ('figura-07-flujo-de-evaluacion.png',     'image8.png',  True),
    8:  ('figura-08-modelo-de-datos.png',         'image9.png',  True),
    9:  ('figura-09-despliegue.png',              'image10.png', True),
    10: ('figura-10-latencia.png',                'image11.png', False),
    11: ('figura-11-separacion-poblaciones.png',  'image12.png', False),
}


def png_size(p):
    return struct.unpack('>II', Path(p).read_bytes()[16:24])


tree = etree.parse(str(UNP / 'word/document.xml'))
body = tree.getroot().find(W + 'body')

# ── 1. Sustituir los ficheros de imagen y reajustar el tamano declarado ───────

for num, (origen, destino, apaisada) in FIGS.items():
    shutil.copy(DIAG / origen, UNP / 'word/media' / destino)

rid_de = {}
rels = etree.parse(str(UNP / 'word/_rels/document.xml.rels'))
for rel in rels.getroot():
    tgt = rel.get('Target')
    for num, (_, destino, _) in FIGS.items():
        if tgt == f'media/{destino}':
            rid_de[rel.get('Id')] = num

parrafo_de_figura = {}
for p in body.iter(W + 'p'):
    for blip in p.iter(A + 'blip'):
        rid = blip.get('{http://schemas.openxmlformats.org/officeDocument/2006/relationships}embed')
        if rid in rid_de:
            num = rid_de[rid]
            parrafo_de_figura[num] = p
            origen, _, apaisada = FIGS[num]
            pw, ph = png_size(DIAG / origen)
            cx = ANCHO_APAISADO if apaisada else ANCHO_VERTICAL
            cy = int(cx * ph / pw)
            for e in p.iter(WP + 'extent'):
                e.set('cx', str(cx)); e.set('cy', str(cy))
            for e in p.iter(A + 'ext'):
                e.set('cx', str(cx)); e.set('cy', str(cy))
            print(f'  Figura {num}: {cx / 914400:.2f}" x {cy / 914400:.2f}"'
                  f'  {"apaisada" if apaisada else "vertical"}')

assert len(parrafo_de_figura) == 6, f'se localizaron {len(parrafo_de_figura)} figuras, no 6'

# ── 2. Secciones apaisadas para las Figuras 6, 7 y 9 ──────────────────────────

body_sect = body.find(W + 'sectPr')
plantilla = etree.tostring(body_sect)


def nuevo_sect(landscape):
    s = etree.fromstring(plantilla)
    s.attrib.clear()
    for hijo in list(s):
        if etree.QName(hijo).localname in ('pgSz', 'pgMar', 'pgNumType', 'type'):
            s.remove(hijo)
    # El esquema exige que las referencias a encabezado y pie precedan a type/pgSz/pgMar.
    pos = 0
    for k, hijo in enumerate(s):
        if etree.QName(hijo).localname in ('headerReference', 'footerReference',
                                           'footnotePr', 'endnotePr'):
            pos = k + 1
    t = etree.Element(W + 'type'); t.set(W + 'val', 'nextPage')
    s.insert(pos, t); pos += 1
    pgsz = etree.Element(W + 'pgSz')
    if landscape:
        pgsz.set(W + 'w', '15840'); pgsz.set(W + 'h', '12240'); pgsz.set(W + 'orient', 'landscape')
    else:
        pgsz.set(W + 'w', '12240'); pgsz.set(W + 'h', '15840')
    s.insert(pos, pgsz); pos += 1
    pgmar = etree.Element(W + 'pgMar')
    vals = ([('top', '1080'), ('right', '1440'), ('bottom', '1080'), ('left', '1800')]
            if landscape else
            [('top', '1440'), ('right', '1440'), ('bottom', '1440'), ('left', '2160')])
    for k, v in vals + [('header', '708'), ('footer', '850'), ('gutter', '0')]:
        pgmar.set(W + k, v)
    s.insert(pos, pgmar)
    return s


def poner_sect(p, sect):
    ppr = p.find(W + 'pPr')
    if ppr is None:
        ppr = etree.Element(W + 'pPr')
        p.insert(0, ppr)
    for viejo in ppr.findall(W + 'sectPr'):
        ppr.remove(viejo)
    ppr.append(sect)


def aislar(num):
    """
    Encierra el bloque epigrafe + imagen + nota de una figura en su propia seccion
    apaisada. En OOXML las propiedades de seccion viven en el ULTIMO parrafo de la
    seccion que describen, de modo que el parrafo anterior al epigrafe cierra la
    seccion vertical y la nota cierra la apaisada.
    """
    img = parrafo_de_figura[num]
    hermanos = list(body)
    i = hermanos.index(img)
    epigrafe, nota, previo = hermanos[i - 1], hermanos[i + 1], hermanos[i - 2]

    est = epigrafe.find(W + 'pPr/' + W + 'pStyle')
    assert est is not None and est.get(W + 'val') == 'Caption', f'Figura {num}: falta el epigrafe'
    assert ''.join(nota.itertext()).startswith('Nota.'), f'Figura {num}: falta la nota'

    poner_sect(previo, nuevo_sect(landscape=False))
    poner_sect(nota, nuevo_sect(landscape=True))
    print(f'  Figura {num} aislada en pagina apaisada')


for num in (6, 7, 8, 9):
    aislar(num)

# El reinicio de la paginacion en arabigos pertenece a la PRIMERA seccion del cuerpo, no
# al sectPr final: si se quedara alli, la numeracion se reiniciaria despues de la ultima
# figura. La seccion 1 es la portada y los indices, en romanos; la 2 abre el cuerpo.
secciones = [p.find(W + 'pPr/' + W + 'sectPr') for p in body.iter(W + 'p')]
secciones = [s for s in secciones if s is not None]

for hijo in list(body_sect):
    if etree.QName(hijo).localname == 'pgNumType':
        body_sect.remove(hijo)

cuerpo_1 = secciones[1]
if cuerpo_1.find(W + 'pgNumType') is None:
    pn = etree.Element(W + 'pgNumType')
    pn.set(W + 'fmt', 'decimal')
    pn.set(W + 'start', '1')
    idx = 0
    for k, hijo in enumerate(cuerpo_1):
        if etree.QName(hijo).localname in ('headerReference', 'footerReference',
                                           'type', 'pgSz', 'pgMar'):
            idx = k + 1
    cuerpo_1.insert(idx, pn)
    print('  paginacion arabiga anclada a la primera seccion del cuerpo')

tree.write(str(UNP / 'word/document.xml'), xml_declaration=True, encoding='UTF-8', standalone=True)
print('LISTO')

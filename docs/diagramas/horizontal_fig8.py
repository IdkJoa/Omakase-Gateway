"""
Pone la Figura 8 (el diagrama entidad-relacion) en una pagina horizontal propia.

El ER tiene diez entidades y unas noventa filas: a los 6 pulgadas de la caja de texto en
vertical el cuerpo de letra queda por debajo de lo legible en papel. Aislarlo en una
seccion apaisada con margenes reducidos lo lleva a 8,2 pulgadas, un 37 % mas.

En OOXML las propiedades de seccion viven en el ULTIMO parrafo de la seccion que
describen, de modo que el corte se materializa asi:

    ...texto...
    <p>La Figura 8 representa...</p>   <- lleva el sectPr vertical: cierra esa seccion
    <p>epigrafe</p> <p>imagen</p>
    <p>nota</p>                        <- lleva el sectPr apaisado: cierra la suya
    ...resto...                        <- vuelve al sectPr de nivel body

El pgNumType con w:start="1" se traslada al primer sectPr y se retira del de nivel body:
si se quedara al final, la numeracion de paginas se reiniciaria despues de la figura.
"""
from pathlib import Path
from lxml import etree

NS = {'w': 'http://schemas.openxmlformats.org/wordprocessingml/2006/main',
      'r': 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'}
W = '{%s}' % NS['w']
R = '{%s}' % NS['r']
UNP = Path('unpacked')

ANCHO_IMG_EMU = 7498080          # 8,2"
PNG_W, PNG_H = 8188, 5594        # figura-08-modelo-de-datos.png

tree = etree.parse(str(UNP / 'word/document.xml'))
body = tree.getroot().find(W + 'body')

# ── Localizar el bloque de la Figura 8 ────────────────────────────────────────

intro = capt = img = nota = None
for i, p in enumerate(body):
    if etree.QName(p).localname != 'p':
        continue
    txt = ''.join(p.itertext())
    if txt.startswith('La Figura 8 representa el modelo de datos'):
        intro = p
        capt, img, nota = body[i + 1], body[i + 2], body[i + 3]
        break

assert intro is not None, 'no se encontro el parrafo introductorio de la Figura 8'
assert 'Modelo de datos relacional' in ''.join(capt.itertext()), 'el epigrafe no cuadra'
assert img.find('.//' + W + 'drawing') is not None, 'no hay imagen donde se esperaba'
assert ''.join(nota.itertext()).startswith('Nota.'), 'la nota no cuadra'
print('  bloque de la Figura 8 localizado')

# ── Tomar el sectPr de nivel body como plantilla ──────────────────────────────

body_sect = body.find(W + 'sectPr')
plantilla = etree.tostring(body_sect)


def nuevo_sect(landscape, con_numeracion):
    s = etree.fromstring(plantilla)
    s.attrib.clear()
    for hijo in list(s):
        if etree.QName(hijo).localname in ('pgSz', 'pgMar', 'pgNumType', 'type'):
            s.remove(hijo)

    # El esquema fija el orden dentro de sectPr: las referencias a encabezado y pie van
    # primero, y solo despues type, pgSz y pgMar. Insertar detras de la ultima referencia.
    pos = 0
    for k, hijo in enumerate(s):
        if etree.QName(hijo).localname in ('headerReference', 'footerReference',
                                           'footnotePr', 'endnotePr'):
            pos = k + 1

    tipo = etree.Element(W + 'type'); tipo.set(W + 'val', 'nextPage')
    s.insert(pos, tipo); pos += 1
    pgsz = etree.Element(W + 'pgSz')
    if landscape:
        pgsz.set(W + 'w', '15840'); pgsz.set(W + 'h', '12240')
        pgsz.set(W + 'orient', 'landscape')
    else:
        pgsz.set(W + 'w', '12240'); pgsz.set(W + 'h', '15840')
    s.insert(pos, pgsz); pos += 1

    pgmar = etree.Element(W + 'pgMar')
    if landscape:
        # margenes reducidos: es una pagina de figura, no de texto corrido
        vals = [('top', '1080'), ('right', '1440'), ('bottom', '1080'), ('left', '1800')]
    else:
        vals = [('top', '1440'), ('right', '1440'), ('bottom', '1440'), ('left', '2160')]
    for k, v in vals + [('header', '708'), ('footer', '850'), ('gutter', '0')]:
        pgmar.set(W + k, v)
    s.insert(pos, pgmar); pos += 1

    if con_numeracion:
        pn = etree.Element(W + 'pgNumType'); pn.set(W + 'start', '1')
        s.insert(pos, pn)
    return s


def poner_sect(p, sect):
    ppr = p.find(W + 'pPr')
    if ppr is None:
        ppr = etree.Element(W + 'pPr')
        p.insert(0, ppr)
    ppr.append(sect)          # sectPr va al final del pPr


poner_sect(intro, nuevo_sect(landscape=False, con_numeracion=True))
poner_sect(nota,  nuevo_sect(landscape=True,  con_numeracion=False))
print('  + seccion vertical cerrada antes de la figura')
print('  + seccion apaisada cerrada tras la nota')

# La numeracion arabica arranca ahora en la primera de las secciones nuevas.
for hijo in list(body_sect):
    if etree.QName(hijo).localname == 'pgNumType':
        body_sect.remove(hijo)
        print('  + pgNumType retirado del sectPr final (evita reiniciar la paginacion)')

# ── Reescalar la imagen al nuevo ancho ────────────────────────────────────────

cy = int(ANCHO_IMG_EMU * PNG_H / PNG_W)
for tag in ('{http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing}extent',
            '{http://schemas.openxmlformats.org/drawingml/2006/main}ext'):
    for e in img.iter(tag):
        e.set('cx', str(ANCHO_IMG_EMU))
        e.set('cy', str(cy))
print(f'  + imagen reescalada a {ANCHO_IMG_EMU / 914400:.2f}" x {cy / 914400:.2f}"')

tree.write(str(UNP / 'word/document.xml'), xml_declaration=True, encoding='UTF-8', standalone=True)
print('LISTO')

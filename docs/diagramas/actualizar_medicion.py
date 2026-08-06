"""
Incorpora al capitulo IV la corrida de carga completa del 6 de agosto y los datos que
faltaban:

  · Tabla 3 con el perfil completo de percentiles (p50 y p99 incluidos)
  · Figura 10 regenerada con esas cifras
  · Tabla 5: la degradacion ante el fallo de geolocalizacion pasa de estar respaldada
    solo por prueba automatizada a estarlo tambien por observacion en ejecucion
  · parrafo de cobertura de reglas contextuales, cuarta variable del apartado 1.6, que
    era la unica operacionalizada en el capitulo I sin resultado reportado
"""
import shutil, struct
from pathlib import Path
from lxml import etree

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
WP = '{http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing}'
A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'
XS = '{http://www.w3.org/XML/1998/namespace}space'
UNP = Path('unpacked')
DIAG = Path('C:/Omakase-Gateway/docs/diagramas')

tree = etree.parse(str(UNP / 'word/document.xml'))
body = tree.getroot().find(W + 'body')


def run(text=None, bold=False, italic=False, sz=None):
    r = etree.Element(W + 'r')
    if bold or italic or sz:
        rpr = etree.SubElement(r, W + 'rPr')
        if bold:   etree.SubElement(rpr, W + 'b')
        if italic: etree.SubElement(rpr, W + 'i')
        if sz:     etree.SubElement(rpr, W + 'sz').set(W + 'val', str(sz))
    if text is not None:
        t = etree.SubElement(r, W + 't'); t.text = text; t.set(XS, 'preserve')
    return r


def para(runs=(), tight=False):
    p = etree.Element(W + 'p')
    if tight:
        ppr = etree.SubElement(p, W + 'pPr')
        sp = etree.SubElement(ppr, W + 'spacing')
        sp.set(W + 'line', '240'); sp.set(W + 'lineRule', 'auto')
    for r in runs:
        p.append(r)
    return p


def texto_completo(p):
    return ''.join(p.itertext())


def poner_texto(p, nuevo):
    """Deja todo el texto del parrafo en su primer run y vacia los demas."""
    ts = list(p.iter(W + 't'))
    ts[0].text = nuevo
    ts[0].set(XS, 'preserve')
    for t in ts[1:]:
        t.text = ''


def poner_nota(p, nuevo):
    """Las notas al pie tienen «Nota. » en cursiva en el primer run y el cuerpo despues."""
    ts = list(p.iter(W + 't'))
    ts[1].text = nuevo
    ts[1].set(XS, 'preserve')
    for t in ts[2:]:
        t.text = ''


def celda_texto(tc, nuevo):
    ts = list(tc.iter(W + 't'))
    ts[0].text = nuevo
    ts[0].set(XS, 'preserve')
    for t in ts[1:]:
        t.text = ''


tablas = [e for e in body if etree.QName(e).localname == 'tbl']
assert len(tablas) == 7, f'se esperaban 7 tablas, hay {len(tablas)}'
tabla3, tabla5 = tablas[2], tablas[4]

# ── 1. Narrativa de latencia ──────────────────────────────────────────────────

for p in body.iter(W + 'p'):
    if texto_completo(p).startswith('El presupuesto de latencia se cumple con margen'):
        poner_texto(p,
            'El presupuesto de latencia se cumple con holgura. En la corrida final, sobre '
            '203.964 evaluaciones realizadas con cien usuarios virtuales concurrentes durante '
            'cinco minutos y sin un solo fallo de transporte, el percentil 95 del recorrido '
            'completo se situó en 20,82 ms frente a los 50 ms exigidos, y aun el percentil 99 '
            'quedó por debajo del presupuesto, en 44,88 ms. Alcanzar esas cifras, sin embargo, '
            'no fue inmediato. La primera corrida instrumentada arrojó un percentil 95 de '
            '103,56 ms e incumplió el requisito, lo que obligó a medir el coste de cada fase de '
            'la evaluación. El desglose mostró que el 83 % del presupuesto se consumía en '
            'operaciones de entrada y salida —dos consultas a la base de datos por petición para '
            'resolver las políticas del servicio, otra para la configuración del motor y una '
            'doble resolución del perfil de comportamiento dentro de la misma evaluación—, '
            'mientras que la inferencia del modelo de aprendizaje automático representaba apenas '
            'el 16,7 %. El hallazgo merece subrayarse porque contradice la intuición: el '
            'componente de aprendizaje automático, candidato natural a ser el cuello de botella, '
            'no lo era. Corregidas las lecturas repetidas mediante cachés de vigencia breve y una '
            'memorización del perfil por petición, la latencia se redujo en un orden de magnitud '
            'respecto de la medición inicial. La Tabla 3 recoge el perfil completo de percentiles '
            'y la Figura 10 lo contrasta con el presupuesto de diseño.')
        print('  + narrativa de latencia actualizada')
        break

# ── 2. Tabla 3 ────────────────────────────────────────────────────────────────

FILAS_T3 = [
    ('Percentil 50 (mediana)',            '—',        '9,56 ms'),
    ('Percentil 90',                      '—',        '14,56 ms'),
    ('Percentil 95',                      '≤ 50 ms',  '20,82 ms'),
    ('Percentil 99',                      '—',        '44,88 ms'),
    ('Media',                             '—',        '11,46 ms'),
    ('Mínimo observado',                  '—',        '6,27 ms'),
    ('Rendimiento sostenido',             '—',        '566,5 peticiones/s'),
    ('Peticiones fallidas',               '< 5 %',    '0,00 %'),
]
filas = tabla3.findall(W + 'tr')
cuerpo = filas[1:]
assert len(cuerpo) >= len(FILAS_T3), 'la Tabla 3 tiene menos filas de las necesarias'
for tr, vals in zip(cuerpo, FILAS_T3):
    for tc, v in zip(tr.findall(W + 'tc'), vals):
        celda_texto(tc, v)
for tr in cuerpo[len(FILAS_T3):]:
    tabla3.remove(tr)
print(f'  + Tabla 3 reescrita ({len(FILAS_T3)} filas)')

# ── 3. Notas de la Tabla 3 y de la Figura 10 ──────────────────────────────────

for p in body.iter(W + 'p'):
    t = texto_completo(p)
    if t.startswith('Nota. Medición realizada el 6 de agosto'):
        poner_nota(p,
            'Corrida del 6 de agosto de 2026: 203.964 peticiones con cien usuarios virtuales '
            'concurrentes durante cinco minutos, a 566,5 peticiones por segundo y sin fallos de '
            'transporte. La medición comprende el recorrido completo de la petición —cabeceras de '
            'seguridad, límite de tasa, autenticación, evaluación de riesgo y reenvío al servicio '
            'de destino—, por lo que acota superiormente el overhead de evaluación que exige el '
            'requisito. Para que la carga alcanzara el motor fue necesario elevar el límite de '
            'tasa, que se aplica antes de la evaluación, y emplear un servicio de destino local: '
            'con un destino remoto la espera de red enmascara el tiempo propio del sistema. '
            'Elaboración propia.')
        print('  + nota de la Tabla 3 actualizada')
    elif t.startswith('Nota. Las tres primeras barras'):
        poner_nota(p,
            'Todas las barras corresponden al recorrido completo de la petición medido con el '
            'generador de carga. La línea discontinua marca el presupuesto de 50 milisegundos '
            'establecido como requisito no funcional de rendimiento. Elaboración propia.')
        print('  + nota de la Figura 10 actualizada')
    elif t.startswith('Nota. El comportamiento requerido corresponde'):
        poner_nota(p,
            'El comportamiento requerido corresponde a la política de degradación segura del '
            'sistema. Las filas marcadas como cumplidas están respaldadas por pruebas '
            'automatizadas que se ejecutan en la integración continua. La degradación ante el '
            'fallo del servicio de geolocalización se observó además en ejecución: 52 '
            'evaluaciones registraron el proveedor como inoperativo, todas ellas con un puntaje '
            'de política de exactamente 15,00 —el incremento previsto en el diseño— y ninguna '
            'resultó en denegación, sino en 15 accesos permitidos y 37 desafíos. Las filas '
            'restantes quedan pendientes de verificación sobre el entorno desplegado. '
            'Elaboración propia.')
        print('  + nota de la Tabla 5 actualizada')

# ── 4. Tabla 5: fila de geolocalizacion ───────────────────────────────────────

for tr in tabla5.findall(W + 'tr'):
    celdas = tr.findall(W + 'tc')
    if 'geolocalización' in ''.join(celdas[0].itertext()):
        celda_texto(celdas[2], 'Cumple (observado en ejecución)')
        print('  + Tabla 5: fila de geolocalización respaldada por observación')
        break

# ── 5. Figura 10: imagen nueva ────────────────────────────────────────────────

png = DIAG / 'figura-10-latencia.png'
shutil.copy(png, UNP / 'word/media/image11.png')
pw, ph = struct.unpack('>II', png.read_bytes()[16:24])
cx = 5486400                      # 6" de ancho util en pagina vertical
cy = int(cx * ph / pw)
for p in body.iter(W + 'p'):
    for blip in p.iter(A + 'blip'):
        if blip.get('{http://schemas.openxmlformats.org/officeDocument/2006/relationships}embed') == 'rIdFig10':
            for e in p.iter(WP + 'extent'):
                e.set('cx', str(cx)); e.set('cy', str(cy))
            for e in p.iter(A + 'ext'):
                e.set('cx', str(cx)); e.set('cy', str(cy))
            print(f'  + Figura 10 sustituida ({cx / 914400:.2f}" x {cy / 914400:.2f}")')

# ── 6. Cobertura de reglas contextuales (variable 4 del apartado 1.6) ─────────

COBERTURA = (
    'Queda por documentar la cobertura de reglas contextuales, cuarta de las variables '
    'operacionalizadas en el apartado 1.6. Conviene precisar antes su naturaleza: el número '
    'de reglas deterministas que se evalúan en cada petición no es una magnitud aleatoria '
    'sino una propiedad de la configuración, porque el motor evalúa exactamente las políticas '
    'activas asociadas al servicio de destino a través de la tabla de unión service_policies. '
    'Es la decisión de diseño que permite aplicar conjuntos de reglas distintos a servicios '
    'distintos —una nómina con ventana horaria estricta frente a un servicio de reportes que '
    'solo necesita límite de tasa— y significa que la cobertura queda fijada al configurar el '
    'sistema y no al ejecutarlo. Sobre las 720.942 evaluaciones acumuladas en el registro de '
    'auditoría, 99 dispararon al menos una regla del motor, con la siguiente distribución: '
    'degradación del proveedor de geolocalización, 52; viaje imposible, 13; huella de '
    'dispositivo, 12; geofencing, 6; exigencia de autenticación, 6; y eventos del segundo '
    'factor, 8. Esa proporción es baja por construcción y no debe leerse como una tasa de '
    'detección: el 99,9 % del volumen procede de las pruebas de carga, ejecutadas '
    'deliberadamente contra un servicio de demostración sin políticas asociadas, porque su '
    'objetivo era medir el tiempo propio del motor y no provocar violaciones. La cobertura '
    'efectiva se aprecia en el banco de casos etiquetados, donde cada escenario asoció las '
    'reglas pertinentes y todas dispararon conforme a lo previsto, según recoge la Tabla 4.'
)

ancla = None
for p in body.iter(W + 'p'):
    if texto_completo(p).startswith('Estas cifras deben leerse con la cautela'):
        ancla = p
        break
assert ancla is not None, 'no se encontro el parrafo de cautela'
padre = ancla.getparent()
i = list(padre).index(ancla)
padre.insert(i + 1, para())
padre.insert(i + 2, para([run(COBERTURA)]))
print('  + párrafo de cobertura de reglas contextuales insertado')

tree.write(str(UNP / 'word/document.xml'), xml_declaration=True, encoding='UTF-8', standalone=True)
print('LISTO')

"""
Devuelve a la Tabla 3 la fila de integridad de la auditoria.

Es un resultado que conviene ensenar en un sistema cuya premisa es que CADA peticion se
evalua y se audita: el canal en memoria esta configurado para descartar bajo presion y
aun asi no perdio un solo evento a 566 por segundo.
"""
import copy
from pathlib import Path
from lxml import etree

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
XS = '{http://www.w3.org/XML/1998/namespace}space'
tree = etree.parse('unpacked/word/document.xml')
body = tree.getroot().find(W + 'body')

tabla3 = [e for e in body if etree.QName(e).localname == 'tbl'][2]
filas = tabla3.findall(W + 'tr')
nueva = copy.deepcopy(filas[-1])

for tc, valor in zip(nueva.findall(W + 'tc'),
                     ('Eventos de auditoría descartados', '0', '0 de 203.964')):
    ts = list(tc.iter(W + 't'))
    ts[0].text = valor
    ts[0].set(XS, 'preserve')
    for t in ts[1:]:
        t.text = ''

tabla3.append(nueva)
tree.write('unpacked/word/document.xml', xml_declaration=True, encoding='UTF-8', standalone=True)
print('  + fila de integridad de la auditoria anadida a la Tabla 3')

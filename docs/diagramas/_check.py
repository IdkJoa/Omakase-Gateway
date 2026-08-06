"""Rasteriza una figura al ancho fisico que tendra en la pagina y la deja para inspeccion.
Criterio: si el texto no se lee en esta imagen, tampoco se leera en el documento."""
import sys, os, fitz
from svglib.svglib import svg2rlg
from reportlab.graphics import renderPDF

import re
ANCHO = {'h': 787, 'v': 576}   # 8,2" apaisado / 6" vertical, a 96 ppp

modo = 'v' if sys.argv[1] == '-v' else 'h'
nombres = sys.argv[2:] if sys.argv[1] in ('-v', '-h') else sys.argv[1:]
ANCHO_PAGINA_PX = ANCHO[modo]

for name in nombres:
    svg = name if name.endswith('.svg') else name + '.svg'
    renderPDF.drawToFile(svg2rlg(svg), '_t.pdf')
    d = fitz.open('_t.pdf')
    pw = d[0].rect.width
    esc = ANCHO_PAGINA_PX / pw
    pix = d[0].get_pixmap(matrix=fitz.Matrix(esc, esc), alpha=False)
    out = '_vista-' + os.path.basename(svg).replace('.svg', '.png')
    pix.save(out); d.close(); os.remove('_t.pdf')
    print(f'{out}  {pix.width}x{pix.height}')

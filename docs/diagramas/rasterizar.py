"""Rasteriza las figuras a 300 ppp para insertarlas en el documento."""
import glob, os, fitz
from svglib.svglib import svg2rlg
from reportlab.graphics import renderPDF
for f in sorted(glob.glob('figura-*.svg')):
    renderPDF.drawToFile(svg2rlg(f), '_t.pdf')
    d = fitz.open('_t.pdf')
    pix = d[0].get_pixmap(matrix=fitz.Matrix(300 / 72, 300 / 72), alpha=False)
    png = f.replace('.svg', '.png'); pix.save(png)
    d.close(); os.remove('_t.pdf')
    print(f'{png}  {pix.width}x{pix.height}  {os.path.getsize(png)//1024} KB')

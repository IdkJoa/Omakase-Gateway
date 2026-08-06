# Figuras del Capítulo IV (HU-036 / T-076)

Las cuatro figuras que el apartado 4.2.4 de la tesis describe pero no tenía insertadas.
Se generan por script para que sean **regenerables y verificables**: si el esquema o el
flujo cambian, se edita el script y se vuelve a ejecutar, en vez de retocar una imagen.

| Figura | Archivo | Generador |
|---|---|---|
| Figura 6 — Arquitectura general | `figura-06-arquitectura.svg` | `generar_arquitectura.py` |
| Figura 7 — Flujo de evaluación de una petición | `figura-07-flujo-de-evaluacion.svg` | `generar_flujo.py` |
| Figura 8 — Modelo de datos (ER) | `figura-08-modelo-de-datos.svg` | `generar_er.py` |
| Figura 9 — Vista de despliegue | `figura-09-despliegue.svg` | `generar_despliegue.py` |
| Figura 10 — Latencia frente al presupuesto | `figura-10-latencia.svg` | `generar_resultados.py` |
| Figura 11 — Separación de poblaciones del Risk Score | `figura-11-separacion-poblaciones.svg` | `generar_resultados.py` |

## Regenerar

```bash
for g in er arquitectura flujo despliegue resultados; do python docs/diagramas/generar_$g.py; done
```

Sin dependencias externas: solo la librería estándar de Python. `svgkit.py` contiene las
primitivas compartidas (cajas, flechas, paleta).

## Rasterizar a PNG (lo que se inserta en el documento)

```bash
python -c "
from svglib.svglib import svg2rlg
from reportlab.graphics import renderPDF
import fitz, glob, os
for f in sorted(glob.glob('docs/diagramas/figura-*.svg')):
    renderPDF.drawToFile(svg2rlg(f), 'tmp.pdf')
    d = fitz.open('tmp.pdf')
    d[0].get_pixmap(matrix=fitz.Matrix(300/72, 300/72), alpha=False).save(f.replace('.svg','.png'))
    d.close(); os.remove('tmp.pdf')
"
```

Requiere `svglib`, `reportlab` y `pymupdf`. Las puntas de flecha se dibujan como triángulos
explícitos y no con `<marker>` porque svglib no implementa `marker-end`, y una flecha sin
punta convierte un diagrama de flujo en un diagrama de cajas.

## Insertar en el documento

`editar_tesis.py` deja constancia de cómo se insertaron en la tesis: copia las imágenes al
paquete, crea las relaciones, convierte los pies en epígrafes con campo `SEQ` —para que el
índice de ilustraciones de Word los recoja— y completa las tablas del apartado 4.2.5. No es
un script para reejecutar a ciegas: sus índices de párrafo corresponden a la versión del
documento de agosto de 2026.

## Fuente de verdad

El ER **no** se transcribe del SRS sino de las configuraciones de EF Core en
`Infrastructure/Configurations/`: tipos, nulabilidad, claves, índices y reglas `ON DELETE`.
Esa decisión es deliberada — el diagrama tiene que resistir que alguien abra pgAdmin
durante la defensa y compare.

Donde el SRS y la base de datos difieren en nomenclatura (los umbrales de
`risk_score_config`), el diagrama usa los nombres reales de la columna e incluye la nota
de equivalencia con el SRS §7.6.

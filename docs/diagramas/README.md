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

## Regenerar

```bash
python docs/diagramas/generar_er.py && python docs/diagramas/generar_arquitectura.py && python docs/diagramas/generar_flujo.py && python docs/diagramas/generar_despliegue.py
```

Sin dependencias externas: solo la librería estándar de Python. `svgkit.py` contiene las
primitivas compartidas (cajas, flechas, paleta).

## Insertar en Word

Word 2016 y posteriores insertan SVG de forma nativa y lo mantienen vectorial, así que la
figura no pixela al ampliar ni al imprimir: *Insertar → Imágenes → Este dispositivo*.

Si la plantilla de la guía exigiera mapa de bits, exportar a PNG a 300 dpi abriendo el SVG
en el navegador e imprimiendo a PDF, o con Inkscape:

```bash
inkscape figura-08-modelo-de-datos.svg --export-type=png --export-dpi=300
```

## Fuente de verdad

El ER **no** se transcribe del SRS sino de las configuraciones de EF Core en
`Infrastructure/Configurations/`: tipos, nulabilidad, claves, índices y reglas `ON DELETE`.
Esa decisión es deliberada — el diagrama tiene que resistir que alguien abra pgAdmin
durante la defensa y compare.

Donde el SRS y la base de datos difieren en nomenclatura (los umbrales de
`risk_score_config`), el diagrama usa los nombres reales de la columna e incluye la nota
de equivalencia con el SRS §7.6.

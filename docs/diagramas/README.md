# Figuras del Capítulo IV (HU-036 / T-076)

Las seis figuras que el Capítulo IV de la tesis describe, generadas por script para que sean
regenerables y verificables: si el esquema o el flujo cambian, se edita el generador y se
vuelve a ejecutar, en vez de redibujar una imagen a mano.

| Figura | Archivo | Generador |
|---|---|---|
| 6 · Arquitectura general | `figura-06-arquitectura.svg` | `generar_arquitectura.py` |
| 7 · Flujo de evaluación de una petición | `figura-07-flujo-de-evaluacion.svg` | `generar_flujo.py` |
| 8 · Modelo de datos (entidad-relación) | `figura-08-modelo-de-datos.svg` | `generar_er.py` |
| 9 · Vista de despliegue | `figura-09-despliegue.svg` | `generar_despliegue.py` |
| 10 · Latencia frente al presupuesto | `figura-10-latencia.svg` | `generar_resultados.py` |
| 11 · Separación de poblaciones del Risk Score | `figura-11-separacion-poblaciones.svg` | `generar_resultados.py` |

## Regenerar

```bash
for g in arquitectura flujo er despliegue resultados; do python docs/diagramas/generar_$g.py; done
```

Sin dependencias: solo la librería estándar de Python. `svgkit.py` contiene las primitivas
compartidas, que son las cajas, las flechas, la paleta y el presupuesto de tamaños de letra.

## Rasterizar a PNG

Es lo que se inserta en el documento, porque Word trata mejor un mapa de bits a 300 puntos por
pulgada que un SVG con tipografías del sistema.

```bash
python docs/diagramas/rasterizar.py
```

Requiere `svglib`, `reportlab` y `pymupdf`.

## Comprobar que se leen

El error más fácil de cometer aquí es dibujar una figura estupenda en pantalla que resulta
ilegible en la página. Lo que decide la legibilidad no son los puntos por pulgada sino la
razón entre el ancho del lienzo y el tamaño de la letra: una figura de 2.600 píxeles con letra
de 11 queda, al reducirse al ancho de la caja de texto, en cuerpos de menos de 5 píxeles.

```bash
python docs/diagramas/verificar_legibilidad.py figura-08-modelo-de-datos
```

Rasteriza al ancho físico que la figura tendrá en la página (8,2 pulgadas en apaisado, 6 en
vertical) y deja el resultado como `_vista-*.png`. Si el texto no se lee ahí, tampoco se leerá
impreso. El presupuesto de tamaños está documentado en la cabecera de `svgkit.py`.

## Alcance del diagrama entidad-relación

Muestra las diez entidades con su clave primaria, sus claves foráneas y únicas, los atributos
que las distinguen, las cardinalidades y las reglas `ON DELETE`. **No reproduce las noventa
columnas del esquema**: eso es un diccionario de datos, cabe en la sección 7 del SRS, y a la
escala de una página resultaría ilegible. Es además lo que el capítulo afirma que la figura
detalla.

Los nombres, tipos y reglas se transcriben de las configuraciones de EF Core en
`Infrastructure/Configurations/`, no del SRS, de modo que el diagrama resiste que alguien abra
pgAdmin durante la defensa y compare.

## Insertar en Word

Word 2016 y posteriores insertan SVG de forma nativa y lo mantienen vectorial. Si la plantilla
exige mapa de bits, se usan los PNG que produce `rasterizar.py`.

Las Figuras 6 a 9 van en página apaisada porque a 6 pulgadas de ancho no se leen; las 10 y 11
caben en vertical.

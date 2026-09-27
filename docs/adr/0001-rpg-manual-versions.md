# Modelo de manuales y versiones RPG

**Estado:** aceptada.

En bibliotecas `RPG`, el `Volume` existente representa un **Manual** dentro de una `Series`; cada `Chapter` de ese volumen representa una versión seleccionable (Pages, Spreads, EPUB, etc.) y conserva el progreso independiente de lectura. Los metadatos bibliográficos y la portada son compartidos a nivel de `Volume`. Se reutiliza la jerarquía de Kavita para conservar el progreso por versión sin nuevas entidades; no se usan varios `MangaFile` como alternativas, porque Kavita trata esos archivos como partes del mismo contenido.

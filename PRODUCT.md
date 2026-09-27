# Kavita-Rol — producto RPG

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

El usuario prioritario es el administrador que incorpora carpetas de juegos de rol, organiza sus materiales, revisa metadatos y también lee. La experiencia de otros roles no es la prioridad de la primera validación.

## Product Purpose

Convertir carpetas de material de rol en Juegos utilizables sin obligar a clasificar o vincular metadatos antes de leer. El administrador debe poder identificar Publicaciones, sus versiones y Recursos, encontrar datos externos fiables y corregir lo que falta desde una interfaz clara.

## Positioning

La biblioteca RPG parte de una carpeta por Juego y presenta tareas propias del rol —Publicaciones, versiones, Recursos y revisión bibliográfica— en lugar de exponer la jerarquía genérica de series, volúmenes y capítulos de Kavita.

## Operating Context

El administrador coloca una carpeta principal en la biblioteca y la escanea: el Juego aparece y puede leerse de inmediato. Clasifica manualmente desde una revisión visible con acciones en lote; las alternativas evidentes como Pages/Spreads se agrupan como versiones separadas de la misma Publicación y esa agrupación se puede corregir. Cada archivo de uso práctico constituye un Recurso distinto. Las bibliotecas de prueba se pueden ajustar para validar el flujo; esto no autoriza alterar archivos originales ni datos de producción.

## Capabilities and Constraints

- Manual básico, Manual, Aventura y Suplemento son tipos de Publicación; mapa, hoja de personaje, ayuda y cartas son Recursos. La clasificación automática del tipo queda aplazada. Una edición diferente es otra Publicación; traducciones del mismo contenido son versiones con idioma propio, progreso propio, identidad y portada compartidas.
- RPGGeek busca candidatos para Publicaciones después de clasificarlas, pero no vincula fichas ni aplica datos automáticamente. El administrador revisa candidatos y cambios por campo, individualmente o en lote, puede buscar con otro texto, introducir un ID, reintentar o editar a mano. Los errores y ausencias se distinguen y nunca bloquean la lectura. Una Publicación tiene una ficha RPGGeek principal.
- Una ficha externa rellena vacíos por defecto; sustituir datos existentes, incluida la portada, exige elección explícita. La portada del Juego procede del único Manual básico si existe; en ausencia de uno único se elige explícitamente, nunca de un Recurso por accidente.
- DriveThruRPG se desactiva por ahora en la biblioteca de prueba, sin eliminar datos existentes ni el proveedor del proyecto. Los aciertos de RPGGeek deben cachearse entre reinicios, las ausencias solo brevemente; fallos de red/autenticación no se cachean, y la reconsulta fuerza una llamada nueva. El token no se almacena en el caché.
- Renombrar o mover archivos/carpetas no implica reconciliación automática con los registros anteriores; el usuario acepta que se pierdan sus asociaciones en ese caso. Un reescaneo sin cambios no debería perderlas.

## Evidence on Hand

Material real de evaluación en `../Rol/Pirate Borg/` (básico, aventuras, versiones y ayudas), `../Rol/Heart - La Ciudad de Abajo/` (cinco PDF en español cuyos títulos pueden no coincidir con RPGGeek) y `../Rol/Asher's Ridge/` (Pages/Spreads, mapa, cartas y piezas complementarias). El diseño y la verificación no deben inventar autores, portadas, identificadores ni resultados para llenar huecos. La biblioteca se probará en escritorio y móvil.

## Brand Commitments

Mantener el marco general de navegación y los temas de Kavita. Las pantallas RPG pueden tener estructura e interacción propias sin imponer un lenguaje visual ajeno al resto de la aplicación.

## Product Principles

1. Lectura disponible desde el primer escaneo; las tareas pendientes ayudan, no bloquean.
2. Clasificación manual rápida en el contexto del Juego, con acciones en lote y sin abrir un modal por archivo.
3. Estados y procedencia honestos: distinguir candidato, ausencia, ambigüedad y fallo; nunca presentar una llamada exitosa como prueba de datos aplicados.
4. Una experiencia RPG coherente orientada a tareas, no una acumulación de opciones genéricas y acciones escondidas.
5. Validar el recorrido completo con material real antes de declarar terminado un corte.

## Open Decisions

El alcance preciso de las superficies compartidas con bibliotecas no RPG y lectores existentes está pendiente de validación. Verificar los términos de uso de RPGGeek antes de fijar la retención de caché persistente.

# Modelo de catálogo RPG: Publicaciones, versiones y Recursos

**Estado:** aceptada; sustituye el mapeo anterior «Volume = Manual».

## Contexto

La jerarquía genérica de Kavita (`Series → Volume → Chapter → MangaFile`) permite conservar catálogo y lectura, pero sus nombres no describen material de rol. Un Juego reúne Publicaciones bibliográficas y Recursos prácticos. Las Publicaciones pueden tener varias maquetaciones o traducciones; un Recurso representa un archivo práctico independiente. Clasificar o vincular metadatos nunca puede ser requisito para abrir un archivo.

## Modelo conceptual y mapeo interno

- **Juego:** `Series` dentro de una biblioteca `LibraryType.Rpg`. La UI RPG lo presenta como Juego y no muestra los términos Series, Volume o Chapter.
- **Elemento del Juego:** `Volume` es un registro de catálogo, no un Manual por definición. `RpgMaterialType` lo clasifica manualmente como Sin clasificar, Manual básico, Manual, Aventura, Suplemento, Otra publicación, Mapa, Hoja de PJ, Ayuda, Cartas/Fichas u Otro recurso. Cero/Sin clasificar es el valor de alta y migración; ningún escáner ni proveedor asigna un tipo por defecto.
- **Publicación:** un `Volume` cuyo tipo sea Manual básico, Manual, Aventura, Suplemento u Otra publicación. Su título bibliográfico, portada y metadatos compartidos pertenecen al `Volume`.
- **Versión de Publicación:** un `Chapter` de ese `Volume`, con un archivo físico propio y progreso independiente. Idioma y progreso pertenecen a cada versión. Seleccionarla lleva directamente al lector; no es una página intermedia ni avanza a otra versión. No se agregan páginas ni progreso entre versiones.
- **Recurso:** un `Volume` clasificado como Mapa, Hoja de PJ, Ayuda, Cartas/Fichas u Otro recurso. Cada archivo físico es su propio Recurso; Fronts y Backs permanecen separados. Su `Chapter`/`MangaFile` es solo el puente que exige el lector actual: no representa una lectura secuencial, no muestra progreso y queda excluido de «Continuar», del progreso agregado y de la navegación siguiente.
- **Sin clasificar:** permanece visible y abrible hasta que una persona lo clasifique. Un recurso no se asocia a una Publicación o aventura.

Los nombres internos son una adaptación para Kavita; la UI utiliza Juego, Publicación, Versión y Recurso.

## Agrupación de versiones

No se tratan varios `MangaFile` del mismo `Chapter` como alternativas: Kavita los procesa como partes del mismo contenido. Un archivo Pages y otro Spreads solo se presentan como versiones de una Publicación cuando sus nombres dejan una identidad base inequívoca y ambos marcadores están presentes. La agrupación es una propuesta visible, corregible y separable; nunca asigna un tipo. Un archivo sin pareja, cualquier conjunto complementario y cada Recurso permanecen independientes. Una edición distinta es otra Publicación; los renombrados/movimientos no autorizan reconciliar identidades previas automáticamente. Un reescaneo sin cambios conserva IDs, relaciones, bloqueos y progreso.

## Metadatos y proveedores

- La identidad bibliográfica, portada y campos compartidos pertenecen a la Publicación (`Volume`); el idioma de cada versión y su progreso permanecen en `Chapter`. El año externo se almacena como año, no como una fecha ficticia. Para la portada del Juego se usa el único Manual básico identificado; mientras no lo haya, la miniatura local del único PDF del Juego sirve de portada provisional sin clasificarlo. Una portada elegida manualmente prevalece; si aparecen más PDFs no se elige uno nuevo arbitrariamente.
- Una Publicación admite como máximo una ficha RPGGeek principal. Clasificarla puede iniciar una búsqueda de candidatos; buscar y previsualizar nunca enlaza ni aplica datos. La revisión en lote se inicia de forma explícita y deriva resultados ambiguos a la revisión individual. Solo una confirmación explícita enlaza y aplica cambios. Los vacíos pueden completarse por defecto; reemplazar título, otros valores existentes o portada exige una elección por campo. No se infiere idioma desde RPGGeek.
- La identidad bibliográfica puede corregirse manualmente antes de buscar. Editar un campo lo protege por defecto frente a la importación externa y el escáner; un título local no protegido puede actualizarse durante un escaneo. RPGGeek ID es informativo/solo lectura en External IDs: la asociación se hace mediante el flujo de candidatura y confirmación.
- Las respuestas exitosas de búsqueda y producto usan la caché persistente distribuida ya configurada por Kavita: 7 días para aciertos y 15 minutos para resultados vacíos/no encontrados. Los errores de transporte no se cachean; una reconsulta explícita omite la caché. El intervalo de petición es configurable, con valor predeterminado de 5 segundos, y se respeta `Retry-After`. La revisión de los términos de Geekdo sigue pendiente porque la página devolvió 403; revisar las condiciones y límites oficiales antes de dar la política por validada.
- DriveThruRPG continúa como proveedor independiente; su activación es por biblioteca, desactivada por defecto, y no altera Kavita+. Los Recursos y Sin clasificar no generan consultas de ningún proveedor. Solo una coincidencia normalizada, exacta y única puede asociarse automáticamente; coincidencias ambiguas o aproximadas quedan sin vincular. Una selección manual de candidato o ID vincula explícitamente y cualquier importación respeta los bloqueos. Esta excepción a la confirmación obligatoria de RPGGeek se conserva del proveedor y se limita a bibliotecas que opten por activarlo. Las condiciones de uso y límites oficiales de DriveThruRPG siguen pendientes de verificar; véase [la guía de integración](../integrations/drivethrurpg.md).
- No exponer ni guardar el token RPGGeek en logs, Git, artefactos de UI o imágenes. La caché y la separación entre fallos/aciertos se describen en la decisión anterior; la conformidad con términos y límites oficiales sigue sin verificarse.

## Migraciones y conservación

Las migraciones parten del modelo oficial limpio `v0.9.1.4`. Añaden únicamente columnas/tablas nuevas con valores compatibles y `RpgMaterialType = 0`; no reutilizan a ciegas migraciones del prototipo ni convierten Volume/Chapter existentes. No reasignan IDs, portadas, IDs externos, locks, progreso, archivos ni asociaciones; no eligen una versión para resolver metadatos en conflicto. La reversión solo elimina la estructura aditiva nueva. Toda migración se verifica contra una copia de base de datos con backup comprobado antes de su uso; esta fase no aplica migraciones a contenedores o bibliotecas existentes.

## Límites

No se escriben metadatos dentro de PDF/EPUB. EPUB, NAS, despliegue de producción y cambios a Book, Comic, Manga o Kavita+ quedan fuera de alcance. El marco de navegación y el tema general de Kavita se conservan; solo las superficies RPG tienen estructura propia.

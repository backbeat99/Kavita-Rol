# Integración DriveThruRPG

**Estado:** implementada y disponible mediante activación explícita por biblioteca; no habilitada por defecto. La verificación de las condiciones de uso y los límites oficiales de DriveThruRPG sigue pendiente. No activar fuera de pruebas hasta completarla.

## Alcance y activación

- El interruptor **Enable DriveThruRPG metadata** de una biblioteca RPG está desactivado por defecto. Activarlo inicia la búsqueda de publicaciones clasificadas que aún no tengan ID externo. Desactivarlo impide nuevas consultas, incluidas las tareas en cola que todavía no hayan empezado.
- Solo se procesan Publicaciones (`CoreManual`, `Manual`, `Adventure`, `Supplement`, `OtherPublication`). Los Recursos y elementos Sin clasificar nunca se envían al proveedor.
- El cliente usa la API pública vBeta de DriveThruRPG (`api.dmsguild.com`, `siteId=29`); no requiere ni almacena una clave. Esto no implica que sus términos, privacidad o límites de uso estén validados.
- La implementación RPG asocia el producto con el `Volume`/Publicación. Los campos heredados de DriveThruRPG en `Chapter` no forman parte de este flujo.
- No se añadió una migración específica para DriveThruRPG: esta implementación reutiliza los campos y la opción de biblioteca existentes.

## Coincidencia y revisión

- La búsqueda automática normaliza acentos, mayúsculas y puntuación, y solo vincula una coincidencia exacta única. Excluye productos de Fantasy Grounds. Puede probar el título completo y retirar sufijos genéricos como «PDF», «Core Rules» o «RPG»; si encuentra más de una coincidencia exacta, ninguna se vincula automáticamente. Las coincidencias aproximadas nunca se enlazan.
- Al buscar desde la ficha, no se vincula nada. Una persona administradora debe elegir un candidato o introducir manualmente un ID positivo de producto; ese acto enlaza explícitamente y pone en cola la importación. También puede abrir los enlaces del proveedor para revisar los resultados antes de elegir.
- Las búsquedas automáticas no se cachean. El trabajo de fondo espera 350 ms entre publicaciones. No se ha validado aún si ese intervalo satisface los límites oficiales del proveedor.

## Metadatos importados

Solo se rellenan valores no vacíos y no bloqueados: título, descripción, año de publicación, autores, editoriales, idioma de cada versión cuyo idioma no esté bloqueado y portada. Los bloqueos de título, descripción, año, autores, editoriales, idioma y portada se respetan. La búsqueda y el vínculo no dependen de RPGGeek ni de Kavita+.

## API y pruebas

Las acciones de usuario están restringidas a administradores y vuelven a comprobar que el elemento sea una Publicación RPG y que el proveedor siga habilitado:

- `GET api/volume/rpg/drivethrurpg/candidates?volumeId={id}&query={texto}`
- `POST api/volume/rpg/drivethrurpg/link?volumeId={id}&productId={id}`
- `POST api/volume/rpg/drivethrurpg/refresh?volumeId={id}`

Pruebas de cliente y servicio:

```sh
dotnet test Kavita.Services.Tests/Kavita.Services.Tests.csproj --filter FullyQualifiedName~DriveThruRpg
```

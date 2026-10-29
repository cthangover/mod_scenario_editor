# Editor de escenarios — escribe, revisa y juega escenarios dentro del juego

El editor de escenarios edita los archivos `.scenario` de todos los mods instalados mientras el juego está en marcha. Muestra el archivo con los comandos resaltados, un resumen de escenas y transiciones y un grafo de adónde puede ir la historia. **▶ Jugar** guarda el archivo y lo juega dentro del juego en curso; **■ Detener** te devuelve a donde estabas.

## Cómo abrirlo

- Desde la caja de herramientas: botón del martillo en la barra del juego → **Editor de escenarios**.
- Desde el editor de escenas: selecciona un escenario de una escena y pulsa **Abrir**, o **+ Nuevo escenario** (el archivo nuevo se abre aquí).

## Trabajar con un escenario

![El editor de escenarios con my_novel_town.scenario de la plantilla de novela abierto: a la izquierda el árbol Archivos de my_novel_game, en el centro el texto resaltado con la elección «Where to go?» y sus dos respuestas; en la barra Guardar, Jugar, Abrir en un editor externo y Volver a escanear.](assets/scenario-editor.jpg)

1. Elige el mod y el archivo en el árbol **Archivos** de la izquierda (**Resumen** enumera escenas y transiciones). Se pueden abrir varios archivos en pestañas.
2. Edita el texto. Los comandos se resaltan; Ctrl+Z deshace, Ctrl+S guarda en el mod al que pertenece el archivo.
3. La pestaña **Grafo de escenas** muestra las escenas y las transiciones entre ellas; al pulsar una escena aparecen sus escenarios.
4. **▶ Jugar** (F5) guarda y juega el escenario en el juego en curso; mientras tanto el editor se oculta. **■ Detener** vuelve a la escena en la que estaba el juego y muestra el editor de nuevo. El archivo debe indicar su escena en la cabecera.
5. **Abrir en un editor externo** abre el archivo con el programa que tu sistema usa para él. **Volver a escanear** relee las listas tras añadir archivos.

Guardar vacía las cachés de escenarios del juego, así que la próxima vez que se entre en la escena sonará el texto nuevo. Con la paleta de comandos de `schema_tools` instalada, el comando elegido se inserta bajo el cursor del último escenario enfocado como un solo paso de deshacer.

## Límites

- El editor no impide guardar un escenario con errores; revisar referencias rotas es tarea del Validador.
- Solo se pueden guardar mods instalados como carpetas. El archivo de un mod empaquetado no se escribe, pero **▶ Jugar** sigue jugando el texto del editor.

## Dependencias

`tools_base`, `scenario_tools_base`, `dialog_base`, `ui_base`.
El paquete antiguo `editor_tools` instala este mod junto con las demás ventanas de autor.

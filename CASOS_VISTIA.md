# Vistia 3 — lista completa de casos

Estado: **en revisión**. No se marca como probado por el agente: Cami hace la prueba funcional.

## A. Teclado y activación

| ID | Caso | Resultado esperado |
|---|---|---|
| A01 | Espacio en lista de archivos | Abre/cierra la vista del archivo seleccionado |
| A02 | Doble espacio en lista | Alterna correctamente sin abrir dos vistas |
| A03 | Renombrar archivo | Escribe espacios normales; no activa Vistia |
| A04 | Buscar en Explorador | Conserva el espacio en la búsqueda |
| A05 | Barra de dirección/nombre de archivo | Conserva el espacio |
| A06 | Ctrl+Espacio fuera de edición | Abre desde otra app o recupera la última vista |
| A07 | Ctrl+Espacio editando | No activa ni cierra |
| A08 | Ctrl/Shift/Alt/Win | Ninguna combinación accidental activa Vistia |
| A09 | Composición IME | No interrumpe la escritura |
| A10 | Evento inyectado | Se ignora como activación |
| A11 | Mantener espacio | No abre/cierra repetidamente |
| A12 | Soltar y volver a pulsar | Nueva transición correcta |
| A13 | Buscar dentro del código | El campo acepta espacios |
| A14 | Input/textarea/select/contenteditable HTML | Conserva teclas y espacio |
| A15 | PDF y diálogos internos | No roba sus controles; se puede cerrar desde fuera |
| A16 | Botón enfocado | Espacio activa el botón, no cierra la vista |
| A17 | Espacio en video | Reproduce/pausa, no cierra |
| A18 | Espacio en imagen/documento no multimedia | Cierra si no está fijada |
| A19 | Vista fijada | No cierra por espacio, clic fuera o cambio de app |
| A20 | Escape normal | Cierra si no está fijada |
| A21 | Escape en pantalla completa | Sale primero de fullscreen |
| A22 | Escape en Info | Cierra solo Info |
| A23 | Espacio/Escape en Inicio | No afecta a otra vista |
| A24 | Pausa | Detiene la activación global y persiste al reiniciar |
| A25 | Inicio con Windows | Bandeja activa sin mostrar ventana |
| A26 | Segunda instancia | No duplica ventanas ni procesos residentes |
| A27 | Espacio+V fuera de edición | Prioriza la selección actual; si no es válida, recupera la última vista o Inicio |
| A28 | Espacio+V dentro de texto/IME | No activa ni consume la combinación |
| A29 | Espacio+V tras un Espacio ya aceptado | No genera una segunda apertura/cierre ni deja la vista como empezó |
| A30 | Soltar V y Espacio en distinto orden | Ninguna tecla queda atascada ni se escribe una V suelta |
| A31 | Mantener V durante Espacio+V | La autorrepetición no escribe V ni repite la activación |

## B. Aperturas y navegación

| ID | Caso | Resultado esperado |
|---|---|---|
| B01 | Imagen → video | Muestra el video actual |
| B02 | Video → imagen | Muestra la imagen actual y detiene el audio |
| B03 | Cambios rápidos | El último archivo siempre gana; ninguno anterior se repinta |
| B04 | Formatos diferentes seguidos | Cada visor se limpia sin destruir el nuevo |
| B05 | Cerrar y reabrir el mismo archivo | Abre el archivo correcto, no queda vacío |
| B06 | Cerrar app y abrir otro archivo | Selección nueva, no archivo anterior |
| B07 | Ctrl+Espacio después de cerrar | Recupera la última ruta o el selector |
| B08 | Probar con un archivo | Abre exactamente el archivo elegido |
| B09 | Archivo borrado/movido | Error honesto, sin contenido anterior |
| B10 | Inicio/final de carpeta | Navegación circular coherente |
| B11 | Entrada interna de ZIP → otra entrada | Abre la entrada elegida y puede volver |
| B12 | Cambio de tema | Mantiene archivo y estado actuales |
| B13 | MP4/MOV A → MP4/MOV B | El segundo genera una carga con identidad propia y muestra B, nunca reutiliza A |
| B14 | Directo → convertido → directo | Cada fuente corresponde a su archivo y no mezcla errores/fotogramas anteriores |
| B15 | Cancelar preparación en cola y reabrir | La cola cancelada termina; un intento nuevo puede completarse sin quedar congelado |
| B16 | Arrastrar progreso y seguir reproduciendo | La barra vuelve a avanzar aunque conserve el foco |
| B17 | Error o preparación | Play no oculta el error ni intenta reproducir una fuente vacía |
| B18 | PDF y documentos con poco contenido | El visor ocupa toda el área disponible, sin banda vacía ni deformar páginas |
| B19 | Actualizar estilos propios | CSS se revalida y no conserva un diseño anterior por caché anual |

## C. Reproductor

| ID | Caso | Resultado esperado |
|---|---|---|
| C01 | Play/pausa | Responde a botón y espacio |
| C02 | Arrastrar progreso | Visual durante arrastre; seek al soltar |
| C03 | Búfer | Se muestra separado del progreso reproducido |
| C04 | Miniaturas | Se crean bajo demanda sin decodificar continuamente |
| C05 | Volumen/mute | Funciona y se recuerda |
| C06 | Velocidad | Funciona y se recuerda |
| C07 | Bucle | Repite solo cuando está activo |
| C08 | Ajuste/rellenar/tamaño real | No deforma ni estira |
| C09 | Pantalla completa | Entra/sale y restaura tamaño y posición |
| C10 | PiP | Solo si existe; no cierra al cambiar de foco |
| C11 | Audio | Comienza pausado y sin controles de video irrelevantes |
| C12 | Cambiar/cerrar archivo | Detiene audio y libera el elemento |
| C13 | Cambiar durante conversión | Cancela la anterior y prioriza el archivo nuevo |
| C14 | Caché expulsada | Se regenera en vez de quedar marcada como lista |
| C15 | Remux inválido | Fuerza transcodificación |
| C16 | MP4/MOV compatible | Reproducción directa |
| C17 | MKV H.264 | Remux o reproducción compatible |
| C18 | ProRes/WebM alfa | Transparencia visible sobre tablero |
| C19 | Error | Reintentar, forzar compatible y abrir original |
| C20 | Reiniciar app | No queda reproducción fantasma ni conversión bloqueada |

## D. Minecraft

| ID | Caso | Resultado esperado |
|---|---|---|
| D01 | MCPACK | Manifiesto, icono, módulos, dependencias y texturas |
| D02 | MCADDON | Packs incluidos/anidados con límite seguro |
| D03 | MCWORLD | Nombre, icono, packs y metadatos NBT disponibles |
| D04 | MCTEMPLATE | Lectura como mundo sin render del terreno |
| D05 | JAR | Mod solo por manifiestos reales y loader correcto |
| D06 | Abrir textura | Se abre desde la tabla ZIP sin extraer |
| D07 | HTML/JS de un pack | Se lee como texto, nunca se ejecuta |
| D08 | ZIP bomba/entrada enorme | Límite honesto sin disparar RAM |
| D09 | Skin moderna 64×64 | UV y segunda capa |
| D10 | Skin legacy 64×32 | UV y extremidades compatibles |
| D11 | Skin HD escalada | Funciona como escalado entero |
| D12 | Classic/Slim | Selector manual funcional |
| D13 | Segunda capa | Se puede mostrar/ocultar |
| D14 | Caminar/girar | Animación y órbita responden |
| D15 | Plano/3D | Cambia sin perder la imagen original |
| D16 | PNG cuadrado cualquiera | No se reclasifica automáticamente como skin |
| D17 | Skin dentro de pack | Botón Minecraft abre el visor correcto |
| D18 | Mundo | No promete render del terreno ni LevelDB |

## E. Formatos, recursos y seguridad

| ID | Caso | Resultado esperado |
|---|---|---|
| E01 | DOCX/XLSX/PPTX | Abre texto/formato sin regresión |
| E02 | PDF | Abre visor y conserva controles |
| E03 | HTML | Renderiza; Código y Dividir funcionan |
| E04 | Código | Números, búsqueda, copiar y ajuste de líneas |
| E05 | HEIC/JXL/RAW/PSD/AI/EPS | Usa motor compatible sin alterar original |
| E06 | ZIP genérico | Lista, abre internos y vuelve |
| E07 | Entrada >64 MB | Error honesto sin copiar todo en memoria |
| E08 | Range con sufijo | Responde 206 correcto |
| E09 | Range inválido | 416 con `Content-Range: bytes */total` |
| E10 | URL remota en manifiesto | No se carga contenido externo |
| E11 | HTML local | Aislado, sin internet |
| E12 | Mensajes del host | `cmd` exacto y esquema validado |
| E13 | Nombre `../` en ZIP | Solo texto, nunca ruta física |
| E14 | Archivo corrupto | Tarjeta de error, sin crash ni contenido anterior |

## F. Ventana, bandeja y estado

| ID | Caso | Resultado esperado |
|---|---|---|
| F01 | Mover/redimensionar | Funciona fijada o no |
| F02 | Texto de inicio | Todo visible; ajuste y scroll |
| F03 | Cerrar/reabrir Inicio | Se recrea y muestra estado actual |
| F04 | Arranque con Windows | Bandeja activa sin ventana de inicio |
| F05 | Pausa | Persistente y coherente con menú/inicio |
| F06 | Iniciar con Windows activado | Crea acceso correcto a Vistia.exe |
| F07 | No mostrar inicio al prender PC | No aparece ninguna ventana automática |
| F08 | Cerrar vista | App sigue en bandeja y espacio vuelve a abrir |
| F09 | Abrir/cerrar Info | No cierra Preview |
| F10 | Metadatos largos | Ajuste sin texto cortado |
| F11 | Clic fuera sin fijar | Cierra Preview solo si ya fue mostrada |
| F12 | Clic fuera fijada | No cierra |
| F13 | Desfijar y X | Cierra correctamente |
| F14 | Bandeja en segundo plano | Espacio sigue funcionando al prender Windows |
| F15 | Segunda ejecución | Despierta la instancia existente sin duplicados |
| F16 | Cambiar tema | No reinicia archivo ni audio |
| F17 | Versión | Muestra Vistia 1.0.1 correctamente |
| F18 | Reiniciar desde bandeja/Inicio | Espera al proceso anterior, conserva configuración y no duplica instancias |
| F19 | Salir | Libera gancho por `user32.PostThreadMessage`, mutex e icono sin excepción |
| F20 | Pausar desde Inicio y abrir bandeja | Ambos muestran el mismo estado y Reanudar funciona a la primera |
| F21 | Pausar con una tecla presionada | Al reanudar no queda Space/V bloqueada ni se traga la siguiente pulsación |
| F22 | Explorador lento | La lectura COM no congela la UI ni una respuesta antigua abre después |
| F23 | WebView2 falla al primer intento | Una pulsación posterior puede reintentar; no queda fallo permanente |
| F24 | Fijar/desfijar en fullscreen | Al salir se conserva el estado actual de fijado |
| F25 | Menú de bandeja | Abrir, última, fijar, pausa, tema, autoinicio, registro, reinicio y salida funcionan |
| F26 | Abrir desde bandeja/otra app | La vista aparece al frente y con foco sin exigir otra acción manual |
| F27 | Cierre automático tras F26 | Al salir de una vista no fijada se cierra igual que antes |
| F28 | Menú Vistia | Fondo tinta, texto legible, selección terracota y esquinas redondeadas |

## G. Identidad Vistia

| ID | Caso | Resultado esperado |
|---|---|---|
| G01 | Ejecutable y metadatos | `Vistia.exe`, Producto Vistia, Empresa Cami-OS, versión 1.0.1.0 |
| G02 | Icono en ventanas/bandeja | V terracota legible, sin icono anterior |
| G03 | Icono en barra de tareas/Administrador | Usa el ICO embebido del EXE |
| G04 | Acceso de inicio | `Vistia.lnk` apunta al nuevo EXE y muestra su icono |
| G05 | Datos migrados | Configuración y caché se conservan en carpetas Vistia |
| G06 | Host/mutex | `vistia.local` y `Vistia.InstanciaUnica`; ninguna referencia antigua en código |
| G07 | Compilación | 0 errores y 0 advertencias; JS propio con sintaxis válida |

## Hallazgos y correcciones

- Inicio con Windows: se retiró `MostrarInicio()` del arranque automático. Bandeja y precalentamiento siguen activos.
- Doble espacio: se añadió detección de segunda pulsación física en 500 ms, también sobre botones no editables; texto e IME siguen protegidos.
- `Document` ya no se considera editable por defecto sin evidencia de escritura: las listas del Explorador pueden activar Vistia.
- Al cerrar con teclado, se devuelve el foco al Explorador de origen para que la siguiente apertura funcione.
- Se conservan las correcciones de estado/render obsoleto, audio, pausa persistente, Range, packs y seguridad.

Cami debe validar los casos de la lista. No se afirma verificación funcional por parte del agente.

# Vistia

**Vistia · Powered by Cami-OS** es un visor universal local para Windows. Abre imágenes, video, audio, PDF, Office, HTML, código, Minecraft, comprimidos y modelos 3D con una pulsación, sin modificar los archivos originales.

## Descargar

La release actual es **Vistia 1.0.1**:

- Ejecutable: `Vistia.exe` para Windows x64.
- Requiere **.NET 10 Desktop Runtime x64** y **WebView2 Evergreen**.
- FFmpeg/ffprobe en `PATH` habilita remux y transcodificación. Los formatos directos siguen funcionando sin él.
- Magick.NET, WebView2Loader y Ghostscript portátil se incluyen con sus licencias correspondientes.

[Descargar Vistia 1.0.1](https://github.com/cami4844/vistia-cami-os/releases/tag/v1.0.1)

## Uso rápido

1. Abre `Vistia.exe`; queda en la bandeja sin mostrar una ventana automática.
2. Selecciona un archivo y pulsa **Espacio**, **doble espacio** o **Espacio+V**.
3. **Ctrl+Espacio** recupera la última vista fuera de edición.
4. **Ctrl+F** fija/desfija; **Ctrl+I** muestra información; **Enter** abre con su aplicación.
5. Una vista sin fijar se cierra al cambiar de aplicación, con Espacio, Escape o X.

La nueva vista se presenta al frente aunque Vistia esté en segundo plano. Renombrar, buscar, formularios, editores e IME conservan la escritura.

## Bandeja

Menú Vistia redondeado con fondo tinta y selección terracota:

- Inicio y Abrir archivo.
- Mostrar última vista.
- Fijar/Desfijar vista.
- Pausar/Reanudar activación.
- Tema claro/oscuro.
- Iniciar con Windows.
- Registro de diagnóstico.
- Reiniciar Vistia y Salir.

Reiniciar espera a que termine el proceso anterior y no duplica instancias.

## Reproductor

Controles propios de reproducción, progreso/búfer, miniaturas bajo demanda, volumen, velocidad, bucle, ajuste, pantalla completa y PiP condicional. La caché multimedia tiene un máximo de **5 GB** y no altera los originales.

Cada archivo usa identidad propia y las respuestas directas evitan caché. Las conversiones canceladas no se reutilizan. La barra continúa avanzando después de arrastrarla.

## Formatos

- **Imagen:** PNG, JPG, GIF, WebP, AVIF, HEIC/HEIF, JXL, PSD/PSB, AI, EPS, DNG/RAW compatibles, JP2, QOI, TGA, DDS, EXR, HDR, TIFF, SVG, BMP e ICO.
- **Video/audio:** MP4, MOV, WebM, MKV, AVI, WMV, FLV, MPEG/TS, 3GP, MP3, WAV, FLAC, OGG, M4A, AAC, OPUS, WMA y AIFF, entre otros reconocidos por FFmpeg.
- **Documentos:** PDF, DOCX/DOCM/DOTX, XLSX/XLSM/XLSB/CSV/TSV/ODS y PPTX/PPTM/PPSX/POTX.
- **Web/código:** HTML renderizado, Código, Dividir, Markdown y más de 60 lenguajes.
- **Minecraft:** MCPACK, MCADDON, MCWORLD, MCTEMPLATE, JAR y skins 2D/3D. No se extraen ni ejecutan contenidos; no se promete render del terreno.
- **3D:** STL, OBJ, PLY, GLB, GLTF, FBX, 3DS, DAE y 3MF.
- **Comprimidos:** ZIP, CBZ, EPUB, APK, JAR, WHL, VSIX y XPI con lectura interna limitada.
- **Fuentes:** TTF, OTF, WOFF, WOFF2 y TTC.

## Privacidad

El visor usa el host virtual local `https://vistia.local`; no requiere internet ni cuentas. La configuración, caché y registro se guardan en las carpetas Vistia del perfil del usuario. Los paquetes de publicación no incluyen configuración, caché, registros ni archivos personales.

## Compilar

Requisito: SDK de .NET 10 con cargas WPF/WinForms para Windows.

```powershell
dotnet restore Vistia.csproj
dotnet publish Vistia.csproj -c Release -o publish
```

La salida queda en `publish/Vistia.exe` con la carpeta `Assets`.

## Verificación de esta entrega

- Compilación Release: **0 errores y 0 advertencias**.
- Módulos propios JavaScript con sintaxis validada por `node --check`.
- EXE y recursos instalados comparados con la publicación.
- SHA-256 del ejecutable: `80F8C6329C208E3701A9E1269E330B1CF9B499A213A5016D4C2B0A52ECC1C029`.

Cami realiza la validación funcional. Las pruebas de aceptación están en `CASOS_VISTIA.md`.

Powered by **Cami-OS · cami4844**.

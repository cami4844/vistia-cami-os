function rutaZip(indice, extension) {
    if (!Number.isSafeInteger(indice) || indice < 0 || !/^(png|jpe?g)$/i.test(extension || '')) throw new Error('Recurso interno no permitido');
    const base = new URL(String(indice) + '/', 'https://vistia.local/zipfile/');
    return new URL(encodeURIComponent(extension.toLowerCase()), base).href;
}

function entradaValida(imagen) {
    return Number.isSafeInteger(imagen.index) && imagen.index >= 0 && /^(png|jpe?g)$/i.test(imagen.ext || '');
}

export function crearMinecraftPanel(estado, listaZip) {
    const mc = estado.mc || {};
    const panel = document.createElement('section');
    panel.className = 'mc-panel';
    panel.innerHTML = `
        <div class="mc-cabecera">
            <div class="mc-icono" hidden><img alt="Icono del paquete"></div>
            <div class="mc-identidad"><h2></h2><p class="mc-descripcion"></p><div class="mc-datos-cortos"></div></div>
        </div>
        <div class="mc-tabs" role="tablist"><button type="button" data-tab="resumen" class="activa">Resumen</button><button type="button" data-tab="texturas">Texturas</button><button type="button" data-tab="archivos">Archivos</button><button type="button" data-tab="datos">Datos</button></div>
        <div class="mc-resumen"></div>
        <div class="mc-galeria" hidden></div>
        <pre class="mc-json" hidden></pre>`;
    const nombre = mc.name || mc.worldName || estado.nombre || 'Paquete Minecraft';
    panel.querySelector('h2').textContent = nombre;
    panel.querySelector('.mc-descripcion').textContent = mc.description || descripcionTipo(mc.kind);
    const cortos = [];
    if (mc.version) cortos.push('Versión ' + mc.version);
    if (mc.minEngineVersion) cortos.push('Motor ' + mc.minEngineVersion);
    if (mc.loader) cortos.push(mc.loader);
    if (mc.modId) cortos.push(mc.modId);
    panel.querySelector('.mc-datos-cortos').textContent = cortos.join(' · ');
    if (mc.iconIndex != null) {
        const caja = panel.querySelector('.mc-icono');
        caja.hidden = false;
        caja.querySelector('img').src = rutaZip(mc.iconIndex, mc.iconExt);
    }
    const resumen = panel.querySelector('.mc-resumen');
    agregarLista(resumen, 'Módulos', mc.modules);
    agregarLista(resumen, 'Dependencias', mc.dependencies);
    agregarLista(resumen, 'Packs incluidos', (mc.packs || []).map(p => p.name || p.path));
    agregarLista(resumen, 'Skins declaradas', (mc.skins || []).map(s => s.name || s.texture));
    agregarLista(resumen, 'Mundo', [mc.worldName, mc.LevelName, tipoJuego(mc.GameType), dificultad(mc.Difficulty)].filter(Boolean));
    agregarLista(resumen, 'Tiempo y spawn', [mc.Time != null ? 'Tiempo ' + mc.Time : '', spawn(mc)].filter(Boolean));
    agregarLista(resumen, 'Avisos', mc.warnings);
    if (!resumen.children.length) agregarCampo(resumen, 'Contenido', 'Paquete Minecraft leído localmente, sin ejecutar contenido');

    const galeria = panel.querySelector('.mc-galeria');
    const imagenes = (mc.images || []).filter(entradaValida);
    if (!imagenes.length) {
        const vacio = document.createElement('p');
        vacio.className = 'mc-vacio';
        vacio.textContent = 'Este paquete no contiene imágenes PNG/JPG.';
        galeria.appendChild(vacio);
    }
    for (const imagen of imagenes) {
        const url = rutaZip(imagen.index, imagen.ext);
        const tarjeta = document.createElement('article');
        tarjeta.className = 'mc-textura';
        const img = document.createElement('img');
        img.src = url; img.alt = imagen.name; img.loading = 'lazy';
        const nombreArchivo = document.createElement('span');
        nombreArchivo.textContent = imagen.name;
        const skin = document.createElement('button');
        skin.type = 'button'; skin.textContent = 'Minecraft';
        skin.title = 'Ver como posible skin 2D/3D';
        skin.onclick = async () => {
            const modulo = await import('/app/skin.js');
            await modulo.initSkin({ ...estado, nombre: imagen.name, src: url }, document.getElementById('pantalla'));
        };
        tarjeta.append(img, nombreArchivo, skin);
        galeria.appendChild(tarjeta);
    }
    const datos = panel.querySelector('.mc-json');
    datos.textContent = JSON.stringify(mc, null, 2);
    const pestanas = panel.querySelectorAll('.mc-tabs button');
    pestanas.forEach(boton => boton.addEventListener('click', () => {
        pestanas.forEach(b => b.classList.toggle('activa', b === boton));
        const tab = boton.dataset.tab;
        resumen.hidden = tab !== 'resumen';
        galeria.hidden = tab !== 'texturas';
        datos.hidden = tab !== 'datos';
        listaZip.hidden = tab !== 'archivos';
    }));
    listaZip.hidden = true;
    return panel;
}

function agregarLista(panel, titulo, valores) {
    if (!valores || !valores.length) return;
    agregarCampo(panel, titulo, valores.filter(v => v != null && String(v).length > 0).join(' · '));
}

function agregarCampo(panel, titulo, texto) {
    const fila = document.createElement('div');
    fila.className = 'mc-campo';
    const fuerte = document.createElement('strong');
    fuerte.textContent = titulo;
    const span = document.createElement('span');
    span.textContent = texto;
    fila.append(fuerte, span);
    panel.appendChild(fila);
}

function descripcionTipo(kind) {
    return ({ bedrockPack: 'Paquete Bedrock', bedrockAddon: 'Addon Bedrock', bedrockWorld: 'Mundo Bedrock', javaMod: 'Mod Java' })[kind] || 'Contenido Minecraft';
}
function tipoJuego(v) { return v == null ? '' : 'Modo de juego ' + v; }
function dificultad(v) { return v == null ? '' : 'Dificultad ' + v; }
function spawn(mc) {
    if (mc.SpawnX == null) return '';
    return 'Spawn ' + [mc.SpawnX, mc.SpawnY, mc.SpawnZ].filter(v => v != null).join(' / ');
}
function mensaje(dato) {
    try { window.chrome?.webview?.postMessage(dato); } catch { }
}

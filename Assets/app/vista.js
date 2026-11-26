// Vistia - despachador de visores (todo local, cero internet)
const pantalla = document.getElementById('pantalla');
let estadoActual = null;
let generacionMedia = 0;
let generacionVista = 0;

function origenTexto(estado) {
    return origen(estado);
}

function origenHtml(estado) {
    if (estado.zipi != null) return origen(estado);
    const base = '/html/' + encodeURIComponent(estado.nombre || ('archivo.' + (estado.ext || 'html')));
    return estado.archivoId ? base + '?id=' + encodeURIComponent(estado.archivoId) : base;
}

function esperar(ms) { return new Promise(resolve => setTimeout(resolve, ms)); }

function avisarListo() {
    try { window.chrome?.webview?.postMessage({ cmd: 'listo' }); } catch (e) { }
}

// ruta de contenido: archivo directo o entrada interna del comprimido (sin descomprimir)
function origen(estado) {
    const base = estado.zipi != null
        ? '/zipfile/' + estado.zipi + '/' + encodeURIComponent(estado.ext)
        : '/file/' + encodeURIComponent(estado.ext);
    const id = estado.archivoId || estado.mediaId || '';
    // La identidad evita que WebView2 reutilice /file/mp4 de otro archivo.
    return id ? base + '?id=' + encodeURIComponent(id) : base;
}

function escapar(s) {
    return String(s).replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

function tamHumano(b) {
    if (b == null) return '';
    if (b < 1024) return b + ' B';
    if (b < 1048576) return (b / 1024).toFixed(1) + ' KB';
    if (b < 1073741824) return (b / 1048576).toFixed(1) + ' MB';
    return (b / 1073741824).toFixed(2) + ' GB';
}

// ---------- iconos (línea, estilo tintero) ----------
const ICONOS = {
    imagen: '<rect x="4" y="5" width="24" height="20" rx="3"/><circle cx="12" cy="12" r="2.6"/><path d="M6 22l6.5-7 4 4.5 3.5-4 5 6.5"/>',
    musica: '<path d="M10 20V7l12-2v13"/><circle cx="7.5" cy="20" r="2.6"/><circle cx="19.5" cy="18" r="2.6"/>',
    video: '<rect x="4" y="6" width="24" height="18" rx="3"/><path d="M13 11l7 4-7 4z"/>',
    pdf: '<path d="M8 4h11l5 5v19H8z"/><path d="M19 4v5h5"/><path d="M11.5 17c0-2 1-3 2.5-3s2.5 1 2.5 3-1 3-2.5 3-2.5-1-2.5-3z"/>',
    documento: '<path d="M8 4h11l5 5v19H8z"/><path d="M19 4v5h5"/><path d="M11 14h10M11 18h10M11 22h7"/>',
    hoja: '<rect x="5" y="5" width="22" height="22" rx="2"/><path d="M5 12h22M5 19h22M12 5v22M19 5v22"/>',
    presentacion: '<rect x="4" y="6" width="24" height="17" rx="2"/><path d="M16 23v4M11 27h10"/><path d="M11 18V12l8 3z"/>',
    comprimido: '<rect x="6" y="4" width="20" height="24" rx="2"/><path d="M16 4v3M16 9v3M16 14v3"/><rect x="14" y="19" width="4" height="5" rx="1"/>',
    modelo: '<path d="M16 4l11 6.5v13L16 30 5 23.5v-13z"/><path d="M5 10.5l11 6.5 11-6.5M16 17v13"/>',
    fuente: '<path d="M6 24L13 6l7 18M9 18h8"/><path d="M22 24c0-4 6-4 6-1 0 2-3 2-3 4 0 1 1 1 2 1"/>',
    texto: '<path d="M8 4h11l5 5v19H8z"/><path d="M19 4v5h5"/><path d="M11 15h10M11 19h10M11 23h6"/>',
    carpeta: '<path d="M4 8a2 2 0 0 1 2-2h7l3 3h10a2 2 0 0 1 2 2v13a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z"/>',
    archivo: '<path d="M8 4h11l5 5v19H8z"/><path d="M19 4v5h5"/><path d="M16 14v8M12.5 19l3.5 3 3.5-3"/>'
};
function svgIcono(cat) {
    const d = ICONOS[cat] || ICONOS.archivo;
    return '<svg class="ico" viewBox="0 0 32 32">' + d + '</svg>';
}

function categoria(ext) {
    ext = (ext || '').toLowerCase();
    const mapa = {
        png: 'imagen', jpg: 'imagen', jpeg: 'imagen', gif: 'imagen', webp: 'imagen', bmp: 'imagen', svg: 'imagen', ico: 'imagen', tif: 'imagen', tiff: 'imagen', avif: 'imagen', heic: 'imagen', heif: 'imagen', jxl: 'imagen', dng: 'imagen', cr2: 'imagen', cr3: 'imagen', nef: 'imagen', arw: 'imagen', raw: 'imagen', psd: 'imagen', psb: 'imagen', ai: 'imagen', eps: 'imagen', qoi: 'imagen', tga: 'imagen', dds: 'imagen', exr: 'imagen', hdr: 'imagen',
        mp3: 'musica', wav: 'musica', ogg: 'musica', oga: 'musica', flac: 'musica', m4a: 'musica', aac: 'musica', opus: 'musica', wma: 'musica', aiff: 'musica', ape: 'musica',
        mp4: 'video', webm: 'video', mov: 'video', m4v: 'video', mkv: 'video', avi: 'video', wmv: 'video', flv: 'video', mpg: 'video', mpeg: 'video', ts: 'video', m2ts: 'video', '3gp': 'video',
        pdf: 'pdf', docx: 'documento', doc: 'documento', rtf: 'documento', odt: 'documento',
        xlsx: 'hoja', xls: 'hoja', csv: 'hoja', tsv: 'hoja', ods: 'hoja', pptx: 'presentacion', ppt: 'presentacion',
        zip: 'comprimido', rar: 'comprimido', '7z': 'comprimido', jar: 'comprimido', apk: 'comprimido', gz: 'comprimido',
        glb: 'modelo', gltf: 'modelo', obj: 'modelo', stl: 'modelo', ply: 'modelo', fbx: 'modelo', '3ds': 'modelo', dae: 'modelo', '3mf': 'modelo',
        ttf: 'fuente', otf: 'fuente', woff: 'fuente', woff2: 'fuente',
        md: 'texto', txt: 'texto', log: 'texto', json: 'texto', xml: 'texto', html: 'texto', js: 'texto', py: 'texto', cs: 'texto', java: 'texto', css: 'texto'
    };
    return mapa[ext] || 'archivo';
}

// ---------- visores ----------

function visorImagen(estado) {
    pantalla.innerHTML = '<div class="v-imagen"><img alt=""></div>';
    pantalla.querySelector('img').src = estado.src || origen(estado);
}

async function visorMedia(estado, seguir = () => true) {
    const { Reproductor } = await import('/app/media/Reproductor.js');
    if (!seguir()) return;
    new Reproductor(estado, pantalla, origen(estado));
}

function visorPdf(estado) {
    pantalla.innerHTML = '<div class="v-pdf"><iframe src="' + origen(estado) + '" title="pdf"></iframe></div>';
}

const LANGS = {
    py: 'python', pyw: 'python', ino: 'cpp', js: 'javascript', mjs: 'javascript', cjs: 'javascript',
    ts: 'typescript', tsx: 'typescript', jsx: 'javascript', cs: 'csharp', java: 'java', kt: 'kotlin',
    c: 'c', h: 'c', cpp: 'cpp', hpp: 'cpp', cc: 'cpp', rs: 'rust', go: 'go', rb: 'ruby', php: 'php',
    sql: 'sql', html: 'xml', htm: 'xml', xml: 'xml', xaml: 'xml', svg: 'xml', css: 'css', scss: 'scss',
    json: 'json', yml: 'yaml', yaml: 'yaml', toml: 'ini', ini: 'ini', cfg: 'ini', conf: 'ini',
    ps1: 'powershell', psm1: 'powershell', bat: 'dos', cmd: 'dos', sh: 'bash', bash: 'bash',
    md: 'markdown', srt: 'plaintext', vtt: 'plaintext', log: 'plaintext', txt: 'plaintext',
    lua: 'lua', pl: 'perl', r: 'r', dart: 'dart', swift: 'swift', scala: 'scala', vb: 'vbnet',
    gradle: 'groovy', diff: 'diff', patch: 'diff', fs: 'fsharp', dart2: 'dart'
};

async function obtenerTexto(estado) {
    const respuesta = await fetch(origenTexto(estado), { headers: { Range: 'bytes=0-4194303' } });
    const buf = await respuesta.arrayBuffer();
    let texto;
    try { texto = new TextDecoder('utf-8', { fatal: true }).decode(buf); }
    catch (e) { texto = new TextDecoder('windows-1252').decode(buf); }
    if (texto.charCodeAt(0) === 0xFEFF) texto = texto.slice(1);
    let recortado = false;
    const rango = respuesta.headers.get('Content-Range') || '';
    const m = /bytes\s+0-(\d+)\/(\d+)/i.exec(rango);
    if (m) recortado = Number(m[2]) > Number(m[1]) + 1;
    return { texto, recortado };
}

async function copiarCodigo(texto, boton) {
    try {
        await navigator.clipboard.writeText(texto);
    } catch (e) {
        const area = document.createElement('textarea');
        area.value = texto;
        area.style.position = 'fixed'; area.style.opacity = '0';
        document.body.appendChild(area); area.select();
        document.execCommand('copy'); area.remove();
    }
    boton.textContent = 'Copiado';
    setTimeout(() => { boton.textContent = 'Copiar'; }, 1300);
}

async function visorTexto(estado, destino = pantalla, seguir = () => true) {
    const datos = await obtenerTexto(estado);
    if (!seguir()) return;
    const texto = datos.texto;
    destino.innerHTML = '<div class="v-codigo"><div class="codigo-herramientas"><input type="search" placeholder="Buscar en el código" aria-label="Buscar en el código"><button type="button" class="cod-buscar">Siguiente</button><span class="cod-conteo"></span><span class="cod-espacio"></span><button type="button" class="cod-envolver">Ajustar líneas</button><button type="button" class="cod-copiar">Copiar</button><button type="button" class="cod-editor">Abrir en editor</button></div><div class="codigo-aviso"></div><pre><code></code></pre></div>';
    const caja = destino.querySelector('.v-codigo');
    const code = caja.querySelector('code');
    const aviso = caja.querySelector('.codigo-aviso');
    if (datos.recortado) aviso.textContent = 'Vista rápida: se muestran los primeros 4 MB. El archivo original está completo.';
    let lang = LANGS[(estado.ext || '').toLowerCase()] || 'plaintext';
    if (!window.hljs.getLanguage(lang)) lang = 'plaintext';
    const todas = texto.split('\n');
    const limite = 12000;
    if (todas.length > limite) aviso.textContent = 'Vista rápida: se muestran las primeras ' + limite.toLocaleString('es') + ' líneas de ' + todas.length.toLocaleString('es') + '.';
    const visible = todas.length > limite ? todas.slice(0, limite).join('\n') : texto;
    let html;
    try { html = window.hljs.highlight(visible, { language: lang }).value; }
    catch (e) { html = escapar(visible); }
    const partes = html.split('\n');
    let out = '';
    for (let i = 0; i < partes.length; i++) {
        out += '<div class="linea"><span class="num">' + (i + 1) + '</span><span class="cod">' + (partes[i] || ' ') + '</span></div>';
    }
    code.innerHTML = out;
    const lineas = Array.from(code.querySelectorAll('.linea'));
    const busqueda = caja.querySelector('input');
    const conteo = caja.querySelector('.cod-conteo');
    let coincidencias = [];
    let posicion = -1;
    function buscar(avanzar) {
        const q = busqueda.value.toLocaleLowerCase();
        lineas.forEach(l => l.classList.remove('linea-resaltada'));
        if (!q) { coincidencias = []; posicion = -1; conteo.textContent = ''; return; }
        if (!avanzar || !coincidencias.length) {
            coincidencias = lineas.filter(l => l.textContent.toLocaleLowerCase().includes(q));
            posicion = -1;
        }
        conteo.textContent = coincidencias.length ? coincidencias.length + ' coincidencias' : 'Sin coincidencias';
        if (!coincidencias.length) return;
        posicion = (posicion + 1) % coincidencias.length;
        coincidencias[posicion].classList.add('linea-resaltada');
        coincidencias[posicion].scrollIntoView({ block: 'center' });
    }
    busqueda.addEventListener('input', () => buscar(false));
    busqueda.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); buscar(true); } });
    caja.querySelector('.cod-buscar').onclick = () => buscar(true);
    caja.querySelector('.cod-copiar').onclick = ev => copiarCodigo(texto, ev.currentTarget);
    caja.querySelector('.cod-envolver').onclick = ev => {
        caja.classList.toggle('envolver');
        ev.currentTarget.textContent = caja.classList.contains('envolver') ? 'Sin ajuste' : 'Ajustar líneas';
    };
    caja.querySelector('.cod-editor').onclick = () => {
        try { window.chrome?.webview?.postMessage({ cmd: 'abrir-editor' }); } catch (e) { }
    };
}

function crearMarcoHtml(estado) {
    const marco = document.createElement('iframe');
    marco.className = 'html-vivo';
    marco.title = 'Vista renderizada de ' + (estado.nombre || 'página HTML');
    marco.setAttribute('sandbox', 'allow-scripts allow-forms allow-modals allow-pointer-lock');
    marco.setAttribute('referrerpolicy', 'no-referrer');
    marco.src = origenHtml(estado);
    return marco;
}

function visorHtml(estado) {
    pantalla.innerHTML = '<div class="v-html"><div class="html-aviso">Página local aislada · sin internet · Código para ver su fuente · Dividir para ver ambas</div><div class="html-contenido"></div></div>';
    pantalla.querySelector('.html-contenido').appendChild(crearMarcoHtml(estado));
}

async function dividirHtml(estado) {
    pantalla.innerHTML = '<div class="v-html-dividido"><section><div class="html-seccion">PÁGINA</div><div class="html-lado"></div></section><section><div class="html-seccion">CÓDIGO FUENTE</div><div class="codigo-lado"></div></section></div>';
    pantalla.querySelector('.html-lado').appendChild(crearMarcoHtml(estado));
    await visorTexto(estado, pantalla.querySelector('.codigo-lado'));
}

async function visorMarkdown(estado, seguir = () => true) {
    const texto = await (await fetch(origen(estado))).text();
    if (!seguir()) return;
    pantalla.innerHTML = '<div class="v-markdown"></div>';
    const caja = pantalla.firstChild;
    const plantilla = document.createElement('template');
    plantilla.innerHTML = window.marked.parse(texto);
    plantilla.content.querySelectorAll('script,iframe,object,embed,link,meta,base,form').forEach(n => n.remove());
    plantilla.content.querySelectorAll('*').forEach(n => {
        for (const a of Array.from(n.attributes)) {
            if (/^on/i.test(a.name) || (/^(href|src)$/i.test(a.name) && /^\s*javascript:/i.test(a.value))) n.removeAttribute(a.name);
        }
    });
    caja.appendChild(plantilla.content.cloneNode(true));
    caja.querySelectorAll('img').forEach(i => { i.onerror = () => i.remove(); });
}

async function visorDocx(estado) {
    // visor propio: el texto completo sale al instante (parser C#); el formato se pide aparte
    if (estado.textoDocx && estado.textoDocx.length) {
        let parrafos = '';
        for (const p of estado.textoDocx) parrafos += '<p>' + escapar(p) + '</p>';
        pantalla.innerHTML = '<div class="v-docx-texto"><div class="aviso-propio">Texto completo al instante · visor propio de Vistia</div>' +
            '<button class="boton-formato">Ver con formato</button><div class="docx-parrafos">' + parrafos + '</div></div>';
        pantalla.querySelector('.boton-formato').onclick = () => renderDocxCompleto(estado);
        avisarListo();
        return;
    }
    await renderDocxCompleto(estado);
}

async function renderDocxCompleto(estado) {
    pantalla.innerHTML = '<div class="v-docx"></div>';
    const caja = pantalla.firstChild;
    const buf = await (await fetch(origen(estado))).arrayBuffer();
    await window.docx.renderAsync(buf, caja);
}

function letrasColumna(n) {
    // 0 -> A, 1 -> B, ... 26 -> AA
    let s = '';
    n = n + 1;
    while (n > 0) {
        const r = (n - 1) % 26;
        s = String.fromCharCode(65 + r) + s;
        n = Math.floor((n - 1) / 26);
    }
    return s;
}

function tablaHoja(ws) {
    // matriz de celdas con formato visual de la primera fila en negrita
    const filas = window.XLSX.utils.sheet_to_json(ws, { header: 1, raw: false, defval: '' });
    let ancho = 1;
    for (const f of filas) ancho = Math.max(ancho, f.length);
    ancho = Math.min(ancho + 1, 200); // +1 por la columna de números

    let html = '<table class="hoja-tabla"><thead><tr><th class="esquina"></th>';
    for (let c = 1; c < ancho; c++) html += '<th>' + letrasColumna(c - 1) + '</th>';
    html += '</tr></thead><tbody>';
    filas.forEach((fila, i) => {
        html += '<tr><th class="num">' + (i + 1) + '</th>';
        const negrita = i === 0;
        for (let c = 0; c < ancho - 1; c++) {
            const v = fila[c] == null ? '' : String(fila[c]);
            html += '<td' + (negrita && v !== '' ? ' class="fuerte"' : '') + '>' + escapar(v) + '</td>';
        }
        html += '</tr>';
    });
    html += '</tbody></table>';
    return html;
}

async function visorHoja(estado, seguir = () => true) {
    const buf = await (await fetch(origen(estado))).arrayBuffer();
    if (!seguir()) return;
    const wb = window.XLSX.read(new Uint8Array(buf), { type: 'array' });
    pantalla.innerHTML = '<div class="v-hoja"><div class="hoja-tabs"></div><div class="hoja-contenido"></div></div>';
    const tabs = pantalla.querySelector('.hoja-tabs');
    const contenido = pantalla.querySelector('.hoja-contenido');
    wb.SheetNames.forEach((nombre, i) => {
        const b = document.createElement('button');
        b.textContent = nombre;
        b.onclick = () => {
            tabs.querySelectorAll('button').forEach(x => x.classList.remove('activa'));
            b.classList.add('activa');
            contenido.innerHTML = tablaHoja(wb.Sheets[nombre]);
        };
        tabs.appendChild(b);
        if (i === 0) b.click();
    });
}

async function visorPptx(estado) {
    // visor propio: texto de todas las láminas al instante (parser C#)
    if (estado.laminas && estado.laminas.length) {
        let listado = '';
        for (const l of estado.laminas) {
            listado += '<div class="lamina"><div class="lamina-n">Lámina ' + l.n + '</div><div class="lamina-t">' + escapar(l.t) + '</div></div>';
        }
        pantalla.innerHTML = '<div class="v-pptx-texto"><div class="aviso-propio">Láminas al instante · visor propio de Vistia</div>' +
            '<button class="boton-formato">Ver las diapositivas</button><div class="laminas">' + listado + '</div></div>';
        pantalla.querySelector('.boton-formato').onclick = () => renderPptxCompleto(estado);
        avisarListo();
        return;
    }
    await renderPptxCompleto(estado);
}

async function renderPptxCompleto(estado) {
    pantalla.innerHTML = '<div class="v-pptx"></div>';
    const caja = pantalla.firstChild;
    try {
        const mod = await import('pptx-preview');
        const buf = await (await fetch(origen(estado))).arrayBuffer();
        const visor = mod.init(caja, { width: 940, height: 540 });
        visor.preview(buf);
    } catch (err) {
        pantalla.innerHTML = '<div class="v-tarjeta"><div class="tarjeta">' + svgIcono('presentacion') +
            '<div class="n">Vista de PowerPoint no disponible para este archivo</div>' +
            '<div class="d">Pulsa Enter para abrirlo con PowerPoint</div></div></div>';
    }
}

async function visorPsd(estado, seguir = () => true) {
    try {
        const buf = await (await fetch(origen(estado))).arrayBuffer();
        if (!seguir()) return;
        const lib = window.agPsd || window.agpsd;
        const psd = lib.readPsd(buf);
        if (psd.canvas) {
            pantalla.innerHTML = '<div class="v-psd"><div class="psd-aviso">Vistia · visor propio de Photoshop (.psd)</div></div>';
            const caja = pantalla.querySelector('.v-psd');
            psd.canvas.style.maxWidth = '100%';
            psd.canvas.style.maxHeight = 'calc(100vh - 90px)';
            psd.canvas.style.border = '1px solid var(--linea)';
            psd.canvas.style.background = '#fff';
            caja.appendChild(psd.canvas);
            if (psd.children && psd.children.length) {
                const capas = psd.children.filter(c => c.name).map(c => escapar(c.name)).join(' · ');
                const info = document.createElement('div');
                info.className = 'psd-capas';
                info.textContent = 'Capas: ' + (capas || 'sin nombres');
                caja.appendChild(info);
            }
        } else {
            // sin imagen embebida: lista de capas
            const capas = (psd.children || []).filter(c => c.name);
            let listado = '';
            for (const c of capas) listado += '<div class="lamina"><div class="lamina-n">' + escapar(c.name) + '</div><div class="lamina-t">' + (c.type || 'capa') + '</div></div>';
            if (!listado) listado = '<div class="lamina"><div class="lamina-t">sin capas con nombre</div></div>';
            pantalla.innerHTML = '<div class="v-pptx-texto"><div class="aviso-propio">Este PSD no trae imagen combinada; capas detectadas con el visor propio de Vistia</div>' +
                '<button class="boton-formato">Abrir con tu editor (Enter)</button><div class="laminas">' + listado + '</div></div>';
            pantalla.querySelector('.boton-formato').onclick = () => {
                try { window.chrome?.webview?.postMessage({ cmd: 'abrir' }); } catch (e) { }
            };
        }
        avisarListo();
    } catch (e) {
        tarjetaError(estado, e);
    }
}

async function visorZip(estado, seguir = () => true) {
    if (estado.error) {
        pantalla.innerHTML = '<div class="v-tarjeta"><div class="tarjeta">' + svgIcono('comprimido') +
            '<div class="n">No se pudo abrir el comprimido</div><div class="d">' + escapar(estado.error) + '</div></div></div>';
        return;
    }
    let filas = '';
    for (const f of (estado.filas || [])) {
        const esCarpeta = /\/$/.test(f.n);
        const clicable = !esCarpeta && f.i != null;
        filas += '<tr class="' + (clicable ? 'fila-abrir' : '') + '"' +
            (clicable ? ' data-i="' + f.i + '" data-e="' + escapar(f.e) + '" data-n="' + escapar(f.n) + '"' : '') +
            '><td class="' + (esCarpeta ? 'carpeta' : '') + '">' + escapar(f.n) + '</td><td>' + tamHumano(f.t) + '</td><td>' + tamHumano(f.c) + '</td></tr>';
    }
    pantalla.innerHTML =
        '<div class="v-zip"><div class="zip-cab">' + escapar(estado.total) + ' entradas  -  descomprimido ' + tamHumano(estado.des) +
        '  -  comprimido ' + tamHumano(estado.com) +
        (estado.omitidas > 0 ? '  -  se muestran las primeras ' + escapar(estado.filas.length) : '') +
        '  -  toca una fila para verla sin descomprimir' +
        '</div><div class="zip-lista"><table><tbody>' + filas + '</tbody></table></div></div>';
    if (estado.mc) {
        const modulo = await import('/app/minecraft.js');
        if (!seguir()) return;
        const lista = pantalla.querySelector('.zip-lista');
        const panel = modulo.crearMinecraftPanel(estado, lista);
        pantalla.querySelector('.v-zip').insertBefore(panel, lista);
    }
    // abrir entradas internas sin descomprimir
    pantalla.querySelectorAll('tr.fila-abrir').forEach(tr => {
        tr.onclick = () => {
            try {
                window.chrome?.webview?.postMessage({
                    cmd: 'zip-abrir', i: parseInt(tr.dataset.i, 10),
                    ext: tr.dataset.e || '', nombre: tr.dataset.n || ''
                });
            } catch (e) { }
        };
    });
}

function visorCarpeta(estado) {
    let items = '';
    for (const it of (estado.items || [])) {
        const mini = it.t
            ? '<img src="' + it.t + '" alt="" loading="lazy">'
            : svgIcono(it.cat);
        items += '<div class="item-car"><div class="mini">' + mini + '</div><div class="nom">' + escapar(it.n) + '</div></div>';
    }
    pantalla.innerHTML = '<div class="v-carpeta">' + (items || '<div class="aviso-carga">Carpeta vacía</div>') + '</div>';
}

function visorFuente(estado) {
    const familia = 'TinteroVistaFuente';
    pantalla.innerHTML = '<style>@font-face{font-family:"' + familia + '";src:url("/file/' + encodeURIComponent(estado.ext) + '");}</style>' +
        '<div class="v-fuente" style="font-family:\'' + familia + '\'">' +
        '<div class="muestra1">AaBbCcDdEeFf 0123456789</div>' +
        '<div class="muestra1" style="color:var(--acento)">áéíóú ÁÉÍÓÚ ñÑ üÜ ¿? ¡!</div>' +
        '<hr><div class="muestra2">La tinta fluye sobre el papel cálido de Vistia.</div>' +
        '<div class="muestra2" style="font-style:italic">Vista previa de la fuente ' + escapar(estado.nombre) + '</div>' +
        '<hr><div class="muestra3">ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz 0123456789 !@#%*()[]{} - _ = + / &</div>' +
        '</div>';
}

function visorTarjeta(estado) {
    const cat = estado.cat || categoria(estado.ext);
    if (estado.error) {
        pantalla.innerHTML = '<div class="v-tarjeta"><div class="tarjeta">' + svgIcono(cat) +
            '<div class="n">No se pudo mostrar ' + escapar(estado.nombre || 'el archivo') + '</div>' +
            '<div class="d">' + escapar(estado.error) + '</div><div class="pista">Enter abre con su aplicación</div></div></div>';
        return;
    }
    pantalla.innerHTML = '<div class="v-tarjeta"><div class="tarjeta">' + svgIcono(cat) +
        '<div class="n">' + escapar(estado.nombre) + '</div>' +
        '<div class="d">' + (estado.ext ? escapar(estado.ext.toUpperCase()) + ' - ' : '') + tamHumano(estado.tam) +
        (estado.modif ? ' - modificado ' + escapar(estado.modif) : '') + '</div>' +
        '<div class="pista">Enter abre con su aplicacion - espacio cierra</div></div></div>';
}

function tarjetaError(estado, err) {
    pantalla.innerHTML = '<div class="v-tarjeta"><div class="tarjeta">' + svgIcono('archivo') +
        '<div class="n">No se pudo mostrar</div>' +
        '<div class="d">' + escapar(err && err.message || err) + '</div>' +
        '<div class="pista">Enter abre con su aplicacion</div></div></div>';
}

// ---------- mensajes del host ----------
function detenerVisores() {
    generacionVista++;
    generacionMedia++;
    try { if (window.__pararMedia) window.__pararMedia(); }
    catch (e) { try { window.chrome?.webview?.postMessage({ cmd: 'jserror', msg: 'stop media: ' + String(e) }); } catch (_) { } }
    try { if (window.__parar3d) window.__parar3d(); }
    catch (e) { try { window.chrome?.webview?.postMessage({ cmd: 'jserror', msg: 'stop 3d: ' + String(e) }); } catch (_) { } }
    window.__alternarMedia = null;
    window.__pararMedia = null;
    window.__parar3d = null;
}

function ejecutarVista(accion, estado) {
    Promise.resolve(accion()).then(() => {
        // Una promesa anterior no puede ocultar la carga del archivo nuevo.
        if (estado && estado === estadoActual) avisarListo();
    }).catch(e => {
        if (estado && estado !== estadoActual) return;
        tarjetaError(estado || estadoActual || { modo: '?' }, e);
        try { window.chrome?.webview?.postMessage({ cmd: 'jserror', msg: String(e && e.message || e) }); } catch (_) { }
    });
}

try {
    window.chrome?.webview?.addEventListener('message', ev => {
        const d = ev.data || {};
        if (d.cmd === 'toggle' && window.__alternarMedia) window.__alternarMedia();
        if (d.cmd === 'stop') detenerVisores();
        if (d.cmd === 'estado' && d.estado) {
            try { window.chrome?.webview?.postMessage({ cmd: 'estado-recibido', modo: d.estado.modo }); } catch (e) { }
            detenerVisores();
            estadoActual = d.estado;
            pantalla.innerHTML = '';
            ejecutarVista(() => mostrarEstado(estadoActual), estadoActual);
            return;
        }
        if (!estadoActual) return;
        if (d.cmd === 'codigo') ejecutarVista(() => visorTexto(estadoActual), estadoActual);
        if (d.cmd === 'pagina') {
            detenerVisores();
            if (estadoActual.modo === 'html') ejecutarVista(() => visorHtml(estadoActual), estadoActual);
            else if (estadoActual.modo === 'markdown') ejecutarVista(() => visorMarkdown(estadoActual), estadoActual);
            else ejecutarVista(() => visorTexto(estadoActual), estadoActual);
        }
        if (d.cmd === 'dividir' && estadoActual.modo === 'html') {
            detenerVisores();
            ejecutarVista(() => dividirHtml(estadoActual), estadoActual);
        }
    });
} catch (e) { }

// ---------- visores ----------
async function mostrarEstado(estado) {
    const generacion = ++generacionVista;
    const seguir = () => generacion === generacionVista;
    document.body.className = estado.tema === 'oscuro' ? 'tema-oscuro' : 'tema-claro';
    // barra de regreso al comprimido (estamos viendo un archivo interno)
    const volver = document.getElementById('volver');
    if (volver) {
        if (estado.zipVolver) {
            volver.style.display = 'block';
            volver.textContent = '← Volver a ' + (estado.zipNombre || 'comprimido');
        } else {
            volver.style.display = 'none';
        }
    }
    switch (estado.modo) {
        case 'imagenweb': visorImagen(estado); break;
        case 'media': await visorMedia(estado, seguir); break;
        case 'skin': { const m = await import('/app/skin.js'); if (!seguir()) return; await m.initSkin(estado, pantalla, seguir); break; }
        case 'pdf': visorPdf(estado); break;
        case 'html': visorHtml(estado); break;
        case 'texto': await visorTexto(estado, pantalla, seguir); break;
        case 'markdown': await visorMarkdown(estado, seguir); break;
        case 'docx': await visorDocx(estado); break;
        case 'hoja': await visorHoja(estado, seguir); break;
        case 'presentacion': await visorPptx(estado); break;
        case 'zip': await visorZip(estado, seguir); break;
        case 'carpeta': visorCarpeta(estado); break;
        case 'fuente': visorFuente(estado); break;
        case 'modelo3d': { const m = await import('/app/modelo.js'); if (!seguir()) return; await m.initModelo(estado, pantalla, seguir); break; }
        case 'psd': await visorPsd(estado, seguir); break;
        default: visorTarjeta(estado); break;
    }
}

// Puente mínimo para automatización y verificación del visor dentro de Vistia.
window.__vistia = Object.freeze({
    codigo: () => visorTexto(estadoActual),
    dividir: () => dividirHtml(estadoActual),
    pagina: () => visorHtml(estadoActual)
});

// ---------- principal ----------
(async () => {
    try {
        const estado = await (await fetch('/estado.json', { cache: 'no-store' })).json();
        estadoActual = estado;
        await mostrarEstado(estado);
    } catch (err) {
        tarjetaError({ modo: '?' }, err);
    }
    avisarListo();
})();

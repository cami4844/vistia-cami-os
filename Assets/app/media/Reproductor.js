import { PreparacionMedia } from './PreparacionMedia.js';
import { MiniaturasMedia } from './MiniaturasMedia.js';

export class Reproductor {
    constructor(estado, pantalla, origen) {
        this.estado = estado;
        this.origen = origen;
        this.esAudio = /^(mp3|wav|ogg|oga|flac|m4a|aac|opus|wma|aiff|aif|ape)$/.test(estado.ext || '');
        this.disposed = false;
        this.preparando = false;
        this.usandoCache = false;
        this.pantallaCompleta = false;
        this.abortos = new AbortController();
        this.prefs = this.leerPrefs();
        this.raiz = document.createElement('section');
        this.raiz.className = 'reproductor' + (this.esAudio ? ' es-audio' : ' es-video');
        this.raiz.tabIndex = 0;
        this.raiz.setAttribute('aria-label', 'Reproductor de ' + (estado.nombre || 'archivo'));
        this.raiz.innerHTML = `
            <div class="escenario">
                <div class="audio-portada" ${this.esAudio ? '' : 'hidden'}><span>AUDIO</span><div class="audio-onda"><i></i><i></i><i></i><i></i><i></i></div></div>
                <div class="media-cargando">Cargando…</div>
                <button type="button" class="play-grande" aria-label="Reproducir">Reproducir</button>
                <div class="media-preparacion" hidden><strong>Preparando una copia compatible</strong><span class="preparacion-texto"></span><progress max="1" value="0"></progress><small>El original no se modifica.</small></div>
                <div class="media-error" hidden><strong>No se pudo reproducir</strong><p></p><div><button type="button" data-accion="reintentar">Reintentar</button><button type="button" data-accion="forzar">Forzar copia compatible</button><button type="button" data-accion="abrir">Abrir original</button></div></div>
                <div class="mini-tooltip" hidden><div class="mini-imagen"></div><span class="mini-tiempo"></span></div>
            </div>
            <div class="controles">
                <div class="progreso-linea"><input class="seek" type="range" min="0" max="100" step="0.01" value="0" aria-label="Posición de reproducción" disabled></div>
                <div class="botones">
                    <button type="button" data-accion="play">Reproducir</button>
                    <button type="button" data-accion="menos">−10</button>
                    <button type="button" data-accion="mas">+10</button>
                    <span class="tiempo">0:00 / 0:00</span>
                    <button type="button" data-accion="mute">Silencio</button>
                    <input class="volumen" type="range" min="0" max="100" step="1" value="100" aria-label="Volumen">
                    <select class="velocidad" aria-label="Velocidad"><option value="0.5">0.5×</option><option value="0.75">0.75×</option><option value="1" selected>1×</option><option value="1.25">1.25×</option><option value="1.5">1.5×</option><option value="2">2×</option></select>
                    <button type="button" data-accion="bucle">Bucle</button>
                    <button type="button" data-accion="aspecto">Ajustar</button>
                    <button type="button" data-accion="pip" ${this.esAudio ? 'hidden' : ''}>PiP</button>
                    <button type="button" data-accion="pantalla">Pantalla completa</button>
                </div>
            </div>`;
        pantalla.replaceChildren(this.raiz);
        this.escenario = this.raiz.querySelector('.escenario');
        this.cargando = this.raiz.querySelector('.media-cargando');
        this.playGrande = this.raiz.querySelector('.play-grande');
        this.play = this.raiz.querySelector('[data-accion="play"]');
        this.seek = this.raiz.querySelector('.seek');
        this.tiempo = this.raiz.querySelector('.tiempo');
        this.volumen = this.raiz.querySelector('.volumen');
        this.velocidad = this.raiz.querySelector('.velocidad');
        this.bucle = this.raiz.querySelector('[data-accion="bucle"]');
        this.aspecto = this.raiz.querySelector('[data-accion="aspecto"]');
        this.pip = this.raiz.querySelector('[data-accion="pip"]');
        this.preparacionPanel = this.raiz.querySelector('.media-preparacion');
        this.preparacionTexto = this.raiz.querySelector('.preparacion-texto');
        this.preparacionBarra = this.raiz.querySelector('progress');
        this.errorPanel = this.raiz.querySelector('.media-error');
        this.errorTexto = this.errorPanel.querySelector('p');
        this.miniTooltip = this.raiz.querySelector('.mini-tooltip');
        this.miniImagen = this.raiz.querySelector('.mini-imagen');
        this.miniTiempo = this.raiz.querySelector('.mini-tiempo');
        this.raiz.querySelector('.media-nombre')?.remove();

        this.el = document.createElement(this.esAudio ? 'audio' : 'video');
        this.el.controls = false;
        this.el.preload = 'metadata';
        this.el.playsInline = true;
        this.el.className = 'elemento-media';
        if (!this.esAudio && /^(mov|webm)$/.test(estado.ext || '')) this.el.classList.add('video-cuadros');
        this.escenario.prepend(this.el);
        this.el.volume = this.prefs.volumen;
        this.el.muted = this.prefs.mute;
        this.el.playbackRate = this.prefs.velocidad;
        this.el.loop = this.prefs.bucle;
        this.volumen.value = Math.round(this.prefs.volumen * 100);
        this.velocidad.value = String(this.prefs.velocidad);
        this.bucle.classList.toggle('activo', this.prefs.bucle);
        this.aspectoActual = 0;
        this.seekEditando = false;
        this.miniaturas = new MiniaturasMedia(estado, this.miniTooltip, this.miniTiempo);
        this.conectar();
        this.actualizarVolumen();
        if (!this.esAudio && !(document.pictureInPictureEnabled && this.el.requestPictureInPicture)) this.pip.hidden = true;
        if (estado.preparar) this.iniciarPreparacion();
        else { this.el.src = origen; this.el.load(); }
        window.__alternarMedia = () => this.alternar();
        window.__pararMedia = () => this.dispose();
    }

    leerPrefs() {
        try {
            const p = JSON.parse(localStorage.getItem('vistia.media') || '{}');
            return { volumen: Number.isFinite(p.volumen) ? p.volumen : 1, mute: !!p.mute, velocidad: p.velocidad || 1, bucle: !!p.bucle };
        } catch { return { volumen: 1, mute: false, velocidad: 1, bucle: false }; }
    }

    guardarPrefs() {
        try { localStorage.setItem('vistia.media', JSON.stringify(this.prefs)); } catch { }
    }

    conectar() {
        const senal = this.abortos.signal;
        this.el.addEventListener('loadedmetadata', () => {
            if (this.disposed) return;
            this.seek.disabled = false;
            this.seek.max = Number.isFinite(this.el.duration) ? this.el.duration : 100;
            this.miniaturas.duracion = this.el.duration;
            this.cargando.hidden = true;
            this.actualizarTiempo();
            if (!this.esAudio) this.el.play().catch(() => { });
        }, { signal: senal });
        this.el.addEventListener('timeupdate', () => this.actualizarTiempo(), { signal: senal });
        this.el.addEventListener('progress', () => this.actualizarBuffer(), { signal: senal });
        this.el.addEventListener('play', () => { this.actualizarBotones(); this.programarReloj(); }, { signal: senal });
        this.el.addEventListener('pause', () => this.actualizarBotones(), { signal: senal });
        this.el.addEventListener('ended', () => this.actualizarBotones(), { signal: senal });
        this.el.addEventListener('waiting', () => { this.cargando.hidden = false; }, { signal: senal });
        this.el.addEventListener('canplay', () => { this.cargando.hidden = true; }, { signal: senal });
        this.el.addEventListener('error', () => {
            if (this.disposed || this.preparando) return;
            if (this.usandoCache) this.mostrarError(new Error('La copia compatible no se pudo abrir'));
            else this.iniciarPreparacion();
        }, { signal: senal });
        this.el.addEventListener('enterpictureinpicture', () => this.informarPip(true), { signal: senal });
        this.el.addEventListener('leavepictureinpicture', () => this.informarPip(false), { signal: senal });

        this.playGrande.addEventListener('click', () => this.alternar(), { signal: senal });
        this.play.addEventListener('click', () => this.alternar(), { signal: senal });
        this.raiz.querySelector('[data-accion="menos"]').addEventListener('click', () => this.buscar(-10), { signal: senal });
        this.raiz.querySelector('[data-accion="mas"]').addEventListener('click', () => this.buscar(10), { signal: senal });
        this.raiz.querySelector('[data-accion="mute"]').addEventListener('click', () => this.alternarMute(), { signal: senal });
        this.raiz.querySelector('[data-accion="bucle"]').addEventListener('click', () => this.alternarBucle(), { signal: senal });
        this.raiz.querySelector('[data-accion="aspecto"]').addEventListener('click', () => this.cambiarAspecto(), { signal: senal });
        this.raiz.querySelector('[data-accion="pantalla"]').addEventListener('click', () => this.alternarPantalla(), { signal: senal });
        this.pip.addEventListener('click', () => this.alternarPip(), { signal: senal });
        this.raiz.querySelector('[data-accion="reintentar"]').addEventListener('click', () => this.iniciarPreparacion(), { signal: senal });
        this.raiz.querySelector('[data-accion="forzar"]').addEventListener('click', () => this.iniciarPreparacion(true), { signal: senal });
        this.raiz.querySelector('[data-accion="abrir"]').addEventListener('click', () => this.mensaje({ cmd: 'abrir' }), { signal: senal });

        this.seek.addEventListener('input', () => {
            if (!Number.isFinite(this.el.duration)) return;
            this.seekEditando = true;
            this.tiempo.textContent = this.formato(Number(this.seek.value)) + ' / ' + this.formato(this.el.duration);
            this.actualizarProgresoVisual();
        }, { signal: senal });
        this.seek.addEventListener('change', () => {
            if (!Number.isFinite(this.el.duration)) return;
            this.el.currentTime = Number(this.seek.value);
            this.seekEditando = false;
            this.actualizarTiempo();
        }, { signal: senal });
        const soltarSeek = () => {
            if (!this.seekEditando) return;
            this.seekEditando = false;
            this.actualizarTiempo();
        };
        window.addEventListener('pointerup', () => setTimeout(soltarSeek, 0), { signal: senal });
        window.addEventListener('pointercancel', soltarSeek, { signal: senal });
        this.seek.addEventListener('pointermove', ev => {
            if (!Number.isFinite(this.el.duration)) return;
            const r = this.seek.getBoundingClientRect();
            const proporcion = Math.min(1, Math.max(0, (ev.clientX - r.left) / r.width));
            this.miniaturas.preparar();
            this.miniaturas.mostrar(proporcion);
            this.miniTooltip.style.left = Math.min(96, Math.max(4, proporcion * 100)) + '%';
        }, { signal: senal });
        this.seek.addEventListener('pointerleave', () => this.miniaturas.ocultar(), { signal: senal });
        this.seek.addEventListener('focus', () => this.miniaturas.preparar(), { signal: senal });

        this.volumen.addEventListener('input', () => {
            this.el.volume = Number(this.volumen.value) / 100;
            this.el.muted = this.el.volume === 0;
            this.prefs.volumen = this.el.volume;
            this.prefs.mute = this.el.muted;
            this.guardarPrefs();
            this.actualizarVolumen();
        }, { signal: senal });
        this.velocidad.addEventListener('change', () => {
            this.el.playbackRate = Number(this.velocidad.value);
            this.prefs.velocidad = this.el.playbackRate;
            this.guardarPrefs();
        }, { signal: senal });
        this.raiz.addEventListener('keydown', ev => this.teclado(ev), { signal: senal });
        this.raiz.addEventListener('pointerdown', () => this.raiz.focus(), { signal: senal });
    }

    teclado(ev) {
        if (ev.target !== this.raiz && ev.target.matches('input,select,button')) return;
        const k = ev.key.toLowerCase();
        if ((k === ' ' || k === 'k') && !ev.repeat) { ev.preventDefault(); this.alternar(); return; }
        if (k === 'arrowleft') { ev.preventDefault(); this.buscar(-5); }
        else if (k === 'arrowright') { ev.preventDefault(); this.buscar(5); }
        else if (k === 'j') { ev.preventDefault(); this.buscar(-10); }
        else if (k === 'l') { ev.preventDefault(); this.buscar(10); }
        else if (k === 'arrowup') { ev.preventDefault(); this.cambiarVolumen(0.05); }
        else if (k === 'arrowdown') { ev.preventDefault(); this.cambiarVolumen(-0.05); }
        else if (k === 'm') { ev.preventDefault(); this.alternarMute(); }
        else if (k === 'f') { ev.preventDefault(); this.alternarPantalla(); }
        else if (k === 'p') { ev.preventDefault(); this.alternarPip(); }
        else if (k === 'home') { ev.preventDefault(); this.el.currentTime = 0; }
        else if (k === 'end' && Number.isFinite(this.el.duration)) { ev.preventDefault(); this.el.currentTime = this.el.duration; }
        else if (k === 'escape' && this.pantallaCompleta) { ev.preventDefault(); this.alternarPantalla(); }
        else if (/^[0-9]$/.test(k) && Number.isFinite(this.el.duration)) { ev.preventDefault(); this.el.currentTime = this.el.duration * Number(k) / 10; }
    }

    alternar() {
        if (this.disposed || this.preparando || !this.errorPanel.hidden || !this.el.currentSrc) return;
        if (this.el.paused) this.el.play().catch(() => { }); else this.el.pause();
    }

    buscar(delta) {
        if (!Number.isFinite(this.el.duration)) return;
        this.el.currentTime = Math.min(this.el.duration, Math.max(0, this.el.currentTime + delta));
    }

    cambiarVolumen(delta) {
        this.el.volume = Math.min(1, Math.max(0, this.el.volume + delta));
        this.el.muted = false;
        this.prefs.volumen = this.el.volume; this.prefs.mute = false;
        this.volumen.value = Math.round(this.el.volume * 100);
        this.guardarPrefs(); this.actualizarVolumen();
    }

    alternarMute() {
        this.el.muted = !this.el.muted;
        this.prefs.mute = this.el.muted;
        this.guardarPrefs(); this.actualizarVolumen();
    }

    actualizarVolumen() {
        const b = this.raiz.querySelector('[data-accion="mute"]');
        b.textContent = this.el.muted ? 'Con sonido' : 'Silencio';
        b.classList.toggle('activo', this.el.muted);
    }

    alternarBucle() {
        this.el.loop = !this.el.loop;
        this.prefs.bucle = this.el.loop;
        this.bucle.classList.toggle('activo', this.el.loop);
        this.guardarPrefs();
    }

    cambiarAspecto() {
        if (this.esAudio) return;
        this.aspectoActual = (this.aspectoActual + 1) % 3;
        const modos = ['contain', 'cover', 'none'];
        const textos = ['Ajustar', 'Rellenar', 'Tamaño real'];
        this.el.style.objectFit = modos[this.aspectoActual];
        this.aspecto.textContent = textos[this.aspectoActual];
    }

    alternarPantalla() {
        this.pantallaCompleta = !this.pantallaCompleta;
        this.mensaje({ cmd: 'pantalla-completa' });
    }

    async alternarPip() {
        if (this.esAudio || this.pip.hidden) return;
        try {
            if (document.pictureInPictureElement) await document.exitPictureInPicture();
            else await this.el.requestPictureInPicture();
        } catch { this.pip.textContent = 'PiP no disponible'; }
    }

    informarPip(valor) { this.mensaje({ cmd: 'pip-estado', valor }); }

    actualizarBotones() {
        const pausado = this.el.paused;
        this.play.textContent = pausado ? 'Reproducir' : 'Pausa';
        this.playGrande.hidden = !pausado || this.esAudio;
        this.playGrande.textContent = 'Reproducir';
        this.raiz.classList.toggle('reproduciendo', !pausado);
    }

    programarReloj() {
        if (this.reloj) return;
        const paso = () => {
            this.reloj = null;
            if (this.disposed || this.el.paused) return;
            this.actualizarTiempo();
            this.reloj = requestAnimationFrame(paso);
        };
        this.reloj = requestAnimationFrame(paso);
    }

    actualizarTiempo() {
        if (this.disposed) return;
        const actual = Number.isFinite(this.el.currentTime) ? this.el.currentTime : 0;
        const total = Number.isFinite(this.el.duration) ? this.el.duration : 0;
        if (!this.seekEditando && total > 0) this.seek.value = actual;
        this.tiempo.textContent = this.formato(actual) + ' / ' + this.formato(total);
        this.actualizarProgresoVisual();
        this.actualizarBuffer();
        this.actualizarBotones();
    }

    actualizarProgresoVisual() {
        const total = Number.isFinite(this.el.duration) && this.el.duration > 0 ? this.el.duration : 100;
        const actual = Number(this.seek.value) || 0;
        this.raiz.style.setProperty('--jugado', Math.min(100, actual / total * 100) + '%');
    }

    actualizarBuffer() {
        let cargado = 0;
        try { if (this.el.buffered.length && this.el.duration > 0) cargado = this.el.buffered.end(this.el.buffered.length - 1) / this.el.duration * 100; } catch { }
        this.raiz.style.setProperty('--cargado', Math.min(100, cargado) + '%');
    }

    formato(segundos) {
        if (!Number.isFinite(segundos)) segundos = 0;
        const total = Math.max(0, Math.floor(segundos));
        const h = Math.floor(total / 3600), m = Math.floor(total % 3600 / 60), s = total % 60;
        return h ? h + ':' + String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0') : m + ':' + String(s).padStart(2, '0');
    }

    iniciarPreparacion(forzar = false) {
        if (this.disposed) return;
        if (this.preparacion) this.preparacion.cancelar();
        this.preparando = true;
        this.usandoCache = false;
        this.errorPanel.hidden = true;
        this.playGrande.hidden = true;
        this.cargando.hidden = true;
        this.preparacionPanel.hidden = false;
        this.preparacionTexto.textContent = 'Leyendo el formato…';
        this.preparacionBarra.value = 0;
        // Ningún error o fotograma del origen anterior debe mezclarse con la copia.
        try { this.el.pause(); this.el.removeAttribute('src'); this.el.load(); } catch { }
        this.preparacion = new PreparacionMedia(this.estado, {
            lista: url => {
                if (this.disposed) return;
                this.preparando = false;
                this.usandoCache = true;
                this.errorPanel.hidden = true;
                this.preparacionPanel.hidden = true;
                this.cargando.hidden = false;
                this.el.src = url;
                this.el.load();
                if (!this.esAudio) this.el.play().catch(() => { });
            },
            progreso: datos => {
                if (this.disposed) return;
                this.preparacionTexto.textContent = datos.mensaje + (datos.progreso > 0 ? ' · ' + Math.round(datos.progreso * 100) + '%' : '');
                this.preparacionBarra.value = datos.progreso || 0;
            },
            error: error => this.mostrarError(error)
        });
        this.preparacion.iniciar(forzar);
    }

    mostrarError(error) {
        if (this.disposed) return;
        this.preparando = false;
        this.cargando.hidden = true;
        this.playGrande.hidden = true;
        this.preparacionPanel.hidden = true;
        this.errorPanel.hidden = false;
        this.errorTexto.textContent = error && error.message ? error.message : String(error || 'Formato no reconocido');
    }

    mensaje(dato) {
        try { window.chrome?.webview?.postMessage(dato); } catch { }
    }

    dispose() {
        if (this.disposed) return;
        this.disposed = true;
        if (this.reloj) cancelAnimationFrame(this.reloj);
        if (this.preparacion) this.preparacion.cancelar();
        this.abortos.abort();
        this.miniaturas.dispose();
        try {
            if (document.pictureInPictureElement === this.el) document.exitPictureInPicture();
        } catch { }
        try { this.el.pause(); this.el.removeAttribute('src'); this.el.load(); } catch { }
        this.raiz.remove();
    }
}

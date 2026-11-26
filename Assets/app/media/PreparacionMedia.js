export class PreparacionMedia {
    constructor(estado, eventos) {
        this.estado = estado;
        this.eventos = eventos;
        this.cancelada = false;
        this.controlador = new AbortController();
    }

    async iniciar(forzar = false) {
        this.cancelada = false;
        this.eventos.progreso({ mensaje: forzar ? 'Forzando copia compatible…' : 'Leyendo el formato…', progreso: 0 });
        try {
            const ruta = forzar ? '/media-forzar?id=' : '/media-preparar?id=';
            const respuesta = await fetch(ruta + encodeURIComponent(this.estado.mediaId || ''), { signal: this.controlador.signal, cache: 'no-store' });
            const datos = await respuesta.json();
            if (datos.estado === 'error') throw new Error(datos.mensaje || 'No se pudo iniciar');
            if (!respuesta.ok) throw new Error('No se pudo iniciar la preparación');
            if (datos.estado === 'lista') { if (!this.cancelada) this.eventos.lista(datos.url); return; }
            while (!this.cancelada) {
                const actual = await (await fetch('/media-estado?id=' + encodeURIComponent(this.estado.mediaId || ''), { signal: this.controlador.signal, cache: 'no-store' })).json();
                if (this.cancelada) return;
                if (actual.estado === 'lista') { this.eventos.lista(actual.url); return; }
                if (actual.estado === 'error') throw new Error(actual.mensaje || 'La preparación falló');
                this.eventos.progreso(actual);
                await new Promise(resolve => setTimeout(resolve, 700));
            }
        } catch (error) {
            if (!this.cancelada && error.name !== 'AbortError') this.eventos.error(error);
        }
    }

    cancelar() {
        this.cancelada = true;
        this.controlador.abort();
    }
}

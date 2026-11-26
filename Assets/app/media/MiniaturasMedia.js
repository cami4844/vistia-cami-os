export class MiniaturasMedia {
    constructor(estado, contenedor, tiempo) {
        this.estado = estado;
        this.contenedor = contenedor;
        this.tiempo = tiempo;
        this.datos = null;
        this.iniciado = false;
        this.cancelado = false;
    }

    async preparar() {
        if (this.iniciado || this.cancelado) return;
        this.iniciado = true;
        try {
            while (!this.cancelado && !this.datos) {
                const datos = await (await fetch('/miniaturas-estado?id=' + encodeURIComponent(this.estado.mediaId || ''), { cache: 'no-store' })).json();
                if (datos.estado === 'lista') { this.datos = datos; break; }
                if (datos.estado === 'error') return;
                await new Promise(resolve => setTimeout(resolve, 800));
            }
        } catch { }
    }

    mostrar(proporcion) {
        const segundos = Math.max(0, proporcion) * this.duracion;
        this.tiempo.textContent = this.formatear(segundos);
        this.contenedor.hidden = false;
        if (!this.datos) return;
        const d = this.datos;
        const indice = Math.min(d.total - 1, Math.max(0, Math.floor(segundos / d.intervalo)));
        const columna = indice % d.columnas;
        const fila = Math.floor(indice / d.columnas);
        this.contenedor.style.backgroundImage = 'url("' + d.url + '")';
        this.contenedor.style.backgroundSize = (d.columnas * 100) + '% ' + (d.filas * 100) + '%';
        this.contenedor.style.backgroundPosition = (columna / Math.max(1, d.columnas - 1) * 100) + '% ' + (fila / Math.max(1, d.filas - 1) * 100) + '%';
    }

    formatear(segundos) {
        const total = Math.max(0, Math.floor(segundos));
        const h = Math.floor(total / 3600), m = Math.floor(total % 3600 / 60), s = total % 60;
        return h ? h + ':' + String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0') : m + ':' + String(s).padStart(2, '0');
    }

    ocultar() { this.contenedor.hidden = true; }
    dispose() { this.cancelado = true; this.contenedor.remove(); }
}

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

export async function initSkin(estado, pantalla, esActual = () => true) {
    if (window.__parar3d) window.__parar3d();
    pantalla.innerHTML = '';
    const raiz = document.createElement('section');
    raiz.className = 'skin-visor';
    raiz.innerHTML = `
        <div class="skin-controles">
            <button type="button" data-a="plano">Plano</button>
            <button type="button" data-a="caminar" class="activo">Caminar</button>
            <button type="button" data-a="girar" class="activo">Girar</button>
            <button type="button" data-a="capas" class="activo">Segunda capa</button>
            <button type="button" data-a="modelo">Slim</button>
        </div>
        <div class="skin-lienzo"><span class="skin-estado">Cargando skin…</span></div>`;
    pantalla.appendChild(raiz);
    const lienzo = raiz.querySelector('.skin-lienzo');
    const textoEstado = raiz.querySelector('.skin-estado');
    const url = estado.src || origenArchivo(estado);
    const imagen = new Image();
    imagen.src = url;
    await new Promise((resolve, reject) => {
        imagen.onload = resolve;
        imagen.onerror = () => reject(new Error('No se pudo leer la imagen de la skin'));
    });
    if (!esActual()) { raiz.remove(); return; }
    const plano = document.createElement('img');
    plano.src = url;
    plano.alt = estado.nombre || 'Skin Minecraft';
    plano.hidden = true;
    lienzo.appendChild(plano);
    const entorno = {
        caminar: true, girar: estado.autogiro !== false, capas: true, slim: false,
        renderer: null, escena: null, camara: null, controles: null, modelo: null,
        abortos: new AbortController(), reloj: 0, detenido: false
    };
    function crear3D() {
        if (entorno.renderer) return;
        plano.hidden = true;
        textoEstado.textContent = 'Arrastra para orbitar · rueda para acercar';
        entorno.escena = new THREE.Scene();
        entorno.escena.background = new THREE.Color(estado.tema === 'oscuro' ? 0x1f1a16 : 0xf7f1e7);
        entorno.camara = new THREE.PerspectiveCamera(40, 1, 0.01, 100);
        entorno.camara.position.set(3.4, 2.6, 5.4);
        entorno.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
        entorno.renderer.setPixelRatio(Math.min(devicePixelRatio || 1, 2));
        entorno.renderer.domElement.style.width = '100%';
        entorno.renderer.domElement.style.height = '100%';
        lienzo.appendChild(entorno.renderer.domElement);
        const hemi = new THREE.HemisphereLight(0xfff4e0, 0x4b4036, 1.8);
        const sol = new THREE.DirectionalLight(0xffffff, 2.2);
        sol.position.set(3, 6, 5);
        entorno.escena.add(hemi, sol);
        entorno.controles = new OrbitControls(entorno.camara, entorno.renderer.domElement);
        entorno.controles.target.set(0, 1.35, 0);
        entorno.controles.enableDamping = true;
        entorno.controles.autoRotate = entorno.girar;
        entorno.controles.autoRotateSpeed = 1.1;
        entorno.controles.minDistance = 2.6;
        entorno.controles.maxDistance = 10;
        entorno.controles.addEventListener('start', () => {
            entorno.girar = false;
            entorno.controles.autoRotate = false;
            raiz.querySelector('[data-a="girar"]').classList.remove('activo');
        });
        entorno.modelo = construirPersonaje(imagen, entorno.slim);
        entorno.escena.add(entorno.modelo.grupo);
        entorno.modelo.grupo.traverse(objeto => { if (objeto.userData.overlay) objeto.visible = entorno.capas; });
        const redimensionar = () => {
            const ancho = lienzo.clientWidth || 800, alto = lienzo.clientHeight || 600;
            entorno.renderer.setSize(ancho, alto, false);
            entorno.camara.aspect = ancho / alto;
            entorno.camara.updateProjectionMatrix();
        };
        new ResizeObserver(redimensionar).observe(lienzo);
        redimensionar();
        const reloj = new THREE.Clock();
        const animar = () => {
            if (entorno.detenido) return;
            const t = reloj.getElapsedTime();
            animarPersonaje(entorno.modelo, t, entorno.caminar);
            entorno.controles.update();
            entorno.renderer.render(entorno.escena, entorno.camara);
            entorno.reloj = requestAnimationFrame(animar);
        };
        animar();
    }
    function alternarPlano() {
        if (!entorno.renderer) {
            plano.hidden = !plano.hidden;
            textoEstado.textContent = plano.hidden ? '' : 'Vista plana de la textura';
            return;
        }
        const mostrarPlano = entorno.renderer.domElement.style.display !== 'none';
        entorno.renderer.domElement.style.display = mostrarPlano ? 'none' : 'block';
        plano.hidden = !mostrarPlano;
    }
    raiz.querySelector('[data-a="plano"]').addEventListener('click', alternarPlano, { signal: entorno.abortos.signal });
    raiz.querySelector('[data-a="caminar"]').addEventListener('click', ev => {
        entorno.caminar = !entorno.caminar;
        ev.currentTarget.classList.toggle('activo', entorno.caminar);
    }, { signal: entorno.abortos.signal });
    raiz.querySelector('[data-a="girar"]').addEventListener('click', ev => {
        entorno.girar = !entorno.girar;
        if (entorno.controles) entorno.controles.autoRotate = entorno.girar;
        ev.currentTarget.classList.toggle('activo', entorno.girar);
    }, { signal: entorno.abortos.signal });
    raiz.querySelector('[data-a="capas"]').addEventListener('click', ev => {
        entorno.capas = !entorno.capas;
        if (entorno.modelo) entorno.modelo.grupo.traverse(objeto => { if (objeto.userData.overlay) objeto.visible = entorno.capas; });
        ev.currentTarget.classList.toggle('activo', entorno.capas);
    }, { signal: entorno.abortos.signal });
    raiz.querySelector('[data-a="modelo"]').addEventListener('click', ev => {
        entorno.slim = !entorno.slim;
        ev.currentTarget.textContent = entorno.slim ? 'Classic' : 'Slim';
        if (entorno.escena && entorno.modelo) {
            entorno.escena.remove(entorno.modelo.grupo);
            entorno.modelo = construirPersonaje(imagen, entorno.slim);
            entorno.escena.add(entorno.modelo.grupo);
            entorno.modelo.grupo.traverse(objeto => { if (objeto.userData.overlay) objeto.visible = entorno.capas; });
        }
    }, { signal: entorno.abortos.signal });
    crear3D();
    window.__parar3d = () => {
        entorno.detenido = true;
        cancelAnimationFrame(entorno.reloj);
        entorno.abortos.abort();
        if (entorno.controles) entorno.controles.dispose();
        if (entorno.renderer) {
            entorno.renderer.dispose();
            entorno.renderer.domElement.remove();
        }
        raiz.remove();
    };
}

function origenArchivo(estado) {
    if (estado.zipi != null) return '/zipfile/' + estado.zipi + '/' + encodeURIComponent(estado.ext || 'png');
    return '/file/' + encodeURIComponent(estado.ext || 'png');
}

function construirPersonaje(imagen, slim) {
    const textura = new THREE.CanvasTexture(imagen);
    textura.magFilter = THREE.NearestFilter;
    textura.minFilter = THREE.NearestFilter;
    textura.colorSpace = THREE.SRGBColorSpace;
    const grupo = new THREE.Group();
    const partes = { brazos: [], piernas: [], cabeza: null, cuerpo: null };
    const moderno = imagen.height >= 64;
    const brazo = slim ? 0.3 : 0.4;

    const cabeza = new THREE.Group();
    cabeza.position.set(0, 2.4, 0);
    cabeza.add(parte(0.8, 0.8, 0.8, 0, 0, 0, textura, imagen, {
        derecha: [0, 8, 8, 8], izquierda: [16, 8, 8, 8], arriba: [8, 0, 8, 8], abajo: [16, 0, 8, 8], frente: [8, 8, 8, 8], atras: [24, 8, 8, 8]
    }));
    if (moderno) {
        const gorra = parte(0.86, 0.86, 0.86, 0, 0, 0, textura, imagen, {
            derecha: [32, 8, 8, 8], izquierda: [48, 8, 8, 8], arriba: [40, 0, 8, 8], abajo: [48, 0, 8, 8], frente: [40, 8, 8, 8], atras: [56, 8, 8, 8]
        });
        gorra.userData.overlay = true;
        cabeza.add(gorra);
    }
    grupo.add(cabeza);
    partes.cabeza = cabeza;

    const cuerpo = new THREE.Group();
    cuerpo.position.set(0, 1.4, 0);
    cuerpo.add(parte(0.8, 1.2, 0.4, 0, 0, 0, textura, imagen, {
        derecha: [16, 20, 4, 12], izquierda: [28, 20, 4, 12], arriba: [20, 16, 8, 4], abajo: [28, 16, 8, 4], frente: [20, 20, 8, 12], atras: [32, 20, 8, 12]
    }));
    if (moderno) {
        const capa = parte(0.84, 1.24, 0.44, 0, 0, 0, textura, imagen, {
            derecha: [16, 36, 4, 12], izquierda: [28, 36, 4, 12], arriba: [20, 32, 8, 4], abajo: [28, 32, 8, 4], frente: [20, 36, 8, 12], atras: [32, 36, 8, 12]
        });
        capa.userData.overlay = true;
        cuerpo.add(capa);
    }
    grupo.add(cuerpo);
    partes.cuerpo = cuerpo;

    function crearBrazo(lado) {
        const g = new THREE.Group();
        g.position.set(lado * (0.4 + brazo / 2), 1.4, 0);
        const uvDer = lado < 0 ? {
            derecha: [40, 20, 4, 12], izquierda: [48, 20, 4, 12], arriba: [44, 16, 4, 4], abajo: [48, 16, 4, 4], frente: [44, 20, 4, 12], atras: [52, 20, 4, 12]
        } : moderno ? {
            derecha: [36, 52, 4, 12], izquierda: [44, 52, 4, 12], arriba: [40, 48, 4, 4], abajo: [44, 48, 4, 4], frente: [40, 52, 4, 12], atras: [48, 52, 4, 12]
        } : {
            derecha: [40, 20, 4, 12], izquierda: [48, 20, 4, 12], arriba: [44, 16, 4, 4], abajo: [48, 16, 4, 4], frente: [44, 20, 4, 12], atras: [52, 20, 4, 12]
        };
        g.add(parte(brazo, 1.2, 0.4, 0, 0, 0, textura, imagen, uvDer));
        if (moderno) {
            const uvCapa = lado < 0 ? {
                derecha: [40, 36, 4, 12], izquierda: [48, 36, 4, 12], arriba: [44, 32, 4, 4], abajo: [48, 32, 4, 4], frente: [44, 36, 4, 12], atras: [52, 36, 4, 12]
            } : {
                derecha: [52, 52, 4, 12], izquierda: [60, 52, 4, 12], arriba: [56, 48, 4, 4], abajo: [60, 48, 4, 4], frente: [56, 52, 4, 12], atras: [64, 52, 4, 12]
            };
            const capa = parte(brazo + 0.05, 1.24, 0.45, 0, 0, 0, textura, imagen, uvCapa);
            capa.userData.overlay = true;
            g.add(capa);
        }
        grupo.add(g);
        partes.brazos.push({ grupo: g, lado });
    }
    crearBrazo(-1); crearBrazo(1);

    function crearPierna(lado) {
        const g = new THREE.Group();
        g.position.set(lado * 0.2, 0.4, 0);
        const uv = lado < 0 ? {
            derecha: [0, 20, 4, 12], izquierda: [8, 20, 4, 12], arriba: [4, 16, 4, 4], abajo: [8, 16, 4, 4], frente: [4, 20, 4, 12], atras: [12, 20, 4, 12]
        } : moderno ? {
            derecha: [20, 52, 4, 12], izquierda: [28, 52, 4, 12], arriba: [24, 48, 4, 4], abajo: [28, 48, 4, 4], frente: [24, 52, 4, 12], atras: [32, 52, 4, 12]
        } : {
            derecha: [0, 20, 4, 12], izquierda: [8, 20, 4, 12], arriba: [4, 16, 4, 4], abajo: [8, 16, 4, 4], frente: [4, 20, 4, 12], atras: [12, 20, 4, 12]
        };
        g.add(parte(0.4, 1.2, 0.4, 0, 0, 0, textura, imagen, uv));
        if (moderno) {
            const uvCapa = lado < 0 ? {
                derecha: [0, 36, 4, 12], izquierda: [8, 36, 4, 12], arriba: [4, 32, 4, 4], abajo: [8, 32, 4, 4], frente: [4, 36, 4, 12], atras: [12, 36, 4, 12]
            } : {
                derecha: [4, 52, 4, 12], izquierda: [12, 52, 4, 12], arriba: [8, 48, 4, 4], abajo: [12, 48, 4, 4], frente: [8, 52, 4, 12], atras: [16, 52, 4, 12]
            };
            const capa = parte(0.45, 1.24, 0.45, 0, 0, 0, textura, imagen, uvCapa);
            capa.userData.overlay = true;
            g.add(capa);
        }
        grupo.add(g);
        partes.piernas.push({ grupo: g, lado });
    }
    crearPierna(-1); crearPierna(1);
    return { grupo, partes };
}

function parte(ancho, alto, profundidad, x, y, z, textura, imagen, mapa) {
    const geometria = new THREE.BoxGeometry(ancho, alto, profundidad);
    const caras = ['derecha', 'izquierda', 'arriba', 'abajo', 'frente', 'atras'];
    caras.forEach((cara, i) => ajustarUV(geometria, i, mapa[cara], imagen.width, imagen.height));
    const materiales = caras.map(() => new THREE.MeshStandardMaterial({
        map: textura, roughness: 1, metalness: 0, transparent: true, alphaTest: 0.02, side: THREE.DoubleSide
    }));
    const malla = new THREE.Mesh(geometria, materiales);
    malla.position.set(x, y, z);
    return malla;
}

function ajustarUV(geometria, cara, rect, ancho, alto) {
    const [x, y, w, h] = rect;
    const u0 = x / ancho, u1 = (x + w) / ancho;
    const v0 = 1 - (y + h) / alto, v1 = 1 - y / alto;
    const uv = geometria.attributes.uv;
    const base = cara * 4;
    uv.setXY(base, u0, v1);
    uv.setXY(base + 1, u1, v1);
    uv.setXY(base + 2, u0, v0);
    uv.setXY(base + 3, u1, v0);
    uv.needsUpdate = true;
}

function animarPersonaje(modelo, t, caminar) {
    const p = modelo.partes;
    if (caminar) {
        const paso = Math.sin(t * 6) * 0.65;
        p.piernas.forEach(pierna => { pierna.grupo.rotation.x = paso * pierna.lado; });
        p.brazos.forEach(brazo => { brazo.grupo.rotation.x = -paso * brazo.lado * 0.8; });
        p.cuerpo.position.y = 1.4 + Math.abs(Math.sin(t * 6)) * 0.035;
    } else {
        p.piernas.forEach(pierna => { pierna.grupo.rotation.x = 0; });
        p.brazos.forEach(brazo => { brazo.grupo.rotation.x = 0; });
        p.cuerpo.position.y = 1.4;
    }
    p.cabeza.rotation.y = Math.sin(t * 0.8) * 0.08;
}

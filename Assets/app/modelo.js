// Tintero Vista - visor 3D con three.js (glb gltf obj stl ply fbx 3ds dae 3mf)
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { DRACOLoader } from 'three/addons/loaders/DRACOLoader.js';
import { OBJLoader } from 'three/addons/loaders/OBJLoader.js';
import { STLLoader } from 'three/addons/loaders/STLLoader.js';
import { PLYLoader } from 'three/addons/loaders/PLYLoader.js';
import { FBXLoader } from 'three/addons/loaders/FBXLoader.js';
import { ColladaLoader } from 'three/addons/loaders/ColladaLoader.js';
import { TDSLoader } from 'three/addons/loaders/TDSLoader.js';
import { ThreeMFLoader } from 'three/addons/loaders/3MFLoader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';

function escapar(s) {
    return String(s).replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

export async function initModelo(estado, contenedor, esActual = () => true) {
    if (!esActual()) return;
    const oscuro = estado.tema === 'oscuro';
    const fondo = oscuro ? 0x1F1A16 : 0xF7F1E7;

    const escena = new THREE.Scene();
    escena.background = new THREE.Color(fondo);

    const camara = new THREE.PerspectiveCamera(50, 1, 0.01, 5000);
    const renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    contenedor.appendChild(renderer.domElement);

    // luces suaves
    escena.add(new THREE.HemisphereLight(0xfff4e0, 0x5a4a3a, oscuro ? 1.0 : 1.25));
    const sol = new THREE.DirectionalLight(0xffffff, oscuro ? 1.7 : 2.1);
    sol.position.set(3, 6, 4);
    escena.add(sol);
    const relleno = new THREE.DirectionalLight(0xffe8d0, 0.75);
    relleno.position.set(-4, 2, -3);
    escena.add(relleno);

    const progreso = document.createElement('div');
    progreso.className = 'modelo-progreso';
    progreso.textContent = 'Cargando...';
    contenedor.appendChild(progreso);

    const orbita = new OrbitControls(camara, renderer.domElement);
    orbita.enableDamping = true;
    orbita.dampingFactor = 0.08;
    orbita.autoRotate = !!estado.autogiro;
    orbita.autoRotateSpeed = 1.1;
    orbita.addEventListener('start', () => { orbita.autoRotate = false; });

    const url = '/file/' + encodeURIComponent(estado.ext || '');
    const ext = (estado.ext || '').toLowerCase();
    const alProgresar = ev => { if (ev.total) progreso.textContent = 'Cargando ' + Math.round(ev.loaded / ev.total * 100) + '%'; };

    let objeto = null;
    try {
        if (ext === 'glb' || ext === 'gltf' || estado.glb) {
            const loader = new GLTFLoader();
            const draco = new DRACOLoader();
            draco.setDecoderPath('/js/three/examples/jsm/libs/draco/gltf/');
            loader.setDRACOLoader(draco);
            loader.setMeshoptDecoder(MeshoptDecoder);
            const gltf = await loader.loadAsync(url, alProgresar);
            if (!esActual()) { renderer.dispose(); return; }
            objeto = gltf.scene;
            if (gltf.animations && gltf.animations.length) {
                const mezclador = new THREE.AnimationMixer(objeto);
                mezclador.clipAction(gltf.animations[0]).play();
                window.__anim3d = mezclador;
            }
        } else if (ext === 'obj') {
            objeto = await new OBJLoader().loadAsync(url, alProgresar);
            if (!esActual()) { renderer.dispose(); return; }
        } else if (ext === 'fbx') {
            objeto = await new FBXLoader().loadAsync(url, alProgresar);
            if (!esActual()) { renderer.dispose(); return; }
        } else if (ext === '3ds') {
            objeto = await new TDSLoader().loadAsync(url, alProgresar);
            if (!esActual()) { renderer.dispose(); return; }
        } else if (ext === 'dae') {
            const dae = await new ColladaLoader().loadAsync(url, alProgresar);
            if (!esActual()) { renderer.dispose(); return; }
            objeto = dae.scene;
        } else if (ext === '3mf') {
            objeto = await new ThreeMFLoader().loadAsync(url, alProgresar);
            if (!esActual()) { renderer.dispose(); return; }
        } else if (ext === 'stl' || ext === 'ply') {
            const geo = ext === 'stl'
                ? await new STLLoader().loadAsync(url, alProgresar)
                : await new PLYLoader().loadAsync(url, alProgresar);
            if (!esActual()) { renderer.dispose(); return; }
            geo.computeVertexNormals();
            const mat = new THREE.MeshStandardMaterial({
                color: 0xC9B8A3, roughness: 0.55, metalness: 0.05,
                vertexColors: !!geo.attributes.color, side: THREE.DoubleSide
            });
            objeto = new THREE.Mesh(geo, mat);
        } else {
            throw new Error('formato sin visor: ' + ext);
        }
    } catch (err) {
        progreso.remove();
        const e = document.createElement('div');
        e.className = 'modelo-error';
        e.innerHTML = '<b>No se pudo cargar el modelo.</b><br>' + escapar((err && err.message) || err) +
            '<br><br><span style="color:var(--suave);font-size:12px">Si es .gltf con texturas externas, descargalo como .glb</span>';
        contenedor.appendChild(e);
        avisarListo2();
        return;
    }
    progreso.remove();

    // enmarcar: centrar y ajustar cámara
    const caja = new THREE.Box3().setFromObject(objeto);
    const centro = caja.getCenter(new THREE.Vector3());
    const tam = caja.getSize(new THREE.Vector3());
    const radio = Math.max(tam.x, tam.y, tam.z, 0.001);
    objeto.position.sub(centro);
    escena.add(objeto);

    camara.position.set(radio * 1.15, radio * 0.75, radio * 1.45);
    camara.near = radio / 100;
    camara.far = radio * 60;
    camara.updateProjectionMatrix();
    orbita.target.set(0, 0, 0);
    orbita.update();

    // rejilla sutil al piso
    const rejilla = new THREE.GridHelper(radio * 3.2, 12,
        oscuro ? 0x4A3F36 : 0xD9CCBB, oscuro ? 0x352D27 : 0xE9DFD0);
    rejilla.position.y = caja.min.y - centro.y;
    escena.add(rejilla);

    // tamaño y bucle
    const ajustar = () => {
        const w = contenedor.clientWidth || 800, h = contenedor.clientHeight || 600;
        renderer.setSize(w, h, false);
        camara.aspect = w / h;
        camara.updateProjectionMatrix();
    };
    new ResizeObserver(ajustar).observe(contenedor);
    ajustar();

    let vivo = true;
    renderer.setAnimationLoop(() => {
        if (!vivo) return;
        if (window.__anim3d) window.__anim3d.update(0.016);
        orbita.update();
        renderer.render(escena, camara);
    });
    window.__parar3d = () => { vivo = false; renderer.setAnimationLoop(null); };

    renderer.domElement.addEventListener('dblclick', () => {
        camara.position.set(radio * 1.15, radio * 0.75, radio * 1.45);
        orbita.target.set(0, 0, 0);
        orbita.update();
    });

    const pista = document.createElement('div');
    pista.className = 'modelo-pista';
    pista.textContent = 'Arrastra para girar - rueda para acercar - doble clic reinicia';
    contenedor.appendChild(pista);

    avisarListo2();
}

function avisarListo2() {
    try { window.chrome?.webview?.postMessage({ cmd: 'listo' }); } catch (e) { }
}

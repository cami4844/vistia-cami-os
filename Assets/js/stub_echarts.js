// Stub ESM de echarts: pptx-preview lo importa para graficos embebidos.
// Sin charts: la diapositiva se muestra sin el grafico (mejor esfuerzo declarado).
const nada = { init: () => ({ setOption: () => {}, resize: () => {}, dispose: () => {} }) };
export default nada;
export const init = nada.init;

// Stub ESM de uuid: solo se usa v4. Sin aleatoriedad debil: randomUUID o contador.
export const v4 = crypto.randomUUID
    ? () => crypto.randomUUID()
    : (() => { let n = 0; return () => 'id-' + Date.now().toString(36) + '-' + (++n); })();
export default { v4 };

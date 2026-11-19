/**
 * O PNG do canvas não diz a resolução (o navegador grava 72 DPI ou nada). A gráfica lê o tamanho em centímetros
 * pelos pixels por polegada, então o arquivo de impressão ganha o bloco pHYs com 300 DPI, logo depois do IHDR.
 */

const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c >>> 0;
  }
  return table;
})();

export function crc32(bytes: Uint8Array): number {
  let c = 0xffffffff;
  for (const b of bytes) c = CRC_TABLE[(c ^ b) & 0xff]! ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

const PNG_SIGNATURE = [137, 80, 78, 71, 13, 10, 26, 10];
// signature (8) + IHDR: tamanho (4) + tipo (4) + dados (13) + CRC (4)
const AFTER_IHDR = 8 + 4 + 4 + 13 + 4;

/** O PNG com o pHYs em `dpi` (substitui um que já houvesse). Recusa o que não é PNG. */
export function withDpi(png: Uint8Array, dpi = 300): Uint8Array {
  if (png.length < AFTER_IHDR || PNG_SIGNATURE.some((b, i) => png[i] !== b)) {
    throw new Error("não é um PNG");
  }
  const perMeter = Math.round(dpi / 0.0254);
  const chunk = new Uint8Array(4 + 4 + 9 + 4);
  const view = new DataView(chunk.buffer);
  view.setUint32(0, 9);
  chunk.set([0x70, 0x48, 0x59, 0x73], 4); // "pHYs"
  view.setUint32(8, perMeter);
  view.setUint32(12, perMeter);
  chunk[16] = 1; // a unidade é o metro
  view.setUint32(17, crc32(chunk.subarray(4, 17)));

  // tira um pHYs que o navegador tenha posto, para não haver dois
  const rest: Uint8Array[] = [];
  let offset = AFTER_IHDR;
  const src = new DataView(png.buffer, png.byteOffset, png.byteLength);
  while (offset + 8 <= png.length) {
    const length = src.getUint32(offset);
    const type = String.fromCharCode(...png.subarray(offset + 4, offset + 8));
    const end = offset + 12 + length;
    if (type !== "pHYs") rest.push(png.subarray(offset, end));
    offset = end;
  }
  const out = new Uint8Array(AFTER_IHDR + chunk.length + rest.reduce((n, r) => n + r.length, 0));
  out.set(png.subarray(0, AFTER_IHDR), 0);
  out.set(chunk, AFTER_IHDR);
  let at = AFTER_IHDR + chunk.length;
  for (const r of rest) {
    out.set(r, at);
    at += r.length;
  }
  return out;
}

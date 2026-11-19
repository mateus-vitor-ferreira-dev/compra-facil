import { withDpi } from "./png";
import { PALETTE, PRINT_SIZES, type PieceKind, type ResolvedTemplate } from "./templates";

/**
 * Desenha a arte no navegador (canvas), no tamanho de impressão da peça, com o fundo transparente: a cor da peça
 * é o fundo. Só no painel, na hora de baixar o arquivo do pedido; o servidor não desenha nada.
 */

const SERIF = "Embrulhado Serif";
const SANS = "Embrulhado Sans";

let fontsReady: Promise<void> | undefined;

/** As fontes da arte (OFL, em /fonts), carregadas uma vez. */
export function loadArtFonts(): Promise<void> {
  fontsReady ??= (async () => {
    const faces = [
      new FontFace(SERIF, "url(/fonts/DMSerifDisplay-Italic.ttf)", { style: "italic" }),
      new FontFace(SERIF, "url(/fonts/DMSerifDisplay-Regular.ttf)", { style: "normal" }),
      new FontFace(SANS, "url(/fonts/Inter.ttf)", { weight: "100 900" }),
    ];
    await Promise.all(faces.map((f) => f.load()));
    for (const f of faces) document.fonts.add(f);
  })();
  return fontsReady;
}

type Ctx = CanvasRenderingContext2D;

const sans = (px: number) => `700 ${px}px "${SANS}"`;
const serif = (px: number) => `italic 400 ${px}px "${SERIF}"`;

function textWidth(ctx: Ctx, text: string, font: string, spacing = 0): number {
  ctx.font = font;
  ctx.letterSpacing = `${spacing}px`;
  return ctx.measureText(text).width;
}

/** O maior tamanho (até start) em que o texto cabe na largura. */
function fit(
  ctx: Ctx,
  text: string,
  font: (px: number) => string,
  maxWidth: number,
  start: number,
  spacing = 0,
) {
  let px = start;
  while (px > 12 && textWidth(ctx, text, font(px), spacing * (px / start)) > maxWidth)
    px -= Math.max(1, px * 0.03);
  return px;
}

/** Quebra o texto grande em duas linhas no espaço mais perto do meio, quando ele é longo para uma. */
export function splitLines(text: string, maxPerLine = 12): string[] {
  if ([...text].length <= maxPerLine || !text.includes(" ")) return [text];
  const middle = text.length / 2;
  let best = -1;
  for (let i = 0; i < text.length; i++) {
    if (text[i] === " " && (best < 0 || Math.abs(i - middle) < Math.abs(best - middle))) best = i;
  }
  return [text.slice(0, best), text.slice(best + 1)];
}

function star(ctx: Ctx, cx: number, cy: number, r: number) {
  ctx.beginPath();
  for (let i = 0; i < 10; i++) {
    const angle = -Math.PI / 2 + (i * Math.PI) / 5;
    const radius = i % 2 === 0 ? r : r * 0.42;
    ctx.lineTo(cx + radius * Math.cos(angle), cy + radius * Math.sin(angle));
  }
  ctx.closePath();
  ctx.fill();
}

/** O gorro do Papai Noel apoiado em (x, y), o meio-esquerdo da barra de pelúcia; w é a largura da barra. */
function hat(ctx: Ctx, x: number, y: number, w: number, furFill: string | null) {
  const h = w * 0.9;
  ctx.fillStyle = PALETTE.red;
  ctx.beginPath();
  ctx.moveTo(x + w * 0.08, y);
  ctx.quadraticCurveTo(x + w * 0.15, y - h * 1.05, x + w * 0.62, y - h * 0.95);
  ctx.quadraticCurveTo(x + w * 0.98, y - h * 0.9, x + w * 1.12, y - h * 0.38);
  ctx.quadraticCurveTo(x + w * 0.9, y - h * 0.45, x + w * 0.92, y);
  ctx.closePath();
  ctx.fill();
  const band = w * 0.24;
  ctx.lineWidth = w * 0.035;
  ctx.strokeStyle = PALETTE.green;
  ctx.beginPath();
  ctx.roundRect(x, y - band / 2, w, band, band / 2);
  if (furFill) {
    ctx.fillStyle = furFill;
    ctx.fill();
  }
  ctx.stroke();
  const r = w * 0.13;
  ctx.beginPath();
  ctx.arc(x + w * 1.12, y - h * 0.38, r, 0, Math.PI * 2);
  if (furFill) ctx.fill();
  ctx.stroke();
}

/** Desenha o bloco (kicker, texto grande, rodapé) centrado em cx, a partir de top, na largura dada. */
function drawBlock(
  ctx: Ctx,
  t: ResolvedTemplate,
  text: string,
  cx: number,
  top: number,
  width: number,
  piece: PieceKind,
) {
  const s = width / 3508;
  const maxWidth = width * 0.86;
  // a caneca não imprime branco: a pelúcia do gorro fica só no contorno
  const fur = piece === "caneca" ? null : "#FFFFFF";
  ctx.textAlign = "left";
  ctx.textBaseline = "alphabetic";
  let y = top;

  if (t.kicker) {
    const spacing = 28 * s;
    const px = fit(ctx, t.kicker, sans, maxWidth * 0.8, 150 * s, spacing);
    const sp = spacing * (px / (150 * s));
    const w = textWidth(ctx, t.kicker, sans(px), sp);
    ctx.fillStyle = PALETTE.green;
    ctx.fillText(t.kicker, cx - w / 2, y + px);
    ctx.fillStyle = PALETTE.red;
    star(ctx, cx - w / 2 - px * 0.9, y + px * 0.62, px * 0.38);
    star(ctx, cx + w / 2 + px * 0.6, y + px * 0.62, px * 0.38);
    y += px * 1.7;
  }

  const lines = splitLines(text);
  const start = (lines.length > 1 ? 700 : 900) * s;
  const px = Math.min(...lines.map((line) => fit(ctx, line, serif, maxWidth, start)));
  ctx.font = serif(px);
  ctx.letterSpacing = "0px";
  lines.forEach((line, i) => {
    const m = ctx.measureText(line);
    const ascent = m.actualBoundingBoxAscent;
    if (i === 0 && t.hat) {
      const first = ctx.measureText([...line][0] ?? "");
      const hw = first.width * 0.85;
      y += hw * 0.7; // o espaço do gorro acima da primeira letra
      ctx.fillStyle = t.color;
      ctx.fillText(line, cx - m.width / 2, y + ascent);
      hat(ctx, cx - m.width / 2 + first.width * 0.22, y + hw * 0.08, hw, fur);
    } else {
      ctx.fillStyle = t.color;
      ctx.fillText(line, cx - m.width / 2, y + ascent);
    }
    y += (ascent + m.actualBoundingBoxDescent) * 1.12;
  });

  if (t.footer) {
    const spacing = 40 * s;
    const fpx = fit(ctx, t.footer, sans, maxWidth * 0.7, 130 * s, spacing);
    const w = textWidth(ctx, t.footer, sans(fpx), spacing * (fpx / (130 * s)));
    ctx.fillStyle = PALETTE.green;
    ctx.fillText(t.footer, cx - w / 2, y + fpx * 1.2);
  }
}

/** A arte inteira no canvas: na caneca, a mesma nos dois lados; nas roupas, na altura do peito. */
export function drawArt(canvas: HTMLCanvasElement, t: ResolvedTemplate, text: string, piece: PieceKind) {
  const { width, height } = PRINT_SIZES[piece];
  canvas.width = width;
  canvas.height = height;
  const ctx = canvas.getContext("2d");
  if (!ctx) throw new Error("o navegador não desenha em canvas");
  ctx.clearRect(0, 0, width, height);
  if (piece === "caneca" || t.twoSides) {
    for (const cx of [width * 0.25, width * 0.75])
      drawBlock(ctx, t, text, cx, height * 0.1, width / 2, piece);
    return;
  }
  // a camiseta adulto tem a arte no peito (o A3 vai até a barriga); as peças menores usam a área toda
  const top = piece === "adulto" ? height * 0.12 : height * 0.06;
  drawBlock(ctx, t, text, width / 2, top, width, piece);
}

/** O PNG de impressão (300 DPI, fundo transparente) da arte. */
export async function renderArtPng(t: ResolvedTemplate, text: string, piece: PieceKind): Promise<Blob> {
  await loadArtFonts();
  const canvas = document.createElement("canvas");
  drawArt(canvas, t, text, piece);
  const blob = await new Promise<Blob>((resolve, reject) =>
    canvas.toBlob((b) => (b ? resolve(b) : reject(new Error("o canvas não gerou o PNG"))), "image/png"),
  );
  const png = withDpi(new Uint8Array(await blob.arrayBuffer()));
  return new Blob([png.slice().buffer], { type: "image/png" });
}

import {
  illustrationUrl,
  linesWithText,
  type ArtLine,
  type Face,
  type IllustratedTemplate,
  type Palette,
  type Variant,
} from "./illustrated";
import { withDpi } from "./png";
import { PRINT_SIZES, type PieceKind } from "./templates";

/**
 * O "Baixar arte" das artes ilustradas, o mesmo desenho do motor das estampas (Python, scratchpad do projeto): a
 * ilustração centralizada em cima, na largura da peça, e o bloco de texto embaixo (kicker com espaçamento e
 * ornamentos, o texto grande ajustado para caber em 86% da largura, o rodapé). Na caneca, a arte vai repetida nas
 * duas metades, e a altura de 9,5 cm manda: a ilustração encolhe para o bloco caber.
 */

// a largura da ilustração (fração da área) e a escala da tipografia, por peça
const LAYOUT: Record<PieceKind, { frac: number; scale: number; top: number }> = {
  adulto: { frac: 0.62, scale: 0.78, top: 300 },
  infantil: { frac: 0.66, scale: 0.55, top: 160 },
  body: { frac: 0.66, scale: 0.4, top: 110 },
  pet: { frac: 0.5, scale: 0.46, top: 140 },
  caneca: { frac: 0.3, scale: 0.34, top: 0 },
};

const FAMILY: Record<Face, { family: string; style?: string; weight?: string }> = {
  "serif-it": { family: "Arte Serif", style: "italic" },
  script: { family: "Arte Script", weight: "700" },
  pacifico: { family: "Arte Pacifico" },
  anton: { family: "Arte Anton" },
  round: { family: "Arte Round", weight: "800" },
  sans: { family: "Arte Sans", weight: "700" },
  lilita: { family: "Arte Lilita" },
  bowlby: { family: "Arte Bowlby" },
};

let ready: Promise<void> | undefined;

/** As fontes OFL das artes (public/fonts), carregadas uma vez. */
export function loadIllustratedFonts(): Promise<void> {
  ready ??= (async () => {
    const faces = [
      new FontFace("Arte Serif", "url(/fonts/DMSerifDisplay-Italic.ttf)", { style: "italic" }),
      new FontFace("Arte Script", "url(/fonts/DancingScript.ttf)", { weight: "400 700" }),
      new FontFace("Arte Pacifico", "url(/fonts/Pacifico-Regular.ttf)"),
      new FontFace("Arte Anton", "url(/fonts/Anton-Regular.ttf)"),
      new FontFace("Arte Round", "url(/fonts/Baloo2.ttf)", { weight: "400 800" }),
      new FontFace("Arte Sans", "url(/fonts/InterDisplay-Bold.otf)", { weight: "700" }),
      new FontFace("Arte Lilita", "url(/fonts/LilitaOne-Regular.ttf)"),
      new FontFace("Arte Bowlby", "url(/fonts/BowlbyOneSC-Regular.ttf)"),
    ];
    await Promise.all(faces.map((f) => f.load()));
    for (const f of faces) document.fonts.add(f);
  })();
  return ready;
}

const fontOf = (face: Face, px: number) => {
  const f = FAMILY[face];
  return `${f.style ?? "normal"} ${f.weight ?? "400"} ${px}px "${f.family}"`;
};

type Ctx = CanvasRenderingContext2D;

function star(ctx: Ctx, cx: number, cy: number, s: number) {
  ctx.beginPath();
  for (let i = 0; i < 10; i++) {
    const a = -Math.PI / 2 + (i * Math.PI) / 5;
    const r = i % 2 === 0 ? s / 2 : s * 0.21;
    ctx.lineTo(cx + r * Math.cos(a), cy + r * Math.sin(a));
  }
  ctx.closePath();
  ctx.fill();
}

function heart(ctx: Ctx, cx: number, cy: number, s: number) {
  ctx.beginPath();
  for (let i = 0; i < 80; i++) {
    const t = (2 * Math.PI * i) / 80;
    const x = 16 * Math.sin(t) ** 3;
    const y = 13 * Math.cos(t) - 5 * Math.cos(2 * t) - 2 * Math.cos(3 * t) - Math.cos(4 * t);
    ctx.lineTo(cx + (x * s) / 34, cy - (y * s) / 34);
  }
  ctx.closePath();
  ctx.fill();
}

/** Barra de carregamento com corações (gestante): o contorno e 5 corações. */
function loading(ctx: Ctx, cx: number, cy: number, s: number, color: string, hearts: string) {
  const w = s * 3.2;
  const h = s * 0.62;
  const x0 = cx - w / 2;
  ctx.strokeStyle = color;
  ctx.lineWidth = Math.max(3, Math.floor(s * 0.07));
  ctx.beginPath();
  ctx.roundRect(x0, cy - h / 2, w, h, h / 2);
  ctx.stroke();
  const step = (w - h * 0.6) / 7;
  ctx.fillStyle = hearts;
  for (let i = 0; i < 5; i++) heart(ctx, x0 + h * 0.3 + step * (i + 0.5), cy, h * 0.62);
}

const DEFAULT_COLOR: Record<ArtLine["k"], string> = { kicker: "b", footer: "b", big: "a", icon: "a" };

/** A cor da linha: a chave `c` dela na paleta, ou a padrão do tipo. */
const lineColor = (line: ArtLine, pal: Palette) => pal[line.c ?? DEFAULT_COLOR[line.k]] ?? pal.a;

/** A largura do texto com o espaçamento entre as letras (sem o espaço depois da última). */
function spacedWidth(ctx: Ctx, text: string, spacing: number) {
  ctx.letterSpacing = `${spacing}px`;
  return ctx.measureText(text).width - spacing;
}

type Measured = { height: number; px: number };

function measure(ctx: Ctx, line: ArtLine, maxWidth: number, scale: number): Measured {
  if (line.k === "icon") {
    const px = (line.size ?? 420) * scale;
    return { px, height: px * 1.25 };
  }
  if (line.k === "kicker" || line.k === "footer") {
    const px = Math.floor((line.size ?? (line.k === "kicker" ? 150 : 130)) * scale);
    return { px, height: px * (line.k === "kicker" ? 1.55 : 1.7) };
  }
  const face = line.face ?? "serif-it";
  let px = Math.floor((line.size ?? 900) * scale);
  ctx.letterSpacing = "0px";
  for (;;) {
    ctx.font = fontOf(face, px);
    const m = ctx.measureText(line.t ?? "");
    if (px <= 30 || m.actualBoundingBoxLeft + m.actualBoundingBoxRight <= maxWidth * (line.w ?? 1)) {
      return { px, height: (m.actualBoundingBoxAscent + m.actualBoundingBoxDescent) * (line.lh ?? 1.18) };
    }
    px -= 6;
  }
}

/** Desenha (ou só mede, com draw falso) o bloco de texto centrado em cx a partir de top; devolve a altura. */
function drawBlock(
  ctx: Ctx,
  cx: number,
  top: number,
  lines: ArtLine[],
  maxWidth: number,
  scale: number,
  pal: Palette,
  draw = true,
): number {
  let y = top;
  for (const line of lines) {
    const { px, height } = measure(ctx, line, maxWidth, scale);
    if (draw) {
      ctx.textAlign = "left";
      ctx.textBaseline = "alphabetic";
      if (line.k === "icon") {
        const color = lineColor(line, pal);
        if (line.i === "loading") loading(ctx, cx, y + px * 0.55, px, color, pal.b);
        else {
          ctx.fillStyle = color;
          (line.i === "heart" ? heart : star)(ctx, cx, y + px * 0.55, px);
        }
      } else if (line.k === "big") {
        ctx.font = fontOf(line.face ?? "serif-it", px);
        ctx.letterSpacing = "0px";
        const m = ctx.measureText(line.t ?? "");
        const width = m.actualBoundingBoxLeft + m.actualBoundingBoxRight;
        ctx.fillStyle = lineColor(line, pal);
        ctx.fillText(line.t ?? "", cx - width / 2 + m.actualBoundingBoxLeft, y + m.actualBoundingBoxAscent);
      } else {
        ctx.font = fontOf("sans", px);
        const text = line.t ?? "";
        const spacing = Math.floor(px * 0.2);
        const width = spacedWidth(ctx, text, spacing);
        const ascent = ctx.measureText(text).fontBoundingBoxAscent;
        ctx.fillStyle = lineColor(line, pal);
        ctx.fillText(text, cx - width / 2, y + ascent);
        if (line.orn) {
          const s = px * 0.75;
          ctx.fillStyle = pal.a;
          for (const sx of [cx - width / 2 - s * 1.2, cx + width / 2 + s * 1.2]) {
            (line.orn === "heart" ? heart : star)(ctx, sx, y + px * 0.62, s);
          }
        }
      }
    }
    y += height;
  }
  return y - top;
}

async function loadImage(url: string): Promise<HTMLImageElement> {
  const img = new Image();
  img.decoding = "async";
  img.src = url;
  await img.decode();
  return img;
}

/** A arte inteira no canvas, na versão (clara, escura ou vermelha) e com o texto do cliente. */
export async function drawIllustrated(
  canvas: HTMLCanvasElement,
  t: IllustratedTemplate,
  text: string,
  variant: Variant,
): Promise<void> {
  await loadIllustratedFonts();
  const piece = t.produto;
  const { width: W, height: H } = PRINT_SIZES[piece];
  const { frac, scale, top: baseTop } = LAYOUT[piece];
  canvas.width = W;
  canvas.height = H;
  const ctx = canvas.getContext("2d");
  if (!ctx) throw new Error("o navegador não desenha em canvas");
  ctx.clearRect(0, 0, W, H);
  const pal = t.paleta[variant];
  const lines = linesWithText(t, text);
  const img = await loadImage(illustrationUrl(t, variant));

  let ilW = W * frac;
  let ilH = (img.naturalHeight / img.naturalWidth) * ilW;
  let top = baseTop;
  const mug = piece === "caneca";
  const textWidth = W * (mug ? 0.42 : 0.86);
  if (mug) {
    // caneca: a altura manda (9,5 cm), o bloco inteiro tem de caber
    const textHeight = drawBlock(ctx, W / 4, 0, lines, textWidth, scale, pal, false);
    const maxH = H - 80 - textHeight;
    if (ilH > maxH * 0.95) {
      const r = (maxH * 0.95) / ilH;
      ilW *= r;
      ilH *= r;
    }
    top = Math.max(30, (H - ilH - textHeight - 30) / 2);
  }
  ctx.imageSmoothingQuality = "high";
  for (const cx of mug ? [W * 0.25, W * 0.75] : [W / 2]) {
    ctx.drawImage(img, cx - ilW / 2, top, ilW, ilH);
    drawBlock(ctx, cx, top + ilH + (mug ? 30 : H * 0.025), lines, textWidth, scale, pal);
  }
}

/** O PNG de impressão (300 DPI, fundo transparente) da arte ilustrada. */
export async function renderIllustratedPng(
  t: IllustratedTemplate,
  text: string,
  variant: Variant,
): Promise<Blob> {
  const canvas = document.createElement("canvas");
  await drawIllustrated(canvas, t, text, variant);
  const blob = await new Promise<Blob>((resolve, reject) =>
    canvas.toBlob((b) => (b ? resolve(b) : reject(new Error("o canvas não gerou o PNG"))), "image/png"),
  );
  const png = withDpi(new Uint8Array(await blob.arrayBuffer()));
  return new Blob([png.slice().buffer], { type: "image/png" });
}

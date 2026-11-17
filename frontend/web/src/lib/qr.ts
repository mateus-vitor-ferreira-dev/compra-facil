import "server-only";
import QRCode from "qrcode";

/**
 * O QR code em SVG, gerado no servidor: o segredo do autenticador não passa por nenhum serviço de fora. Fundo
 * branco e módulos pretos mesmo no tema escuro (a câmera do celular lê melhor) e margem de 4 módulos, como
 * pede a norma.
 */
export function qrSvg(text: string): Promise<string> {
  return QRCode.toString(text, {
    type: "svg",
    errorCorrectionLevel: "M",
    margin: 4,
    color: { dark: "#000000", light: "#ffffff" },
  });
}

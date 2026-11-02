"use client";

import { useEffect, useRef, type ComponentProps } from "react";

// o "Remover" marca que o foco deve ir para o "Desfazer" quando a página voltar sem a linha: o botão focado
// sumiu, e sem isso o teclado cairia no começo da página (#273). A marca é só desta aba e deste clique: ao
// recarregar o carrinho com o "Desfazer" ainda na tela, o foco fica onde estava
const KEY = "cf_focus_undo";

/** O botão "Remover" do carrinho: o mesmo botão de envio, que deixa a marca antes de enviar. */
export function RemoveButton(props: ComponentProps<"button">) {
  return <button {...props} onClick={() => sessionStorage.setItem(KEY, "1")} />;
}

/** Em volta do "Desfazer": recebe o foco se o cliente acabou de remover a linha. */
export function UndoFocus({ children }: { children: React.ReactNode }) {
  const box = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (sessionStorage.getItem(KEY) !== "1") return;
    sessionStorage.removeItem(KEY);
    box.current?.querySelector<HTMLButtonElement>("button")?.focus();
  }, []);
  return <div ref={box}>{children}</div>;
}

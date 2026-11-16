#!/usr/bin/env bash
# Guarda as credenciais da Efí no Bitwarden, e as traz de volta em outro computador.
#   scripts/efi-cofre.sh guardar    ~/.efi/{homologacao,producao}.{env,p12} -> Bitwarden
#   scripts/efi-cofre.sh restaurar  Bitwarden -> ~/.efi (chmod 600)
#
# Cada ambiente vira uma nota segura, "Efí Embrulhado - <ambiente>": o .env inteiro e o .p12 em base64 (cabe nas
# notas, sem anexo, que é do plano pago). Rode no seu terminal, não pelo chat: o bw pede a senha mestra, e a sessão
# não pode aparecer em lugar nenhum. Nenhum valor é impresso.
set -euo pipefail
umask 077

dir="$HOME/.efi"
ambientes=(homologacao producao)
nome() { echo "Efí Embrulhado - $1"; }

case "${1:-}" in guardar|restaurar) ;; *) echo "uso: $0 guardar|restaurar"; exit 2 ;; esac

command -v bw >/dev/null || { echo "falta o Bitwarden CLI: npm i -g @bitwarden/cli"; exit 1; }
command -v jq >/dev/null || { echo "falta o jq"; exit 1; }

case "$(bw status | jq -r .status)" in
  unauthenticated) echo "entre primeiro: bw login"; exit 1 ;;
  locked) BW_SESSION=$(bw unlock --raw); export BW_SESSION ;;
esac
bw sync >/dev/null

id_de() { bw list items --search "$(nome "$1")" | jq -r --arg n "$(nome "$1")" '[.[] | select(.name == $n)][0].id // empty'; }

guardar() {
  local amb=$1 env="$dir/$1.env" p12="$dir/$1.p12" notes id
  [ -f "$env" ] || { echo "$amb: sem o $env, pulei"; return; }
  # o caminho do .p12 sai do .env: em outro computador, o restaurar põe o daquele $HOME
  notes=$(grep -v '^EFI_P12=' "$env")
  if [ -s "$p12" ]; then
    notes+=$'\n'"EFI_P12_BASE64=$(base64 -w0 "$p12")"
  fi
  id=$(id_de "$amb")
  if [ -n "$id" ]; then
    bw get item "$id" | jq --arg n "$notes" '.notes = $n' | bw encode | bw edit item "$id" >/dev/null
    echo "$amb: atualizado no Bitwarden"
  else
    bw get template item \
      | jq --arg name "$(nome "$amb")" --arg n "$notes" '.type = 2 | .secureNote = {type: 0} | .name = $name | .notes = $n | .login = null' \
      | bw encode | bw create item >/dev/null
    echo "$amb: criado no Bitwarden"
  fi
}

restaurar() {
  local amb=$1 id notes
  id=$(id_de "$amb")
  [ -n "$id" ] || { echo "$amb: não está no Bitwarden, pulei"; return; }
  mkdir -p "$dir" && chmod 700 "$dir"
  notes=$(bw get item "$id" | jq -r .notes)
  printf '%s\n' "$notes" | grep -v '^EFI_P12_BASE64=' > "$dir/$amb.env"
  echo "EFI_P12=$dir/$amb.p12" >> "$dir/$amb.env"
  if printf '%s\n' "$notes" | grep -q '^EFI_P12_BASE64='; then
    printf '%s\n' "$notes" | sed -n 's/^EFI_P12_BASE64=//p' | base64 -d > "$dir/$amb.p12"
  fi
  chmod 600 "$dir/$amb".*
  echo "$amb: restaurado em $dir"
}

case "${1:-}" in
  guardar) for a in "${ambientes[@]}"; do guardar "$a"; done ;;
  restaurar) for a in "${ambientes[@]}"; do restaurar "$a"; done ;;
  *) echo "uso: $0 guardar|restaurar"; exit 2 ;;
esac

#!/usr/bin/env python3
"""Access token do operador local, com o segundo fator: `task token`.

A 2FA é obrigatória para o papel admin. No ambiente local, o compose liga a do operador com uma chave TOTP fixa
(Identity__Admin__TotpKey); este script faz o login com a senha, calcula o código da chave (RFC 6238, como o app
autenticador) e imprime o access token, que já leva o amr do segundo fator.

Uso: admin-token.py <url do identity> <e-mail> <senha> <chave TOTP em Base32>
"""

import base64
import hashlib
import hmac
import json
import struct
import sys
import time
import urllib.error
import urllib.request


def totp(key: str, at: float) -> str:
    clean = key.replace(" ", "").upper()
    secret = base64.b32decode(clean + "=" * (-len(clean) % 8))
    digest = hmac.new(secret, struct.pack(">Q", int(at // 30)), hashlib.sha1).digest()
    offset = digest[-1] & 0x0F
    code = (struct.unpack(">I", digest[offset : offset + 4])[0] & 0x7FFFFFFF) % 1_000_000
    return f"{code:06d}"


def post(url: str, body: dict) -> tuple[int, dict]:
    request = urllib.request.Request(
        url, data=json.dumps(body).encode(), headers={"Content-Type": "application/json"}, method="POST"
    )
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            return response.status, json.loads(response.read() or b"{}")
    except urllib.error.HTTPError as error:
        return error.code, json.loads(error.read() or b"{}")


def main() -> int:
    identity, email, password, key = sys.argv[1:5]
    status, challenge = post(f"{identity}/v1/sessions", {"email": email, "password": password})
    if status != 202 or challenge.get("status") != "mfa_required":
        print(f"login do operador: {status} {challenge}", file=sys.stderr)
        return 1
    for _ in range(2):
        status, tokens = post(
            f"{identity}/v1/sessions/mfa", {"ticket": challenge["mfa_ticket"], "code": totp(key, time.time())}
        )
        if status == 200:
            print(tokens["access_token"])
            return 0
        if status != 422:
            break
        time.sleep(31 - time.time() % 30)  # o código deste passo já foi usado: o próximo
    print(f"segundo fator: {status} {tokens}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())

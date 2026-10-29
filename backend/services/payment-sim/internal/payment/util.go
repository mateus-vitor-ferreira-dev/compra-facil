package payment

import (
	"crypto/rand"
	"encoding/base64"
	"encoding/hex"
	"fmt"
	"strconv"
	"strings"

	qrcode "github.com/skip2/go-qrcode"
)

// Amount é um valor em reais como o Mercado Pago manda (número decimal, 120.9), guardado em centavos.
type Amount struct{ Cents int64 }

func (a Amount) MarshalJSON() ([]byte, error) {
	return []byte(fmt.Sprintf("%d.%02d", a.Cents/100, a.Cents%100)), nil
}

func (a *Amount) UnmarshalJSON(data []byte) error {
	s := strings.Trim(strings.TrimSpace(string(data)), `"`)
	whole, frac, _ := strings.Cut(s, ".")
	if len(frac) > 2 {
		return fmt.Errorf("valor %q: no máximo 2 casas", s)
	}
	frac += strings.Repeat("0", 2-len(frac))
	w, err := strconv.ParseInt(whole, 10, 64)
	if err != nil {
		return fmt.Errorf("valor %q: %w", s, err)
	}
	f, err := strconv.ParseInt(frac, 10, 64)
	if err != nil {
		return fmt.Errorf("valor %q: %w", s, err)
	}
	a.Cents = w*100 + f
	return nil
}

// brandOf é a bandeira pelo início do número (as que o simulador aceita).
func brandOf(digits string) (string, bool) {
	if len(digits) < 13 || len(digits) > 19 || strings.Trim(digits, "0123456789") != "" {
		return "", false
	}
	switch {
	case digits[0] == '4':
		return "visa", true
	case digits[0] == '5':
		return "master", true
	case strings.HasPrefix(digits, "34"), strings.HasPrefix(digits, "37"):
		return "amex", true
	}
	return "", false
}

func luhn(digits string) bool {
	sum := 0
	for i := range digits {
		d := int(digits[len(digits)-1-i] - '0')
		if i%2 == 1 {
			d *= 2
			if d > 9 {
				d -= 9
			}
		}
		sum += d
	}
	return sum%10 == 0
}

func randomHex(n int) string {
	b := make([]byte, n)
	_, _ = rand.Read(b)
	return hex.EncodeToString(b)
}

// pixCode monta um "copia e cola" no formato EMV do PIX (BR Code), com o CRC16 no fim. A chave e o
// recebedor são do simulador: o código não paga nada num banco de verdade.
func pixCode(id int64, amount Amount) string {
	field := func(tag, value string) string { return fmt.Sprintf("%s%02d%s", tag, len(value), value) }
	account := field("00", "br.gov.bcb.pix") + field("01", "simulador@comprafacil.local")
	txid := field("05", fmt.Sprintf("SIM%d", id))
	payload := field("00", "01") + field("26", account) + field("52", "0000") + field("53", "986") +
		field("54", fmt.Sprintf("%d.%02d", amount.Cents/100, amount.Cents%100)) + field("58", "BR") +
		field("59", "COMPRAFACIL SIMULADO") + field("60", "LAVRAS") + field("62", txid) + "6304"
	return payload + fmt.Sprintf("%04X", crc16(payload))
}

// crc16 é o CRC-16/CCITT-FALSE que o BR Code usa.
func crc16(s string) uint16 {
	crc := uint16(0xFFFF)
	for i := range len(s) {
		crc ^= uint16(s[i]) << 8
		for range 8 {
			if crc&0x8000 != 0 {
				crc = crc<<1 ^ 0x1021
			} else {
				crc <<= 1
			}
		}
	}
	return crc
}

// qrPNG é o QR do código em PNG, em base64 (o qr_code_base64 do Mercado Pago).
func qrPNG(code string) (string, error) {
	png, err := qrcode.Encode(code, qrcode.Medium, 256)
	if err != nil {
		return "", fmt.Errorf("QR do PIX: %w", err)
	}
	return base64.StdEncoding.EncodeToString(png), nil
}

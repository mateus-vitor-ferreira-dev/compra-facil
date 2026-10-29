package messaging

import (
	"strconv"
	"time"

	"github.com/twmb/franz-go/pkg/kgo"
)

// Headers de diagnóstico nos tópicos de retry e na DLT, os mesmos dos serviços .NET (ADR 0003). A
// origem (tópico, partição, offset) é a da primeira falha; os do produtor (traceparent) seguem iguais.
const (
	headerOriginalTopic     = "x-original-topic"
	headerOriginalPartition = "x-original-partition"
	headerOriginalOffset    = "x-original-offset"
	// quantas vezes a mensagem passou por um nível sem ser processada
	headerAttempts = "x-attempts"
	// quando a mensagem pode ser tentada de novo (Unix ms), nos tópicos de retry
	headerRetryAt  = "x-retry-at"
	headerError    = "x-error"
	headerFailedAt = "x-failed-at"

	maxErrorLength = 500
)

func header(r *kgo.Record, key string) (string, bool) {
	// vale o último, como no .NET (TryGetLastBytes)
	for i := len(r.Headers) - 1; i >= 0; i-- {
		if r.Headers[i].Key == key {
			return string(r.Headers[i].Value), true
		}
	}
	return "", false
}

func setHeader(headers []kgo.RecordHeader, key, value string) []kgo.RecordHeader {
	out := headers[:0:0]
	for _, h := range headers {
		if h.Key != key {
			out = append(out, h)
		}
	}
	return append(out, kgo.RecordHeader{Key: key, Value: []byte(value)})
}

// failureHeaders são os headers da cópia que vai para o retry ou a DLT.
func failureHeaders(r *kgo.Record, topic string, cause error, now time.Time, retryAt *time.Time) []kgo.RecordHeader {
	headers := make([]kgo.RecordHeader, 0, len(r.Headers)+7)
	for _, h := range r.Headers {
		switch h.Key {
		case headerAttempts, headerRetryAt, headerError, headerFailedAt:
		default:
			headers = append(headers, h)
		}
	}
	if _, ok := header(r, headerOriginalTopic); !ok {
		headers = setHeader(headers, headerOriginalTopic, topic)
		headers = setHeader(headers, headerOriginalPartition, strconv.Itoa(int(r.Partition)))
		headers = setHeader(headers, headerOriginalOffset, strconv.FormatInt(r.Offset, 10))
	}
	attempts := 1
	if v, ok := header(r, headerAttempts); ok {
		if n, err := strconv.Atoi(v); err == nil {
			attempts = n + 1
		}
	}
	headers = setHeader(headers, headerAttempts, strconv.Itoa(attempts))
	msg := cause.Error()
	if len(msg) > maxErrorLength {
		msg = msg[:maxErrorLength]
	}
	headers = setHeader(headers, headerError, msg)
	headers = setHeader(headers, headerFailedAt, now.UTC().Format(time.RFC3339Nano))
	if retryAt != nil {
		headers = setHeader(headers, headerRetryAt, strconv.FormatInt(retryAt.UnixMilli(), 10))
	}
	return headers
}

// retryAt é quando a mensagem de um tópico de retry pode ser tentada (zero se não tem o header).
func retryAt(r *kgo.Record) time.Time {
	v, ok := header(r, headerRetryAt)
	if !ok {
		return time.Time{}
	}
	ms, err := strconv.ParseInt(v, 10, 64)
	if err != nil {
		return time.Time{}
	}
	return time.UnixMilli(ms)
}

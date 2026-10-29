package messaging

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"strconv"
	"sync"
	"time"

	"github.com/google/uuid"
	"github.com/twmb/franz-go/pkg/kadm"
	"github.com/twmb/franz-go/pkg/kgo"
)

// ErrNoRetryTopics: sem tópicos de retry, não há para onde devolver.
var ErrNoRetryTopics = errors.New("sem tópicos de retry para reprocessar")

// DeadLetter é uma mensagem parada na DLT, com a origem e o último erro (contrato: DeadLetter).
type DeadLetter struct {
	EventID   *uuid.UUID
	EventType *string
	Topic     string
	Partition *int
	Offset    *int64
	Attempts  int
	Error     *string
	FailedAt  *time.Time
}

// DeadLetterStore lista o que está pendente na DLT e reprocessa. A posição do reprocessamento é o
// consumer group <serviço>.dlt-replay: pendente é o que veio depois do último reprocessamento.
// Reprocessar devolve as mensagens para <serviço>.retry-1; falhando de novo, voltam para a DLT pelo
// caminho normal. O inbox garante que reprocessar algo já tratado não repete o efeito.
type DeadLetterStore struct {
	opts     Options
	failures *failureProducer
	log      *slog.Logger
	replay   sync.Mutex
}

func (s *DeadLetterStore) replayGroup() string { return s.opts.deadLetterTopic() + "-replay" }

// Pending devolve até limit mensagens pendentes, da mais antiga para a mais nova (por partição).
func (s *DeadLetterStore) Pending(ctx context.Context, limit int) ([]DeadLetter, error) {
	found := []DeadLetter{}
	err := s.readPending(ctx, limit, false, func(r *kgo.Record) error {
		found = append(found, describe(r))
		return nil
	})
	return found, err
}

// Replay devolve as pendentes para o primeiro tópico de retry; retorna quantas.
func (s *DeadLetterStore) Replay(ctx context.Context) (int, error) {
	if len(s.opts.RetryDelays) == 0 {
		return 0, ErrNoRetryTopics
	}
	s.replay.Lock()
	defer s.replay.Unlock()
	replayed := 0
	err := s.readPending(ctx, int(^uint(0)>>1), true, func(r *kgo.Record) error {
		headers := setHeader(append([]kgo.RecordHeader(nil), r.Headers...), headerRetryAt, strconv.FormatInt(time.Now().UnixMilli(), 10))
		if err := s.failures.produce(ctx, &kgo.Record{Topic: s.opts.retryTopic(1), Key: r.Key, Value: r.Value, Headers: headers}); err != nil {
			return err
		}
		replayed++
		return nil
	})
	if replayed > 0 {
		s.log.InfoContext(ctx, "mensagens da DLT devolvidas para reprocessamento", "count", replayed, "dlt", s.opts.deadLetterTopic())
	}
	return replayed, err
}

// readPending lê da posição do grupo de reprocessamento até o fim de cada partição (o fim no momento
// da leitura: o que chega durante não entra). Com commit, avança a posição até onde leu.
func (s *DeadLetterStore) readPending(ctx context.Context, limit int, commit bool, each func(*kgo.Record) error) error {
	if err := s.failures.ensureTopics(ctx); err != nil {
		return err
	}
	dlt := s.opts.deadLetterTopic()
	admin := s.failures.admin
	ctx, cancel := context.WithTimeout(ctx, 15*time.Second)
	defer cancel()

	committed, err := admin.FetchOffsetsForTopics(ctx, s.replayGroup(), dlt)
	if err != nil {
		return fmt.Errorf("posição do reprocessamento: %w", err)
	}
	starts, err := admin.ListStartOffsets(ctx, dlt)
	if err != nil {
		return fmt.Errorf("início da DLT: %w", err)
	}
	ends, err := admin.ListEndOffsets(ctx, dlt)
	if err != nil {
		return fmt.Errorf("fim da DLT: %w", err)
	}
	from := map[int32]kgo.Offset{}
	until := map[int32]int64{}
	ends.Each(func(end kadm.ListedOffset) {
		start := int64(0)
		if o, ok := starts.Lookup(dlt, end.Partition); ok {
			start = o.Offset
		}
		if c, ok := committed.Lookup(dlt, end.Partition); ok && c.At >= start {
			start = c.At
		}
		if start < end.Offset {
			from[end.Partition] = kgo.NewOffset().At(start)
			until[end.Partition] = end.Offset
		}
	})
	if len(from) == 0 {
		return nil
	}

	reader, err := kgo.NewClient(kgo.SeedBrokers(s.opts.Brokers...), kgo.ConsumePartitions(map[string]map[int32]kgo.Offset{dlt: from}))
	if err != nil {
		return fmt.Errorf("leitor da DLT: %w", err)
	}
	defer reader.Close()

	read := kadm.Offsets{}
	count := 0
	for len(until) > 0 && count < limit {
		fetches := reader.PollFetches(ctx)
		if ctx.Err() != nil {
			break // não chegou tudo a tempo: devolve o que leu
		}
		if err := fetches.Err0(); err != nil {
			return fmt.Errorf("lendo a DLT: %w", err)
		}
		var stop error
		fetches.EachRecord(func(r *kgo.Record) {
			end, pending := until[r.Partition]
			if stop != nil || count >= limit || !pending || r.Offset >= end {
				return
			}
			if err := each(r); err != nil {
				stop = err
				return
			}
			count++
			read.AddOffset(dlt, r.Partition, r.Offset+1, -1)
			if r.Offset+1 >= end {
				delete(until, r.Partition)
			}
		})
		if stop != nil {
			return s.commitRead(commit, read, stop)
		}
	}
	return s.commitRead(commit, read, nil)
}

// commitRead avança a posição do reprocessamento até o que foi lido (mesmo se parou no meio).
func (s *DeadLetterStore) commitRead(commit bool, read kadm.Offsets, cause error) error {
	if commit && len(read) > 0 {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		if _, err := s.failures.admin.CommitOffsets(ctx, s.replayGroup(), read); err != nil {
			return fmt.Errorf("guardando a posição do reprocessamento: %w", errors.Join(err, cause))
		}
	}
	return cause
}

func describe(r *kgo.Record) DeadLetter {
	d := DeadLetter{Topic: "?"}
	var env struct {
		EventID   uuid.UUID `json:"event_id"`
		EventType string    `json:"event_type"`
	}
	if json.Unmarshal(r.Value, &env) == nil && env.EventID != uuid.Nil {
		d.EventID, d.EventType = &env.EventID, &env.EventType
	}
	if v, ok := header(r, headerOriginalTopic); ok {
		d.Topic = v
	}
	if v, ok := header(r, headerOriginalPartition); ok {
		if n, err := strconv.Atoi(v); err == nil {
			d.Partition = &n
		}
	}
	if v, ok := header(r, headerOriginalOffset); ok {
		if n, err := strconv.ParseInt(v, 10, 64); err == nil {
			d.Offset = &n
		}
	}
	if v, ok := header(r, headerAttempts); ok {
		d.Attempts, _ = strconv.Atoi(v)
	}
	if v, ok := header(r, headerError); ok {
		d.Error = &v
	}
	if v, ok := header(r, headerFailedAt); ok {
		if t, err := time.Parse(time.RFC3339Nano, v); err == nil {
			d.FailedAt = &t
		}
	}
	return d
}

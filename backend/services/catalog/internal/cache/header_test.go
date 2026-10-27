package cache

import (
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/stretchr/testify/assert"
)

func TestHeaderMiddleware_MarksCacheResult(t *testing.T) {
	store := newMemStore()
	r := &fakeReaders{}
	c := newTestCatalog(t, store, r)
	h := HeaderMiddleware(http.HandlerFunc(func(w http.ResponseWriter, req *http.Request) {
		_, _ = c.List(req.Context())
		w.WriteHeader(http.StatusOK)
	}))
	do := func() string {
		rec := httptest.NewRecorder()
		h.ServeHTTP(rec, httptest.NewRequestWithContext(t.Context(), http.MethodGet, "/v1/categories", nil))
		return rec.Header().Get(Header)
	}

	assert.Equal(t, "MISS", do())
	assert.Equal(t, "HIT", do())
	store.down = true
	assert.Equal(t, "BYPASS", do())
}

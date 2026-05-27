package main

import (
	"encoding/base64"
	"fmt"
	"net/http"
	"os"
	"path/filepath"
)

var fixturesPath = getEnv("FIXTURES_PATH", "Fixtures")

func main() {
	port := getEnv("PORT", "{PORT}")
	mux := http.NewServeMux()
	registerRoutes(mux)
	fmt.Printf("EggIncognito Go mock server :%s  fixtures: %s\n", port, fixturesPath)
	if err := http.ListenAndServe(":"+port, mux); err != nil {
		fmt.Fprintf(os.Stderr, "error: %v\n", err)
		os.Exit(1)
	}
}

// GENERATED - do not edit by hand; re-run EggIncognito.CodeGen generate go
func registerRoutes(mux *http.ServeMux) {
{ROUTES}
}

func makeHandler(slug string) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodPost {
			http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
			return
		}
		if err := r.ParseForm(); err != nil {
			http.Error(w, "bad request", http.StatusBadRequest)
			return
		}
		fixture := loadFixture(slug, extractEid(r.FormValue("data")))
		w.Header().Set("Content-Type", "text/html")
		fmt.Fprint(w, base64.StdEncoding.EncodeToString(fixture))
	}
}

func loadFixture(slug, eid string) []byte {
	if eid != "" {
		if data, err := os.ReadFile(filepath.Join(fixturesPath, "eids", eid, slug+".binpb")); err == nil {
			return data
		}
	}
	if data, err := os.ReadFile(filepath.Join(fixturesPath, "default", slug+".binpb")); err == nil {
		return data
	}
	return []byte{}
}

// extractEid reads AuthenticatedMessage.user_id (field 6, wire type 2) from base64 data.
func extractEid(data string) string {
	raw, err := base64.StdEncoding.DecodeString(data)
	if err != nil || len(raw) == 0 {
		return ""
	}
	return readProtoString(raw, 6)
}

func readProtoString(data []byte, fieldNum uint64) string {
	i := 0
	for i < len(data) {
		tag, n := decodeVarint(data[i:])
		if n == 0 {
			break
		}
		i += n
		fn, wt := tag>>3, tag&7
		switch wt {
		case 0:
			_, n = decodeVarint(data[i:])
			i += n
		case 1:
			i += 8
		case 2:
			length, n := decodeVarint(data[i:])
			i += n
			if fn == fieldNum && i+int(length) <= len(data) {
				return string(data[i : i+int(length)])
			}
			i += int(length)
		case 5:
			i += 4
		default:
			return ""
		}
	}
	return ""
}

func decodeVarint(b []byte) (uint64, int) {
	var x uint64
	var s uint
	for i, v := range b {
		if i >= 10 {
			return 0, 0
		}
		x |= uint64(v&0x7f) << s
		s += 7
		if v < 0x80 {
			return x, i + 1
		}
	}
	return 0, 0
}

func getEnv(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

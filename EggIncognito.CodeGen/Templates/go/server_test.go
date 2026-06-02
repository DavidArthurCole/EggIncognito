//go:build ignore

package main

import (
	"os"
	"path/filepath"
	"testing"
)

const eidProto = "MhJFSTAwMDAwMDAwMDAwMDAwMDE="

func TestExtractEid(t *testing.T) {
	got := extractEid(eidProto)
	if got != "EI0000000000000001" {
		t.Errorf("got %q, want EI0000000000000001", got)
	}
}

func TestExtractEidEmpty(t *testing.T) {
	if extractEid("") != "" {
		t.Errorf("expected empty string for empty input")
	}
}

func TestLoadFixtureDefault(t *testing.T) {
	dir := t.TempDir()
	defDir := filepath.Join(dir, "default")
	os.MkdirAll(defDir, 0755)
	os.WriteFile(filepath.Join(defDir, "test.binpb"), []byte{0x01, 0x02}, 0644)

	orig := fixturesPath
	fixturesPath = dir
	t.Cleanup(func() { fixturesPath = orig })

	data := loadFixture("test", "")
	if len(data) != 2 || data[0] != 0x01 || data[1] != 0x02 {
		t.Errorf("unexpected fixture data: %v", data)
	}
}

func TestLoadFixtureEidOverride(t *testing.T) {
	dir := t.TempDir()
	os.MkdirAll(filepath.Join(dir, "default"), 0755)
	os.MkdirAll(filepath.Join(dir, "eids", "EI0000000000000001"), 0755)
	os.WriteFile(filepath.Join(dir, "default", "test.binpb"), []byte{0x01}, 0644)
	os.WriteFile(filepath.Join(dir, "eids", "EI0000000000000001", "test.binpb"), []byte{0x02}, 0644)

	orig := fixturesPath
	fixturesPath = dir
	t.Cleanup(func() { fixturesPath = orig })

	data := loadFixture("test", "EI0000000000000001")
	if len(data) != 1 || data[0] != 0x02 {
		t.Errorf("expected EID fixture byte 0x02, got %v", data)
	}
}

func TestLoadFixtureMissing(t *testing.T) {
	dir := t.TempDir()
	orig := fixturesPath
	fixturesPath = dir
	t.Cleanup(func() { fixturesPath = orig })

	data := loadFixture("nonexistent", "")
	if len(data) != 0 {
		t.Errorf("expected empty bytes for missing fixture, got %v", data)
	}
}

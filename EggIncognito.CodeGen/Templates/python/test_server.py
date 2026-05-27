import os
import sys
import pytest

sys.path.insert(0, os.path.dirname(__file__))
import server

EID_PROTO = "MhJFSTAwMDAwMDAwMDAwMDAwMDE="


def test_extract_eid():
    assert server._extract_eid(EID_PROTO) == "EI0000000000000001"


def test_extract_eid_empty():
    assert server._extract_eid("") == ""


def test_load_fixture_default(tmp_path):
    (tmp_path / "default").mkdir()
    (tmp_path / "default" / "test.binpb").write_bytes(b"\x01\x02")

    orig = server.FIXTURES_PATH
    server.FIXTURES_PATH = str(tmp_path)
    try:
        data = server._load_fixture("test", "")
        assert data == b"\x01\x02"
    finally:
        server.FIXTURES_PATH = orig


def test_load_fixture_eid_override(tmp_path):
    (tmp_path / "default").mkdir()
    (tmp_path / "eids" / "EI0000000000000001").mkdir(parents=True)
    (tmp_path / "default" / "test.binpb").write_bytes(b"\x01")
    (tmp_path / "eids" / "EI0000000000000001" / "test.binpb").write_bytes(b"\x02")

    orig = server.FIXTURES_PATH
    server.FIXTURES_PATH = str(tmp_path)
    try:
        data = server._load_fixture("test", "EI0000000000000001")
        assert data == b"\x02"
    finally:
        server.FIXTURES_PATH = orig


def test_load_fixture_missing(tmp_path):
    orig = server.FIXTURES_PATH
    server.FIXTURES_PATH = str(tmp_path)
    try:
        data = server._load_fixture("nonexistent", "")
        assert data == b""
    finally:
        server.FIXTURES_PATH = orig

'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const os = require('os');
const server = require('./server');

const EID_PROTO = 'MhJFSTAwMDAwMDAwMDAwMDAwMDE=';

test('extractEid returns correct EID', () => {
  assert.equal(server.extractEid(EID_PROTO), 'EI0000000000000001');
});

test('extractEid returns empty string for empty input', () => {
  assert.equal(server.extractEid(''), '');
});

test('loadFixture returns default fixture', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ei-test-'));
  try {
    fs.mkdirSync(path.join(dir, 'default'));
    fs.writeFileSync(path.join(dir, 'default', 'test.binpb'), Buffer.from([0x01, 0x02]));
    server.FIXTURES_PATH = dir;
    const data = server.loadFixture('test', '');
    assert.deepEqual(data, Buffer.from([0x01, 0x02]));
  } finally {
    fs.rmSync(dir, { recursive: true });
    server.FIXTURES_PATH = process.env.FIXTURES_PATH ?? 'Fixtures';
  }
});

test('loadFixture returns EID override', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ei-test-'));
  try {
    fs.mkdirSync(path.join(dir, 'default'));
    fs.mkdirSync(path.join(dir, 'eids', 'EI0000000000000001'), { recursive: true });
    fs.writeFileSync(path.join(dir, 'default', 'test.binpb'), Buffer.from([0x01]));
    fs.writeFileSync(path.join(dir, 'eids', 'EI0000000000000001', 'test.binpb'), Buffer.from([0x02]));
    server.FIXTURES_PATH = dir;
    const data = server.loadFixture('test', 'EI0000000000000001');
    assert.deepEqual(data, Buffer.from([0x02]));
  } finally {
    fs.rmSync(dir, { recursive: true });
    server.FIXTURES_PATH = process.env.FIXTURES_PATH ?? 'Fixtures';
  }
});

test('loadFixture returns empty buffer for missing fixture', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ei-test-'));
  try {
    server.FIXTURES_PATH = dir;
    const data = server.loadFixture('nonexistent', '');
    assert.deepEqual(data, Buffer.alloc(0));
  } finally {
    fs.rmSync(dir, { recursive: true });
    server.FIXTURES_PATH = process.env.FIXTURES_PATH ?? 'Fixtures';
  }
});

# proto-extract toolchain

Vendored third-party `pbtk` that recovers Egg Inc `.proto` text from an APK. C# `ApkExtractService` shells out to it. Copy as-is, do not "clean" the python.

## Input

- Use the ARM split `config.arm64_v8a.apk`. NOT `base.apk`.
- The compiled proto lives in `lib/arm64-v8a/libegginc.so`, present only in the arm split.

## Pipeline

1. `python -W ignore pbtk/extractors/jar_extract.py <armApk> <outDir>` writes `outDir/ei.proto` + `outDir/common.proto`.
2. C# `ProtoCleanup` merges `common.proto` into `ei.proto` (after `package ei;`, drops the import, strips aux prefixes). `protocleanup.py` is reference-only, not run at runtime.

## Requirements

- `java` (default-jre-headless / openjdk) on PATH for dex2jar.
- `python3` with `protobuf` + `requests` (setup.sh makes a `.venv`).

## Setup

`./setup.sh` on a fresh linux checkout: strips CR from `*.sh`, fetches stable binaries, chmods them, makes `.venv` with `protobuf` + `requests`.

CRLF gotcha: a `#!/bin/sh\r` shebang makes the kernel report "required file not found". setup.sh runs `sed -i 's/\r$//'` over `*.sh`.

## External binaries (38MB, gitignored)

`pbtk/utils/external/` is gitignored (mirrors `tools/tailwind/`). setup.sh fetches dex2jar+protoc if missing and reuses any already present.

| Binary | Size | Source |
|---|---|---|
| dex2jar | 20MB | GitHub release (pxb1988/dex2jar v2.1) |
| protoc | 17MB | GitHub release (protocolbuffers/protobuf v25.1, linux-x86_64) |
| jad | 1.9MB | Operator supplies; no stable URL. Copy from the EggIncProtoExtractor repo. |

On fetch failure setup.sh prints the copy-from-EggIncProtoExtractor instruction.

## Config

| Key | Value |
|---|---|
| `ProtoExtract:Enabled` | `true` |
| `ProtoExtract:PythonPath` | `tools/proto-extract/.venv/bin/python3` |
| `ProtoExtract:RepoPath` | `tools/proto-extract/` |

`RepoPath` is the python cwd; the service invokes `pbtk/extractors/jar_extract.py` relative to it.

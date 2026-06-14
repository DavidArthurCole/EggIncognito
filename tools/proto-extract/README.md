# proto-extract toolchain

Vendored `pbtk` (third-party) that recovers the Egg Inc `.proto` text from an APK. The C# `ApkExtractService` shells out to it (`ProtoExtract:*` config). Copy as-is, do not "clean" the python.

## Input requirement

| Use | Skip |
|---|---|
| The ARM split `config.arm64_v8a.apk` | `base.apk` |

The compiled proto lives in `lib/arm64-v8a/libegginc.so`, present ONLY in the arm split. `base.apk` yields only ad-network SDK protos, not `ei.proto`.

## Pipeline

1. `python -W ignore pbtk/extractors/jar_extract.py <armApk> <outDir>` writes `outDir/ei.proto` + `outDir/common.proto`.
2. The C# `ProtoCleanup` then merges `common.proto` into `ei.proto` (after `package ei;`, drops the import, strips aux prefixes).

`protocleanup.py` is reference-only. It is NOT run at runtime; the C# `ProtoCleanup` replaces it (one fewer subprocess, parity is unit-tested).

## Requirements

- `java` (default-jre-headless / openjdk) on PATH. dex2jar runs the DEX -> JAR step.
- `python3` with `protobuf` + `requests` (setup.sh creates a `.venv` with these).

## Setup

Run `./setup.sh` on a fresh linux checkout. It:

- Strips CR from `*.sh` (CRLF gotcha, below).
- Ensures the external binaries exist, fetching the ones with stable URLs.
- `chmod +x` the wrappers + native binaries.
- Creates `.venv` and installs `protobuf` + `requests`.

### CRLF gotcha

Windows git adds CR to checked-out text files. A `#!/bin/sh\r` shebang on dex2jar's wrapper scripts makes the linux kernel report "required file not found". setup.sh runs `sed -i 's/\r$//'` over every `*.sh` to fix this before anything runs.

## External binaries (38MB, gitignored)

Not version-controlled (mirrors the `tools/tailwind/` posture). `pbtk/utils/external/` is gitignored; setup.sh places the binaries.

| Binary | Size | Source |
|---|---|---|
| dex2jar | 20MB | Fetched from GitHub release (pxb1988/dex2jar v2.1). |
| protoc | 17MB | Fetched from GitHub release (protocolbuffers/protobuf v25.1, linux-x86_64). |
| jad | 1.9MB | Operator supplies. No stable release URL. Copy `pbtk/utils/external/jad/` from the EggIncProtoExtractor repo or the frame. |

If a fetch fails, setup.sh prints a clear instruction to copy that dir from the EggIncProtoExtractor repo (the proven source). On the frame the binaries already exist at `~/ei-extract-full/pbtk/utils/external/`, so setup.sh reuses them rather than re-fetching.

## Config

| Key | Value |
|---|---|
| `ProtoExtract:Enabled` | `true` |
| `ProtoExtract:PythonPath` | `tools/proto-extract/.venv/bin/python3` |
| `ProtoExtract:RepoPath` | `tools/proto-extract/` |

`RepoPath` is the python cwd; the service invokes `pbtk/extractors/jar_extract.py` relative to it.

# EggIncognito.Core

The shared class library. No web dependency. Part of [EggIncognito](../README.md), referenced by every other project.

What lives here:

- The `Ei.*` proto types, generated from the frozen proto2 schema snapshot (message classes only, no gRPC stubs). Endpoint JSON goes through the protobuf JSON parser/formatter, never System.Text.Json.
- The service layer: endpoint store, transport pipeline (the single signing authority), route catalog, redaction, markdown rendering, wire forensics.
- Proto extraction: carves the embedded proto schema out of shipped mobile binaries (APK, IPA, Mach-O). Binaries are read as bytes, never executed.
- The capture-to-endpoint pipeline: decode, auto-detect type, redact, self-repair the route map, write the endpoint. Every capture path funnels through the same per-flow contract, so they cannot diverge.
- Device farm drivers: probe, proxy config, and CA install for the physical Android and iOS devices, all behind mockable process seams.

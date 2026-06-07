// Format converters for the request/response data sections. The decoded proto is delivered as a
// JSON string; these render it in alternate shapes. Hex/Bin operate on the RAW on-the-wire bytes
// (decoded from base64), not the JSON, since that is the only meaningful binary view.

// Formats that render the decoded JSON value.
export const JSON_FORMATS = ["json-tree", "json", "yaml", "xml", "js"];
// Formats that render the raw wire bytes (base64-decoded).
export const BYTE_FORMATS = ["hex", "bin"];

export const FORMAT_LABELS = {
  "json-tree": "JSON (tree)",
  json: "JSON (raw)",
  yaml: "YAML",
  xml: "XML",
  js: "JS object",
  hex: "Hex",
  bin: "Binary",
};

// --- JSON -> YAML -----------------------------------------------------------

export function toYaml(value, indent = 0) {
  const pad = "  ".repeat(indent);
  if (value === null) return "null";
  if (Array.isArray(value)) {
    if (value.length === 0) return "[]";
    return value.map((v) => {
      const child = toYaml(v, indent + 1);
      return isContainer(v) ? `${pad}-\n${child}` : `${pad}- ${child}`;
    }).join("\n");
  }
  if (typeof value === "object") {
    const keys = Object.keys(value);
    if (keys.length === 0) return "{}";
    return keys.map((k) => {
      const v = value[k];
      const key = yamlKey(k);
      if (isContainer(v) && (Array.isArray(v) ? v.length : Object.keys(v).length) > 0) {
        return `${pad}${key}:\n${toYaml(v, indent + 1)}`;
      }
      return `${pad}${key}: ${toYaml(v, indent + 1)}`;
    }).join("\n");
  }
  return scalarYaml(value);
}

function yamlKey(k) {
  return /^[A-Za-z0-9_]+$/.test(k) ? k : JSON.stringify(k);
}

function scalarYaml(v) {
  if (typeof v === "string") {
    // Quote strings that could be misread as another YAML type.
    if (v === "" || /[:#\-?{}[\],&*!|>'"%@`]/.test(v) || /^\s|\s$/.test(v) || /^(true|false|null|~|\d)/i.test(v)) {
      return JSON.stringify(v);
    }
    return v;
  }
  return String(v);
}

function isContainer(v) {
  return v !== null && typeof v === "object";
}

// --- JSON -> XML ------------------------------------------------------------

export function toXml(value, root = "root") {
  return `<${root}>${xmlBody(value)}</${root}>`;
}

function xmlBody(value) {
  if (value === null) return "";
  if (Array.isArray(value)) {
    return value.map((v) => `<item>${xmlBody(v)}</item>`).join("");
  }
  if (typeof value === "object") {
    return Object.keys(value).map((k) => {
      const tag = xmlTag(k);
      return `<${tag}>${xmlBody(value[k])}</${tag}>`;
    }).join("");
  }
  return xmlEscape(String(value));
}

function xmlTag(k) {
  // XML element names cannot start with a digit and allow a limited char set.
  let t = k.replace(/[^A-Za-z0-9_.-]/g, "_");
  if (/^[0-9.-]/.test(t)) t = "_" + t;
  return t || "_";
}

function xmlEscape(s) {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

// Pretty-print the (compact) XML produced above with indentation.
export function prettyXml(xml) {
  let out = "";
  let depth = 0;
  xml.replace(/></g, ">\n<").split("\n").forEach((node) => {
    if (/^<\/\w/.test(node)) depth--;
    out += "  ".repeat(Math.max(0, depth)) + node + "\n";
    if (/^<\w[^>]*[^/]>$/.test(node) && !/^<.*<\/.*>$/.test(node)) depth++;
  });
  return out.trim();
}

// --- JSON -> JS object literal ---------------------------------------------

export function toJsLiteral(value, indent = 0) {
  const pad = "  ".repeat(indent);
  const padIn = "  ".repeat(indent + 1);
  if (value === null) return "null";
  if (Array.isArray(value)) {
    if (value.length === 0) return "[]";
    const items = value.map((v) => padIn + toJsLiteral(v, indent + 1));
    return `[\n${items.join(",\n")}\n${pad}]`;
  }
  if (typeof value === "object") {
    const keys = Object.keys(value);
    if (keys.length === 0) return "{}";
    const items = keys.map((k) => `${padIn}${jsKey(k)}: ${toJsLiteral(value[k], indent + 1)}`);
    return `{\n${items.join(",\n")}\n${pad}}`;
  }
  if (typeof value === "string") return JSON.stringify(value);
  return String(value);
}

function jsKey(k) {
  return /^[A-Za-z_$][A-Za-z0-9_$]*$/.test(k) ? k : JSON.stringify(k);
}

// --- bytes -> hex / binary --------------------------------------------------

// Tolerant base64 -> Uint8Array (handles form-mangled '+'/'/' and missing padding).
export function bytesFromBase64(b64) {
  if (!b64) return new Uint8Array(0);
  let s = String(b64).trim().replace(/ /g, "+");
  const pad = s.length % 4;
  if (pad) s += "=".repeat(4 - pad);
  try {
    const bin = atob(s);
    const out = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
    return out;
  } catch {
    return new Uint8Array(0);
  }
}

// Classic hex dump: offset  16 hex bytes  | ascii |
export function toHexDump(bytes) {
  const lines = [];
  for (let i = 0; i < bytes.length; i += 16) {
    const slice = bytes.slice(i, i + 16);
    const off = i.toString(16).padStart(8, "0");
    const hex = [...slice].map((b) => b.toString(16).padStart(2, "0")).join(" ").padEnd(16 * 3 - 1, " ");
    const ascii = [...slice].map((b) => (b >= 32 && b < 127 ? String.fromCharCode(b) : ".")).join("");
    lines.push(`${off}  ${hex}  |${ascii}|`);
  }
  return lines.join("\n") || "(empty)";
}

// 8-bit binary per byte, 8 bytes per line, offset-prefixed.
export function toBinDump(bytes) {
  const lines = [];
  for (let i = 0; i < bytes.length; i += 8) {
    const slice = bytes.slice(i, i + 8);
    const off = i.toString(16).padStart(8, "0");
    const bits = [...slice].map((b) => b.toString(2).padStart(8, "0")).join(" ");
    lines.push(`${off}  ${bits}`);
  }
  return lines.join("\n") || "(empty)";
}

// Render the decoded-JSON string into the chosen text format. Returns the text, or null if the
// format does not apply (json-tree is rendered by the tree viewer, not here).
export function jsonToText(jsonStr, fmt) {
  let value;
  try { value = JSON.parse(jsonStr); } catch { return jsonStr; }
  switch (fmt) {
    case "json": return JSON.stringify(value, null, 2);
    case "yaml": return toYaml(value) || "{}";
    case "xml": return prettyXml(toXml(value));
    case "js": return toJsLiteral(value);
    default: return null;
  }
}

// Render the raw wire bytes (from base64) into the chosen byte format.
export function bytesToText(b64, fmt) {
  const bytes = bytesFromBase64(b64);
  return fmt === "bin" ? toBinDump(bytes) : toHexDump(bytes);
}

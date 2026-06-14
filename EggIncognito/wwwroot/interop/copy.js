// Minimal clipboard bridge for copyable code chips (proto SHA). Returns true on success.
export async function writeText(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}

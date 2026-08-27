export function insert(el, text) {
    if (!el) return "";
    const value = el.value ?? "";
    const start = typeof el.selectionStart === "number" ? el.selectionStart : value.length;
    const end = typeof el.selectionEnd === "number" ? el.selectionEnd : start;
    const next = value.slice(0, start) + text + value.slice(end);
    el.value = next;
    const caret = start + text.length;
    el.focus();
    if (typeof el.setSelectionRange === "function") {
        el.setSelectionRange(caret, caret);
    }
    return next;
}

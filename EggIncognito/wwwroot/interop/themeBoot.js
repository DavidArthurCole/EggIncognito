(function () {
  function tokenCss(v) {
    if (!v || typeof v !== "object") return null;
    if (typeof v.hex === "string" && /^#[0-9a-f]{6}$/.test(v.hex)) return v.hex;
    if (Number.isFinite(v.l) && Number.isFinite(v.c) && Number.isFinite(v.h)) {
      var l = Math.min(Math.max(v.l, 0), 1) * 100;
      var c = Math.min(Math.max(v.c, 0), 0.5);
      var h = ((v.h % 360) + 360) % 360;
      return "oklch(" + l.toFixed(1) + "% " + c.toFixed(3) + " " + h.toFixed(1) + ")";
    }
    return null;
  }

  try {
    var lockAccent = document.currentScript?.dataset.lockAccent === "1";
    if (document.documentElement.dataset.egiTheme) return;
    if (localStorage.getItem("theme.active") !== "1") return;
    var raw = localStorage.getItem("theme.model");
    if (!raw) return;
    var model = JSON.parse(raw);
    if (!model || typeof model !== "object" || !model.tokens) return;
    var names = ["bg", "panel0", "panel", "panel2", "fg", "muted", "info", "ok", "err", "border"];
    if (!lockAccent) names.push("accent");
    for (var name of names) {
      var css = tokenCss(model.tokens[name]);
      if (css) document.documentElement.style.setProperty("--color-" + name, css);
    }
  } catch {
  }
})();

export function getHash() {
  return location.hash.replace(/^#/, "");
}

export function setHash(h) {
  const url = h ? "#" + h : location.pathname + location.search;
  history.replaceState(null, "", url);
}

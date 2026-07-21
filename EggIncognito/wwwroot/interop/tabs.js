export function getHash() {
  return location.hash.replace(/^#/, "");
}

export function setHash(h) {
  history.replaceState(null, "", "#" + h);
}

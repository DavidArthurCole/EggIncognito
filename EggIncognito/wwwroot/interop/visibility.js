export function isVisible() {
  return document.visibilityState === "visible";
}

export function listen(ref) {
  const handler = () => ref.invokeMethodAsync("OnVisibilityChanged", document.visibilityState === "visible");
  document.addEventListener("visibilitychange", handler);
  return {
    dispose: () => document.removeEventListener("visibilitychange", handler)
  };
}

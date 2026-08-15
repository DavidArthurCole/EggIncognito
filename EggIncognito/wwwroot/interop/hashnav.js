export function read() {
    return location.hash || "";
}

export function write(hash) {
    const url = location.pathname + location.search + (hash ? "#" + hash : "");
    history.replaceState(null, "", url);
}

export function replacePath(path, hash) {
    const url = path + location.search + (hash ? "#" + hash : "");
    history.replaceState(null, "", url);
}

export function push(hash) {
    const url = location.pathname + location.search + (hash ? "#" + hash : "");
    history.pushState(null, "", url);
}

export function listen(ref) {
    const handler = () => ref.invokeMethodAsync("OnHashChanged", location.hash || "");
    window.addEventListener("hashchange", handler);
    window.addEventListener("popstate", handler);
    return {
        dispose: () => {
            window.removeEventListener("hashchange", handler);
            window.removeEventListener("popstate", handler);
        }
    };
}

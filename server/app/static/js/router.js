// Mini-routeur history API : interception des liens [data-link], cleanup entre vues.

const routes = [];
let cleanup = null;
let topbarRenderer = null;
let lastLocation = null;

export function route(pattern, handler) {
    routes.push({ pattern, handler });
}

export function setTopbarRenderer(fn) {
    topbarRenderer = fn;
}

export function onCleanup(fn) {
    cleanup = fn;
}

export function navigate(path) {
    if (path === location.pathname + location.search) return;
    history.pushState(null, '', path);
    dispatch();
}

export function replace(path) {
    history.replaceState(null, '', path);
    dispatch();
}

export function setQuery(updates) {
    const params = new URLSearchParams(location.search);
    for (const [key, value] of Object.entries(updates)) {
        if (value == null || value === '' || value === false) params.delete(key);
        else params.set(key, String(value));
    }
    const qs = params.toString();
    navigate(location.pathname + (qs ? `?${qs}` : ''));
}

async function dispatch() {
    if (cleanup) {
        try { cleanup(); } catch { /* ignore */ }
        cleanup = null;
    }
    const path = location.pathname;
    const query = new URLSearchParams(location.search);
    const currentLocation = path + location.search;
    const scrolled = currentLocation !== lastLocation;
    lastLocation = currentLocation;
    for (const { pattern, handler } of routes) {
        const match = path.match(pattern);
        if (match) {
            if (topbarRenderer) topbarRenderer(path);
            await handler(document.getElementById('view'), match.slice(1), query);
            if (scrolled) window.scrollTo(0, 0);
            return;
        }
    }
    if (topbarRenderer) topbarRenderer(path);
    const { notFoundView } = await import('./views/notfound.js');
    notFoundView(document.getElementById('view'));
}

export function startRouter() {
    window.addEventListener('popstate', dispatch);
    document.addEventListener('click', (event) => {
        const anchor = event.target.closest('a[data-link]');
        if (!anchor) return;
        const href = anchor.getAttribute('href');
        if (!href || !href.startsWith('/')) return;
        event.preventDefault();
        navigate(href);
    });
    dispatch();
}

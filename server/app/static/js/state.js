// État partagé : session courante + tenants visibles (pour le switcher de la topbar).

import { get } from './api.js';

export const state = {
    me: null,
    meLoaded: false,
    tenants: null,
};

export async function loadMe(force = false) {
    if (state.meLoaded && !force) return state.me;
    state.me = await get('/api/v1/auth/me');
    state.meLoaded = true;
    return state.me;
}

export async function loadTenants(force = false) {
    if (state.tenants && !force) return state.tenants;
    const data = await get('/api/v1/tenants');
    state.tenants = data.tenants || [];
    return state.tenants;
}

export function invalidateTenants() {
    state.tenants = null;
}

export function isAdmin() {
    return state.me?.user?.role === 'admin';
}

export function isAuthenticated() {
    return !!state.me?.authenticated;
}

// --- Flux live : un EventSource global, les vues s'abonnent ----------

let eventSource = null;
const changeListeners = new Set();

export function subscribeChanges(fn) {
    changeListeners.add(fn);
    if (!eventSource && typeof EventSource !== 'undefined') {
        eventSource = new EventSource('/api/v1/stream');
        eventSource.onmessage = () => {
            changeListeners.forEach((listener) => {
                try { listener(); } catch { /* ignore */ }
            });
        };
    }
    return () => changeListeners.delete(fn);
}

// Client API JSON — cookies de session same-origin, erreurs homogènes {ok, message}.

export class ApiError extends Error {
    constructor(message, status) {
        super(message);
        this.status = status;
    }
}

export async function api(path, { method = 'GET', body } = {}) {
    let res;
    try {
        res = await fetch(path, {
            method,
            credentials: 'same-origin',
            headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
            body: body !== undefined ? JSON.stringify(body) : undefined,
        });
    } catch {
        throw new ApiError('Serveur injoignable.', 0);
    }
    let data = null;
    try {
        data = await res.json();
    } catch {
        // réponse non-JSON : traité comme erreur générique ci-dessous
    }
    if (!res.ok) throw new ApiError((data && data.message) || `Erreur ${res.status}`, res.status);
    return data;
}

export const get = (path) => api(path);
export const post = (path, body = {}) => api(path, { method: 'POST', body });
export const patch = (path, body = {}) => api(path, { method: 'PATCH', body });
export const del = (path) => api(path, { method: 'DELETE' });

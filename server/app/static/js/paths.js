// Chemins URL friendly (slug tenant).

export function tenantPath(tenant) {
    if (!tenant) return '/';
    return `/t/${tenant.slug || tenant.id}`;
}

export function recentPath(tenant) {
    return `${tenantPath(tenant)}/recent`;
}

export function championshipPath(tenant) {
    return `${tenantPath(tenant)}/championship`;
}

export function pilotPath(tenant, pseudo) {
    const name = (pseudo || '').trim();
    return `${tenantPath(tenant)}/pilot/${encodeURIComponent(name)}`;
}

/** @returns {(name: string) => string|null} */
export function makePilotHref(tenant, linkedSet) {
    return (name) => {
        const n = (name || '').trim();
        if (!n || n === '—' || !linkedSet?.has(n.toLowerCase())) return null;
        return pilotPath(tenant, n);
    };
}

export function tenantKeyFromPath(path) {
    return path.match(/^\/t\/([\w-]+)/)?.[1] || null;
}

export function findTenantByKey(tenants, key) {
    if (!key) return null;
    return tenants.find((t) => t.slug === key || t.id === key) || null;
}

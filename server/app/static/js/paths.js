// Chemins URL friendly (slug tenant).

export const ALL_TENANT_KEY = 'all';

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

export function versusPath(tenant, a = null, b = null) {
    const base = `${tenantPath(tenant)}/versus`;
    const params = new URLSearchParams();
    if (a) params.set('a', a);
    if (b) params.set('b', b);
    const qs = params.toString();
    return qs ? `${base}?${qs}` : base;
}

export function pilotPath(tenant, pseudo) {
    const name = (pseudo || '').trim();
    return `${tenantPath(tenant)}/pilot/${encodeURIComponent(name)}`;
}

/** Lien profil pour tout pseudo non vide (compte lié ou non). */
export function makePilotHref(tenant) {
    return (name) => {
        const n = (name || '').trim();
        if (!n || n === '—') return null;
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

export function isAllTenant(tenant) {
    return !!(tenant && (tenant.id === ALL_TENANT_KEY || tenant.slug === ALL_TENANT_KEY || tenant.isAggregate));
}

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

export function tenantKeyFromPath(path) {
    return path.match(/^\/t\/([\w-]+)/)?.[1] || null;
}

export function findTenantByKey(tenants, key) {
    if (!key) return null;
    return tenants.find((t) => t.slug === key || t.id === key) || null;
}

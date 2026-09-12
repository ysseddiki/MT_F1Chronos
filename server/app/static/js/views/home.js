// Accueil : cartes des organisations visibles.

import { h, clear } from '../dom.js';
import { loadTenants, state } from '../state.js';
import { visibilityBadge } from '../components.js';
import { replace } from '../router.js';
import { tenantPath, isAllTenant } from '../paths.js';

export async function homeView(container) {
    clear(container);
    container.append(h('p', { class: 'loading' }, 'Chargement…'));

    let tenants;
    try {
        tenants = await loadTenants(true);
    } catch (err) {
        clear(container);
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }

    const realOrgs = tenants.filter((t) => !isAllTenant(t));
    const allTenant = tenants.find((t) => isAllTenant(t));

    if (realOrgs.length === 1 && !allTenant) {
        replace(tenantPath(realOrgs[0]));
        return;
    }
    if (allTenant && realOrgs.length >= 2) {
        replace(tenantPath(allTenant));
        return;
    }
    if (realOrgs.length === 1) {
        replace(tenantPath(realOrgs[0]));
        return;
    }

    clear(container);
    document.title = 'Organisations — F1 Chronos';

    container.append(
        h('div', { class: 'page-head' },
            h('div', { class: 'titles' },
                h('p', { class: 'kicker' }, 'Serveur de résultats'),
                h('h1', {}, 'Organisations'),
                h('p', { class: 'lede' }, 'Chaque organisation regroupe un ou plusieurs simulateurs.'),
            ),
        ),
    );

    if (!realOrgs.length) {
        const hint = state.me?.publicAccess === false && !state.me?.authenticated
            ? 'L’accès public est désactivé — connecte-toi pour voir les résultats.'
            : 'Aucune organisation visible. Active la sync dans F1 Chronos ou crée-en une dans l’administration.';
        container.append(h('p', { class: 'lede' }, hint));
        return;
    }

    const cards = [];
    if (allTenant) {
        cards.push(h('a', { class: 'card card-all', href: tenantPath(allTenant), 'data-link': true },
            h('h2', {}, allTenant.label, ' ', visibilityBadge(allTenant.visibility, { aggregate: true })),
            h('p', {},
                h('span', { class: 'count' }, String(allTenant.orgCount ?? realOrgs.length)),
                ` organisation${(allTenant.orgCount ?? realOrgs.length) > 1 ? 's' : ''}`,
                ' · ',
                h('span', { class: 'count' }, String(allTenant.simCount ?? 0)),
                ` simulateur${(allTenant.simCount ?? 0) > 1 ? 's' : ''}`,
            ),
        ));
    }
    cards.push(...realOrgs.map((t) => h('a', { class: 'card', href: tenantPath(t), 'data-link': true },
        h('h2', {}, t.label, ' ', visibilityBadge(t.visibility)),
        h('p', {}, h('span', { class: 'count' }, String(t.simCount ?? 0)), ` simulateur${(t.simCount ?? 0) > 1 ? 's' : ''}`),
    )));

    container.append(h('div', { class: 'grid' }, cards));
}

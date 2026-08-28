// Accueil : cartes des organisations visibles.

import { h, clear } from '../dom.js';
import { loadTenants, state } from '../state.js';
import { visibilityBadge } from '../components.js';
import { replace } from '../router.js';

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

    if (tenants.length === 1) {
        replace(`/t/${tenants[0].id}`);
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

    if (!tenants.length) {
        const hint = state.me?.publicAccess === false && !state.me?.authenticated
            ? 'L’accès public est désactivé — connecte-toi pour voir les résultats.'
            : 'Aucune organisation visible. Active la sync dans F1 Chronos ou crée-en une dans l’administration.';
        container.append(h('p', { class: 'lede' }, hint));
        return;
    }

    container.append(h('div', { class: 'grid' },
        tenants.map((t) => h('a', { class: 'card', href: `/t/${t.id}`, 'data-link': true },
            h('h2', {}, t.label, ' ', visibilityBadge(t.visibility)),
            h('p', {}, h('span', { class: 'count' }, String(t.simCount ?? 0)), ` simulateur${(t.simCount ?? 0) > 1 ? 's' : ''}`),
        )),
    ));
}

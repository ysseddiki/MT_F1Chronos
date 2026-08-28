// Index des concours : choix du simulateur puis cartes des concours.

import { h, clear } from '../dom.js';
import { get } from '../api.js';
import { setQuery } from '../router.js';

export async function contestsView(container, _params, query) {
    clear(container);
    container.append(h('p', { class: 'loading' }, 'Chargement…'));

    let sims;
    try {
        ({ sims } = await get('/api/v1/sims'));
    } catch (err) {
        clear(container);
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }

    const current = sims.find((s) => s.id === query.get('sim')) || sims[0] || null;
    let contests = [];
    if (current) {
        try {
            ({ contests } = await get(`/api/v1/sims/${current.id}/contests`));
        } catch { contests = []; }
    }

    clear(container);
    document.title = 'Concours — F1 Chronos';

    container.append(
        h('div', { class: 'page-head' },
            h('div', { class: 'titles' },
                h('p', { class: 'kicker' }, 'Concours'),
                h('h1', {}, 'Concours'),
            ),
        ),
    );

    if (!sims.length) {
        container.append(h('p', { class: 'lede' }, 'Aucun simulateur visible.'));
        return;
    }

    if (sims.length > 1) {
        container.append(h('div', { class: 'chips' },
            sims.map((s) => h('button', {
                class: `chip${current && s.id === current.id ? ' active' : ''}`,
                onclick: () => setQuery({ sim: s.id }),
            }, s.label, s.tenantLabel ? h('span', { class: 'n' }, s.tenantLabel) : null)),
        ));
    }

    if (!contests.length) {
        container.append(h('p', { class: 'lede' }, 'Aucun concours dans l’historique reçu.'));
        return;
    }

    container.append(h('div', { class: 'grid' },
        contests.map((c) => h('a', {
            class: 'card',
            href: `/sim/${current.id}/contests/${c.id}`,
            'data-link': true,
        },
            h('h2', {}, c.name),
            h('p', {}, statusLabel(c.status), ' · ', h('span', { class: 'count' }, String(c.scoreCount ?? 0)), ' chrono(s)'),
        )),
    ));
}

export function statusLabel(status) {
    return { active: 'Actif', draft: 'Brouillon', stopped: 'Terminé' }[status] || status;
}

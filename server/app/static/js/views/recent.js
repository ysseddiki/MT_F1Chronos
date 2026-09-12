// Derniers chronos — log global (admin), accessible depuis la topbar.

import { h, clear } from '../dom.js';
import { get } from '../api.js';
import { recentLapsPanel, banner, visibilityBadge, simToolbarStrip } from '../components.js';
import { onCleanup, replace } from '../router.js';
import { subscribeChanges, mySimulatorPseudo, isAdmin } from '../state.js';
import { boardRowManageMenu } from '../board_manage.js';
import { tenantPath } from '../paths.js';
import { FALLBACK_REFRESH_MS } from './board_page.js';

const RECENT_LAPS_LIMIT = 15;
const LIVE_DEBOUNCE_MS = 1500;

export async function recentView(container, [tenantKey]) {
    clear(container);

    if (!isAdmin()) {
        container.append(h('p', { class: 'lede' }, 'Réservé aux administrateurs.'));
        return;
    }

    let data;
    try {
        data = await get(`/api/v1/tenants/${tenantKey}`);
    } catch (err) {
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }

    const { tenant, sims } = data;
    const canonical = tenant.slug || tenant.id;
    if (canonical !== tenantKey) {
        replace(`/t/${canonical}/recent`);
        return;
    }

    document.title = `Derniers chronos — ${tenant.label} — F1 Chronos`;

    container.append(
        h('div', { class: 'page-head' },
            h('div', { class: 'titles' },
                h('a', { class: 'back-link', href: tenantPath(tenant), 'data-link': true }, `← ${tenant.label}`),
                h('p', { class: 'kicker' }, 'Journal'),
                h('h1', {}, 'Derniers chronos', ' ', visibilityBadge(tenant.visibility)),
                h('p', { class: 'lede' },
                    'Les 15 derniers tours enregistrés, tous circuits confondus.'),
            ),
        ),
    );

    const strip = simToolbarStrip(sims);
    if (strip) container.append(strip);

    const slot = h('div', {}, h('p', { class: 'loading' }, 'Chargement des chronos…'));
    container.append(slot);

    let gen = 0;

    async function load() {
        const g = ++gen;
        try {
            const res = await get(`/api/v1/tenants/${tenant.id}/recent-laps?limit=${RECENT_LAPS_LIMIT}`);
            if (g !== gen) return;
            clear(slot);
            slot.append(recentLapsPanel(res.rows || [], {
                showSim: sims.length > 1,
                highlightName: mySimulatorPseudo() || null,
                manage: (row) => boardRowManageMenu(row, {
                    simId: row.simId,
                    contestId: null,
                    onDone: load,
                }),
            }));
        } catch (err) {
            if (g !== gen) return;
            clear(slot);
            slot.append(banner(err.message || 'Erreur de chargement.', 'error'));
        }
    }

    load();

    const reload = () => { if (!document.hidden) load(); };
    let lastLive = 0;
    const unsub = subscribeChanges(() => {
        const now = Date.now();
        if (now - lastLive < LIVE_DEBOUNCE_MS) return;
        lastLive = now;
        reload();
    });
    const fallback = setInterval(reload, FALLBACK_REFRESH_MS);
    const onVisible = () => { if (!document.hidden) reload(); };
    document.addEventListener('visibilitychange', onVisible);
    onCleanup(() => {
        unsub();
        clearInterval(fallback);
        document.removeEventListener('visibilitychange', onVisible);
    });
}

/** Redirection /recent → org courante ou liste. */
export async function recentIndexView(container) {
    clear(container);
    if (!isAdmin()) {
        container.append(h('p', { class: 'lede' }, 'Réservé aux administrateurs.'));
        return;
    }
    const { loadTenants } = await import('../state.js');
    const { recentPath } = await import('../paths.js');
    let tenants;
    try {
        tenants = await loadTenants(true);
    } catch (err) {
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }
    if (tenants.length === 1) {
        replace(recentPath(tenants[0]));
        return;
    }
    document.title = 'Derniers chronos — F1 Chronos';
    container.append(
        h('p', { class: 'kicker' }, 'Journal'),
        h('h1', {}, 'Derniers chronos'),
        h('p', { class: 'lede' }, 'Choisissez une organisation.'),
        h('div', { class: 'grid' },
            tenants.map((t) => h('a', { class: 'card', href: recentPath(t), 'data-link': true },
                h('h2', {}, t.label),
            )),
        ),
    );
}

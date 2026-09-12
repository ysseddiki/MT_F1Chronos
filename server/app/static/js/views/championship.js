// Expérience (points web) — meilleur tour / pilote / circuit → points P1…Pn.

import { h, clear } from '../dom.js';
import { get } from '../api.js';
import { visibilityBadge } from '../components.js';
import { onCleanup, replace } from '../router.js';
import { subscribeChanges, mySimulatorPseudo, loadLinkedPilots } from '../state.js';
import { tenantPath, championshipPath, makePilotHref } from '../paths.js';
import { FALLBACK_REFRESH_MS } from './board_page.js';

const LIVE_DEBOUNCE_MS = 1500;

export async function championshipView(container, [tenantKey]) {
    clear(container);
    container.append(h('p', { class: 'loading' }, 'Chargement…'));

    let data, champ;
    try {
        [data, champ] = await Promise.all([
            get(`/api/v1/tenants/${tenantKey}`),
            get(`/api/v1/tenants/${tenantKey}/championship`),
        ]);
    } catch (err) {
        clear(container);
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }

    const { tenant } = data;
    const canonical = tenant.slug || tenant.id;
    if (canonical !== tenantKey) {
        replace(championshipPath(tenant));
        return;
    }

    let linked = new Set();
    try { linked = await loadLinkedPilots(tenant.id); } catch { /* ignore */ }
    const pilotHref = makePilotHref(tenant, linked);

    document.title = `Expérience — ${tenant.label} — F1 Chronos`;

    const highlight = (mySimulatorPseudo() || '').trim().toLowerCase();
    const points = champ.pointsByPlace || [];

    clear(container);
    container.append(
        h('div', { class: 'page-head' },
            h('div', { class: 'titles' },
                h('a', { class: 'back-link', href: tenantPath(tenant), 'data-link': true }, `← ${tenant.label}`),
                h('p', { class: 'kicker' }, 'Progression'),
                h('h1', {}, 'Expérience', ' ', visibilityBadge(tenant.visibility)),
                h('p', { class: 'lede' },
                    `Points attribués sur chaque circuit (meilleur tour / pilote). `,
                    `${champ.tracksCounted || 0} circuit${(champ.tracksCounted || 0) > 1 ? 's' : ''} comptabilisé${(champ.tracksCounted || 0) > 1 ? 's' : ''}.`),
            ),
        ),
        h('div', { class: 'panel champ-scale' },
            h('h2', {}, 'Barème'),
            h('p', { class: 'hint' },
                points.length
                    ? `P1→P${points.length} : ${points.join(', ')}`
                    : 'Aucun point configuré.',
                ' — réglable dans Administration → Réglages.'),
        ),
    );

    const slot = h('div', { class: 'champ-table-slot' });
    container.append(slot);
    renderStandings(slot, champ.standings || [], highlight, pilotHref);

    let gen = 0;
    async function reload() {
        if (document.hidden) return;
        const g = ++gen;
        try {
            const next = await get(`/api/v1/tenants/${tenant.id}/championship`);
            if (g !== gen) return;
            const scale = container.querySelector('.champ-scale .hint');
            if (scale) {
                const pts = next.pointsByPlace || [];
                scale.textContent = pts.length
                    ? `P1→P${pts.length} : ${pts.join(', ')} — réglable dans Administration → Réglages.`
                    : 'Aucun point configuré. — réglable dans Administration → Réglages.';
            }
            renderStandings(slot, next.standings || [], highlight, pilotHref);
        } catch {
            /* ignore transient */
        }
    }

    let lastLive = 0;
    const unsub = subscribeChanges(() => {
        const now = Date.now();
        if (now - lastLive < LIVE_DEBOUNCE_MS) return;
        lastLive = now;
        reload();
    });
    const fallback = setInterval(reload, FALLBACK_REFRESH_MS);
    onCleanup(() => {
        unsub();
        clearInterval(fallback);
    });
}

function renderStandings(slot, standings, highlight, pilotHref) {
    clear(slot);
    if (!standings.length) {
        slot.append(h('p', { class: 'lede' }, 'Aucun point pour l’instant — enregistrez des chronos sur au moins un circuit.'));
        return;
    }

    const thead = h('thead', {}, h('tr', {},
        h('th', { class: 'pos champ-primary' }, '#'),
        h('th', { class: 'champ-primary' }, 'Pilote'),
        h('th', { class: 'time champ-primary' }, 'Points'),
        h('th', { class: 'champ-secondary' }, 'Tours'),
        h('th', { class: 'champ-secondary' }, 'Victoires'),
        h('th', { class: 'champ-secondary' }, 'Podiums'),
        h('th', { class: 'champ-secondary' }, 'Circuits'),
    ));

    const body = h('tbody', {},
        standings.map((row) => {
            const me = highlight && (row.name || '').trim().toLowerCase() === highlight;
            const name = (row.name || '').trim();
            const href = pilotHref?.(name) || null;
            const nameNode = href
                ? h('a', { class: 'pilot-link', href, 'data-link': true, title: 'Voir le profil' }, name)
                : name;
            return h('tr', { class: me ? 'row-me' : '' },
                h('td', { class: 'pos champ-primary' }, String(row.rank)),
                h('td', { class: `pilot champ-primary${me ? ' pilot-me' : ''}${href ? ' pilot-linked' : ''}` }, nameNode),
                h('td', { class: 'time champ-primary champ-points' }, String(row.points)),
                h('td', { class: 'champ-secondary' }, String(row.totalLaps ?? 0)),
                h('td', { class: 'champ-secondary' }, String(row.wins)),
                h('td', { class: 'champ-secondary' }, String(row.podiums)),
                h('td', { class: 'champ-secondary' }, String(row.tracks)),
            );
        }),
    );

    slot.append(h('div', { class: 'board-wrap' },
        h('table', { class: 'board championship-table' }, thead, body),
    ));
}

export async function championshipIndexView(container) {
    clear(container);
    const { loadTenants } = await import('../state.js');
    let tenants;
    try {
        tenants = await loadTenants(true);
    } catch (err) {
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }
    if (tenants.length === 1) {
        replace(championshipPath(tenants[0]));
        return;
    }
    document.title = 'Expérience — F1 Chronos';
    container.append(
        h('p', { class: 'kicker' }, 'Progression'),
        h('h1', {}, 'Expérience'),
        h('p', { class: 'lede' }, 'Choisissez une organisation.'),
        !tenants.length
            ? h('p', { class: 'lede' }, 'Aucune organisation visible.')
            : h('div', { class: 'grid' },
                tenants.map((t) => h('a', { class: 'card', href: championshipPath(t), 'data-link': true },
                    h('h2', {}, t.label),
                )),
            ),
    );
}

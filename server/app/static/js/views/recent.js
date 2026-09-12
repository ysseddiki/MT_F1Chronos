// Liste chrono — panneau admin filtrable (circuit, simu, org, pilote, tri).

import { h, clear } from '../dom.js';
import { get } from '../api.js';
import { recentLapsPanel, banner, visibilityBadge, simToolbarStrip } from '../components.js';
import { onCleanup, replace } from '../router.js';
import { subscribeChanges, mySimulatorPseudo, isAdmin } from '../state.js';
import { boardRowManageMenu } from '../board_manage.js';
import { tenantPath, makePilotHref, isAllTenant } from '../paths.js';
import { FALLBACK_REFRESH_MS } from './board_page.js';

const LIST_LIMIT = 100;
const LIVE_DEBOUNCE_MS = 1500;

const SORT_OPTIONS = [
    { value: 'started_at:desc', label: 'Plus récents' },
    { value: 'started_at:asc', label: 'Plus anciens' },
    { value: 'best_lap_ms:asc', label: 'Temps ↑' },
    { value: 'best_lap_ms:desc', label: 'Temps ↓' },
    { value: 'name:asc', label: 'Pilote A → Z' },
    { value: 'track_name:asc', label: 'Circuit A → Z' },
    { value: 'sim_label:asc', label: 'Simulateur A → Z' },
];

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

    const pilotHref = makePilotHref(tenant);
    const aggregate = isAllTenant(tenant);
    const showSim = sims.length > 1;

    document.title = `Liste chrono — ${tenant.label} — F1 Chronos`;

    container.append(
        h('div', { class: 'page-head' },
            h('div', { class: 'titles' },
                h('a', { class: 'back-link', href: tenantPath(tenant), 'data-link': true }, `← ${tenant.label}`),
                h('p', { class: 'kicker' }, 'Administration'),
                h('h1', {}, 'Liste chrono', ' ', visibilityBadge(tenant.visibility, { aggregate })),
                h('p', { class: 'lede' },
                    'Tous les chronos enregistrés — filtrez par circuit, simulateur, organisation ou pilote.'),
            ),
        ),
    );

    const strip = simToolbarStrip(sims);
    if (strip) container.append(strip);

    const filters = {
        trackId: '',
        simulatorId: '',
        orgId: '',
        pilot: '',
        sortKey: 'started_at:desc',
    };

    const filterSlot = h('div', { class: 'chrono-filters panel' });
    const metaSlot = h('p', { class: 'chrono-list-meta muted' });
    const slot = h('div', {}, h('p', { class: 'loading' }, 'Chargement des chronos…'));
    container.append(filterSlot, metaSlot, slot);

    let tracks = [];
    let pilots = [];
    try {
        const [tracksRes, namesRes] = await Promise.all([
            get(`/api/v1/tenants/${tenant.id}/tracks`),
            get(`/api/v1/tenants/${tenant.id}/pilot-names`),
        ]);
        tracks = tracksRes.tracks || [];
        pilots = namesRes.names || [];
    } catch {
        /* filtres partiels OK */
    }

    const orgs = aggregate
        ? uniqueOrgs(sims)
        : [];

    function renderFilters() {
        clear(filterSlot);
        const controls = [];

        controls.push(filterSelect('Circuit', filters.trackId, [
            { value: '', label: 'Tous les circuits' },
            ...tracks.map((t) => ({
                value: String(t.trackId),
                label: t.trackName || `Circuit ${t.trackId}`,
            })),
        ], (v) => { filters.trackId = v; load(); }));

        if (orgs.length > 1) {
            controls.push(filterSelect('Organisation', filters.orgId, [
                { value: '', label: 'Toutes les organisations' },
                ...orgs.map((o) => ({ value: o.id, label: o.label })),
            ], (v) => {
                filters.orgId = v;
                if (filters.simulatorId) {
                    const sim = sims.find((s) => s.id === filters.simulatorId);
                    if (sim && v && sim.tenantId !== v) filters.simulatorId = '';
                }
                renderFilters();
                load();
            }));
        }

        const simOptions = sims.filter((s) => !filters.orgId || s.tenantId === filters.orgId);
        if (sims.length > 1) {
            controls.push(filterSelect('Simulateur', filters.simulatorId, [
                { value: '', label: 'Tous les simulateurs' },
                ...simOptions.map((s) => ({
                    value: s.id,
                    label: aggregate && s.tenantLabel ? `${s.tenantLabel} · ${s.label}` : s.label,
                })),
            ], (v) => { filters.simulatorId = v; load(); }));
        }

        controls.push(filterSelect('Pilote', filters.pilot, [
            { value: '', label: 'Tous les pilotes' },
            ...pilots.map((name) => ({ value: name, label: name })),
        ], (v) => { filters.pilot = v; load(); }));

        controls.push(filterSelect('Tri', filters.sortKey,
            SORT_OPTIONS.map((o) => ({ value: o.value, label: o.label })),
            (v) => { filters.sortKey = v; load(); }));

        filterSlot.append(
            h('div', { class: 'chrono-filters-row' }, ...controls),
        );
    }

    let gen = 0;

    async function load() {
        const g = ++gen;
        const [sort, order] = (filters.sortKey || 'started_at:desc').split(':');
        const qs = new URLSearchParams({ limit: String(LIST_LIMIT), sort, order });
        if (filters.trackId) qs.set('track_id', filters.trackId);
        if (filters.simulatorId) qs.set('simulator_id', filters.simulatorId);
        if (filters.orgId) qs.set('org_id', filters.orgId);
        if (filters.pilot) qs.set('pilot', filters.pilot);

        try {
            const res = await get(`/api/v1/tenants/${tenant.id}/recent-laps?${qs}`);
            if (g !== gen) return;
            const rows = res.rows || [];
            clear(slot);
            metaSlot.textContent = rows.length
                ? `${rows.length} chrono${rows.length > 1 ? 's' : ''} affiché${rows.length > 1 ? 's' : ''}${rows.length >= LIST_LIMIT ? ` (max. ${LIST_LIMIT})` : ''}`
                : '';
            slot.append(recentLapsPanel(rows, {
                showSim,
                highlightName: mySimulatorPseudo() || null,
                pilotHref,
                manage: (row) => boardRowManageMenu(row, {
                    simId: row.simId,
                    contestId: null,
                    onDone: load,
                }),
            }));
        } catch (err) {
            if (g !== gen) return;
            clear(slot);
            metaSlot.textContent = '';
            slot.append(banner(err.message || 'Erreur de chargement.', 'error'));
        }
    }

    renderFilters();
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

function filterSelect(label, value, options, onChange) {
    const sel = h('select', {
        class: 'chrono-filter-select',
        'aria-label': label,
        onchange: () => onChange(sel.value),
    }, options.map((it) => h('option', { value: it.value }, it.label)));
    queueMicrotask(() => { sel.value = value; });
    return h('label', { class: 'chrono-filter' },
        h('span', { class: 'chrono-filter-label' }, label),
        sel,
    );
}

function uniqueOrgs(sims) {
    const map = new Map();
    for (const s of sims || []) {
        if (!s.tenantId) continue;
        if (!map.has(s.tenantId)) {
            map.set(s.tenantId, { id: s.tenantId, label: s.tenantLabel || s.tenantId });
        }
    }
    return [...map.values()].sort((a, b) => a.label.localeCompare(b.label, 'fr'));
}

/** Redirection /recent → org courante ou liste. */
export async function recentIndexView(container) {
    clear(container);
    if (!isAdmin()) {
        container.append(h('p', { class: 'lede' }, 'Réservé aux administrateurs.'));
        return;
    }
    const { loadTenants } = await import('../state.js');
    const { recentPath, isAllTenant } = await import('../paths.js');
    let tenants;
    try {
        tenants = await loadTenants(true);
    } catch (err) {
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }
    const all = tenants.find((t) => isAllTenant(t));
    if (all) {
        replace(recentPath(all));
        return;
    }
    if (tenants.length === 1) {
        replace(recentPath(tenants[0]));
        return;
    }
    document.title = 'Liste chrono — F1 Chronos';
    container.append(
        h('p', { class: 'kicker' }, 'Administration'),
        h('h1', {}, 'Liste chrono'),
        h('p', { class: 'lede' }, 'Choisissez une organisation.'),
        h('div', { class: 'grid' },
            tenants.map((t) => h('a', { class: 'card', href: recentPath(t), 'data-link': true },
                h('h2', {}, t.label),
            )),
        ),
    );
}

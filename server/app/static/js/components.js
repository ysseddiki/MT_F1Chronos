// Composants UI partagés.

import { h, clear, fmtLap, fmtGap, fmtDateTime } from './dom.js';
import { state, isAdmin, isAuthenticated, loadTenants } from './state.js';
import { navigate } from './router.js';
import { post } from './api.js';

// ---------- Topbar ----------

export async function renderTopbar(activePath) {
    const bar = document.getElementById('topbar');
    clear(bar);

    const brand = h('a', { class: 'brand', href: '/', 'data-link': true },
        h('img', { src: '/static/favicon.svg', alt: '' }),
        'F1 CHRONOS',
        h('small', {}, 'résultats'),
    );

    const nav = h('nav', {},
        navLink('/', 'Résultats', activePath === '/' || activePath.startsWith('/t/') || activePath.startsWith('/sim/')),
        navLink('/contests', 'Concours', activePath.startsWith('/contests')),
        isAdmin() ? navLink('/admin', 'Administration', activePath.startsWith('/admin')) : null,
    );

    const right = h('div', { class: 'topbar-right' });

    let tenants = [];
    try { tenants = await loadTenants(); } catch { /* hors-ligne : switcher vide */ }
    const currentTenant = activePath.match(/^\/t\/([\w-]+)/)?.[1];
    if (tenants.length) {
        const select = h('select', {
            'aria-label': 'Choisir une organisation',
            onchange: (e) => {
                const id = e.target.value;
                if (id) navigate(`/t/${id}`);
                else navigate('/');
            },
        },
            h('option', { value: '', selected: !currentTenant }, 'Toutes les organisations'),
            tenants.map((t) => h('option', { value: t.id, selected: t.id === currentTenant }, t.label)),
        );
        right.append(h('div', { class: 'tenant-switch' }, select));
    }

    if (isAuthenticated()) {
        const user = state.me.user;
        right.append(
            h('span', { class: 'user-chip' },
                h('span', { class: `role ${user.role}` }, user.role === 'admin' ? 'Admin' : 'Visiteur'),
                user.email,
            ),
            h('button', {
                class: 'btn-ghost btn-sm',
                onclick: async () => {
                    await post('/api/v1/auth/logout');
                    state.me = null;
                    state.meLoaded = false;
                    state.tenants = null;
                    navigate('/');
                    location.reload();
                },
            }, 'Déconnexion'),
        );
    } else {
        right.append(h('a', { class: 'btn btn-sm', href: '/login', 'data-link': true }, 'Connexion'));
    }

    bar.append(brand, nav, right);
}

function navLink(href, label, active) {
    return h('a', { class: `nav-link${active ? ' active' : ''}`, href, 'data-link': true }, label);
}

// ---------- Présence (pastille, pas bouton) ----------

export function presence(sim) {
    const wrap = h('span', { class: `presence ${sim.connected ? 'on' : 'off'}` },
        h('span', { class: 'dot' }),
        h('span', { class: 'label' }, sim.connected ? 'En ligne' : 'Hors ligne'),
    );
    if (!sim.connected && sim.lastSeenUtc)
        wrap.append(h('span', {}, `· vu le ${fmtDateTime(sim.lastSeenUtc)}`));
    if (sim.connected)
        wrap.append(h('span', {}, `· sync ${sim.syncIntervalSeconds} s`));
    return wrap;
}

export function visibilityBadge(visibility) {
    return visibility === 'private'
        ? h('span', { class: 'badge private' }, 'Privé')
        : h('span', { class: 'badge public' }, 'Public');
}

// ---------- Segmented (switch Tous les tours / Meilleur par joueur) ----------

export function segmented(options, current, onChange) {
    return h('div', { class: 'segmented', role: 'tablist' },
        options.map((opt) => h('button', {
            class: opt.value === current ? 'active' : '',
            role: 'tab',
            'aria-selected': opt.value === current ? 'true' : 'false',
            onclick: () => { if (opt.value !== current) onChange(opt.value); },
        }, opt.label)),
    );
}

// ---------- Chips circuits ----------

export function trackChips(tracks, currentId, onSelect) {
    return h('div', { class: 'chips', role: 'tablist', 'aria-label': 'Circuits' },
        tracks.map((t) => h('button', {
            class: `chip${t.trackId === currentId ? ' active' : ''}`,
            role: 'tab',
            'aria-selected': t.trackId === currentId ? 'true' : 'false',
            onclick: () => onSelect(t.trackId),
        }, t.trackName, h('span', { class: 'n' }, String(t.scoreCount)))),
    );
}

// ---------- Tableau de classement ----------

export function boardTable(rows, { showSim = false, manage = null } = {}) {
    const leaderMs = rows.length ? rows[0].bestLapMs : 0;
    const cols = 4 + (showSim ? 1 : 0) + (manage ? 1 : 0);

    const thead = h('thead', {}, h('tr', {},
        h('th', {}, '#'),
        h('th', {}, 'Pilote'),
        showSim ? h('th', {}, 'Simu') : null,
        h('th', { class: 'time' }, 'Temps'),
        h('th', { class: 'gap' }, 'Écart'),
        manage ? h('th', {}) : null,
    ));

    const body = h('tbody', {},
        rows.length ? rows.map((row) => {
            const cells = [
                h('td', {}, h('span', { class: `rank${row.rank <= 3 ? ` p${row.rank}` : ''}` }, `P${row.rank}`)),
                h('td', { class: 'pilot' }, row.name),
                showSim ? h('td', { class: 'sim-tag' }, row.simLabel || '—') : null,
                h('td', { class: 'time' }, row.formatted || fmtLap(row.bestLapMs)),
                h('td', { class: 'gap' }, fmtGap(row.bestLapMs - leaderMs)),
            ];
            if (manage) cells.push(h('td', {}, h('div', { class: 'row-actions' }, manage(row))));
            return h('tr', {}, cells);
        }) : h('tr', { class: 'empty-row' }, h('td', { colspan: String(cols) }, 'Aucun chrono.')),
    );

    return h('div', { class: 'board-wrap' }, h('table', { class: 'board' }, thead, body));
}

// ---------- Pagination ----------

export function pagination(board, onPage) {
    if (!board || board.pages <= 1) return null;
    return h('div', { class: 'pagination' },
        h('span', { class: 'info' }, `${board.total} chrono${board.total > 1 ? 's' : ''} · ${board.pageSize} par page`),
        h('div', { class: 'pages' },
            h('button', { class: 'btn-sm', disabled: board.page <= 1, onclick: () => onPage(board.page - 1) }, '← Précédent'),
            h('span', { class: 'current' }, `Page ${board.page} / ${board.pages}`),
            h('button', { class: 'btn-sm', disabled: board.page >= board.pages, onclick: () => onPage(board.page + 1) }, 'Suivant →'),
        ),
    );
}

// ---------- Bannières & toasts ----------

export function banner(message, type = '') {
    return h('div', { class: `banner ${type}`.trim(), role: 'status' }, message);
}

export function toast(message, type = '') {
    const el = h('div', { class: `toast ${type}`.trim() }, message);
    document.getElementById('toasts').append(el);
    setTimeout(() => el.remove(), 4500);
}

// ---------- Modales ----------

export function openModal(title, content, actions = [], { onClose } = {}) {
    const overlay = h('div', { class: 'modal-overlay' });
    let closed = false;
    const close = () => {
        if (closed) return;
        closed = true;
        overlay.remove();
        document.removeEventListener('keydown', onKey);
        if (onClose) onClose();
    };
    overlay.addEventListener('click', (e) => { if (e.target === overlay) close(); });
    const onKey = (e) => { if (e.key === 'Escape') close(); };
    document.addEventListener('keydown', onKey);

    const modal = h('div', { class: 'modal', role: 'dialog', 'aria-modal': 'true' },
        h('h2', {}, title),
        content,
        actions.length ? h('div', { class: 'modal-actions' },
            actions.map((a) => h('button', {
                class: a.class || '',
                onclick: () => a.onClick(close),
            }, a.label)),
        ) : null,
    );
    overlay.append(modal);
    document.body.append(overlay);
    return { close };
}

export function promptDialog(title, { label = '', value = '', placeholder = '', maxlength = 60, confirmLabel = 'Valider' } = {}) {
    return new Promise((resolve) => {
        let settled = false;
        const input = h('input', { type: 'text', value, placeholder, maxlength: String(maxlength) });
        const done = (closeFn, ok) => {
            if (settled) return;
            settled = true;
            closeFn();
            resolve(ok ? input.value.trim() : null);
        };
        const form = h('form', {
            onsubmit: (e) => { e.preventDefault(); done(modal.close, true); },
        }, label ? h('div', { class: 'field' }, h('label', {}, label), input) : input);
        const modal = openModal(title, form, [
            { label: 'Annuler', onClick: (c) => done(c, false) },
            { label: confirmLabel, class: 'btn-primary', onClick: (c) => done(c, true) },
        ], { onClose: () => { if (!settled) { settled = true; resolve(null); } } });
        input.focus();
        input.select();
    });
}

export function confirmDialog(message, { title = 'Confirmation', confirmLabel = 'Confirmer', danger = false } = {}) {
    return new Promise((resolve) => {
        let settled = false;
        const done = (closeFn, ok) => {
            if (settled) return;
            settled = true;
            closeFn();
            resolve(ok);
        };
        const modal = openModal(title,
            h('p', { class: 'muted' }, message),
            [
                { label: 'Annuler', onClick: (c) => done(c, false) },
                { label: confirmLabel, class: danger ? 'btn-danger' : 'btn-primary', onClick: (c) => done(c, true) },
            ],
            { onClose: () => { if (!settled) { settled = true; resolve(false); } } },
        );
        void modal;
    });
}

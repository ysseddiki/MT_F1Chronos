// Composants UI partagés.

import { h, clear, fmtLap, fmtGap, fmtDateTime } from './dom.js';
import { state, isAdmin, isAuthenticated, isSimRacer, mySimulatorPseudo, loadTenants } from './state.js';
import { navigate } from './router.js';
import { post } from './api.js';
import { tenantPath, tenantKeyFromPath, findTenantByKey } from './paths.js';

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
        isAdmin() ? navLink('/admin', 'Administration', activePath.startsWith('/admin')) : null,
    );

    const right = h('div', { class: 'topbar-right' });

    let tenants = [];
    try { tenants = await loadTenants(); } catch { /* hors-ligne : switcher vide */ }
    const pathKey = tenantKeyFromPath(activePath);
    const currentTenant = findTenantByKey(tenants, pathKey);
    if (tenants.length) {
        const select = h('select', {
            'aria-label': 'Choisir une organisation',
            onchange: (e) => {
                const key = e.target.value;
                if (!key) navigate('/');
                else {
                    const t = tenants.find((x) => (x.slug || x.id) === key);
                    navigate(t ? tenantPath(t) : `/t/${key}`);
                }
            },
        },
            h('option', { value: '', selected: !currentTenant }, 'Toutes les organisations'),
            tenants.map((t) => h('option', {
                value: t.slug || t.id,
                selected: currentTenant?.id === t.id,
            }, t.label)),
        );
        right.append(h('div', { class: 'tenant-switch' }, select));
    }

    if (isAuthenticated()) {
        const user = state.me.user;
        const roleLabel = user.role === 'admin' ? 'Admin'
            : user.role === 'simracer' ? 'SimRacer'
                : 'Visiteur';
        const authActions = [
            isSimRacer()
                ? h('a', { class: 'btn-ghost btn-sm', href: '/profile', 'data-link': true }, 'Profil')
                : null,
            h('span', { class: 'user-chip' },
                h('span', { class: `role ${user.role}` }, roleLabel),
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
        ].filter(Boolean);
        right.append(...authActions);
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

// ---------- Pseudo de session (admin) ----------

const PENCIL_SVG = '<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M17 3a2.8 2.8 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z"/></svg>';

export function pencilIcon() {
    const span = h('span', { class: 'icon', 'aria-hidden': 'true' });
    span.innerHTML = PENCIL_SVG; // SVG statique interne, aucune donnée utilisateur
    return span;
}

export function sessionPseudoEditor(sim) {
    if (!isAdmin()) return null;
    return h('button', {
        class: 'btn-ghost btn-sm',
        type: 'button',
        title: 'Changer le pseudo de la session en cours sur ce simulateur',
        onclick: async () => {
            const name = await promptDialog('Pseudo de la session en cours', {
                label: `Nouveau pseudo pour « ${sim.label} » (appliqué par le simu à sa prochaine sync)`,
                value: sim.playerName || '',
                maxlength: 20,
            });
            if (!name) return;
            try {
                const res = await post(`/api/v1/admin/simulators/${sim.id}/player-name`, { new_name: name });
                toast(res.message, 'success');
            } catch (err) {
                toast(err.message, 'error');
            }
        },
    }, `Session : ${sim.playerName || '—'}`, pencilIcon());
}

export function sessionPlayerLabel(sim) {
    return h('span', { class: 'session-pseudo muted' }, `Session : ${sim.playerName || '—'}`);
}

export function applyMyPseudoButton(sim, pseudo) {
    const name = (pseudo || mySimulatorPseudo()).trim();
    if (!name) return null;
    return h('button', {
        class: 'btn-accent btn-sm',
        type: 'button',
        title: `Remplacer la session en cours par votre pseudo de profil`,
        onclick: async () => {
            const sessionName = (sim.playerName || '').trim();
            const detail = sessionName && sessionName.toLowerCase() !== name.toLowerCase()
                ? `La session affiche actuellement « ${sessionName} ». `
                : '';
            if (!await confirmDialog(
                `${detail}Appliquer votre pseudo de profil « ${name} » sur « ${sim.label} » ? Le simulateur l’adoptera à sa prochaine sync.`,
                { confirmLabel: 'Appliquer' },
            )) return;
            try {
                const res = await post(`/api/v1/sims/${sim.id}/apply-my-pseudo`);
                toast(res.message, 'success');
            } catch (err) {
                toast(err.message, 'error');
            }
        },
    }, 'Appliquer mon pseudo');
}

/** Contrôles pseudo session : admin (libre) + SimRacer (profil + appliquer). */
export function simPseudoControls(sim) {
    const parts = [];
    if (isAdmin()) {
        parts.push(sessionPseudoEditor(sim));
    } else if (isSimRacer()) {
        parts.push(sessionPlayerLabel(sim));
        const pseudo = mySimulatorPseudo();
        if (pseudo) {
            parts.push(
                h('a', {
                    class: 'btn-ghost btn-sm',
                    href: '/profile',
                    'data-link': true,
                    title: 'Modifier votre pseudo de profil (n’affecte pas le simulateur tant que vous n’appliquez pas)',
                }, `Profil : ${pseudo}`, pencilIcon()),
                applyMyPseudoButton(sim, pseudo),
            );
        } else {
            parts.push(h('a', {
                class: 'btn-ghost btn-sm',
                href: '/profile',
                'data-link': true,
            }, 'Configurer mon pseudo'));
        }
    }
    if (!parts.length) return null;
    return h('span', { class: 'sim-pseudo-controls' }, ...parts);
}

/** Bandeau simulateur(s) : présence + contrôles pseudo sur la feuille de temps. */
export function simToolbarStrip(sims, { liveTrackId = null } = {}) {
    if (!sims?.length) return null;
    return h('div', { class: 'sim-toolbar panel' },
        h('h2', {}, sims.length > 1 ? 'Simulateurs' : 'Simulateur'),
        h('div', { class: 'sim-toolbar-list' },
            sims.map((sim) => {
                const onTrack = liveTrackId != null && sim.currentTrackId === liveTrackId;
                return h('div', { class: 'sim-toolbar-item' },
                    h('div', { class: 'sim-toolbar-head' },
                        h('a', {
                            class: 'sim-toolbar-label',
                            href: `/sim/${sim.id}`,
                            'data-link': true,
                        }, sim.label),
                        presence(sim),
                        onTrack ? h('span', { class: 'badge live-track' }, 'En piste ici') : null,
                    ),
                    simPseudoControls(sim)
                        || (sim.playerName ? h('span', { class: 'muted' }, `Session : ${sim.playerName}`) : null),
                );
            }),
        ),
    );
}

/** Menu contextuel compact (⋯) — position fixe pour échapper au overflow des tableaux. */
let openActionMenuClose = null;

function closeAnyOpenActionMenu() {
    if (openActionMenuClose) {
        openActionMenuClose();
        openActionMenuClose = null;
    }
}

export function actionMenu(triggerLabel, items, { title = 'Actions' } = {}) {
    const wrap = h('div', { class: 'action-menu' });
    const btn = h('button', {
        class: 'btn-sm action-menu-trigger',
        type: 'button',
        title,
        'aria-label': title,
        'aria-haspopup': 'true',
        'aria-expanded': 'false',
    }, triggerLabel);
    const list = h('div', { class: 'action-menu-list', role: 'menu' },
        items.map((item) => h('button', {
            class: `action-menu-item${item.danger ? ' danger' : ''}`,
            type: 'button',
            role: 'menuitem',
            onclick: (e) => {
                e.stopPropagation();
                closeMenu();
                item.onClick();
            },
        }, item.label)),
    );

    let onDocClick = null;
    let onScroll = null;
    let onResize = null;

    const placeMenu = () => {
        if (!wrap.classList.contains('open')) return;
        const rect = btn.getBoundingClientRect();
        const listWidth = list.offsetWidth || 190;
        const listHeight = list.offsetHeight || 120;
        let left = rect.right - listWidth;
        left = Math.max(8, Math.min(left, window.innerWidth - listWidth - 8));
        let top = rect.bottom + 4;
        if (top + listHeight > window.innerHeight - 8) {
            top = Math.max(8, rect.top - listHeight - 4);
        }
        list.style.position = 'fixed';
        list.style.top = `${top}px`;
        list.style.left = `${left}px`;
        list.style.right = 'auto';
        list.style.zIndex = '1000';
    };

    const closeMenu = () => {
        wrap.classList.remove('open');
        btn.setAttribute('aria-expanded', 'false');
        list.removeAttribute('style');
        if (onDocClick) document.removeEventListener('click', onDocClick);
        if (onScroll) window.removeEventListener('scroll', onScroll, true);
        if (onResize) window.removeEventListener('resize', onResize);
        onDocClick = onScroll = onResize = null;
        if (openActionMenuClose === closeMenu) openActionMenuClose = null;
    };

    btn.addEventListener('click', (e) => {
        e.stopPropagation();
        if (wrap.classList.contains('open')) {
            closeMenu();
            return;
        }
        closeAnyOpenActionMenu();
        wrap.classList.add('open');
        btn.setAttribute('aria-expanded', 'true');
        list.style.display = 'block';
        placeMenu();
        openActionMenuClose = closeMenu;
        onScroll = () => placeMenu();
        onResize = () => placeMenu();
        onDocClick = (ev) => {
            if (wrap.contains(ev.target)) return;
            closeMenu();
        };
        setTimeout(() => {
            document.addEventListener('click', onDocClick);
            window.addEventListener('scroll', onScroll, true);
            window.addEventListener('resize', onResize);
        }, 0);
    });

    wrap.append(btn, list);
    return wrap;
}

export function contestStatusLabel(status) {
    return { active: 'Actif', draft: 'Brouillon', stopped: 'Terminé' }[status] || status;
}

/** Sélecteur global / concours sur la page simulateur. */
export function contestBoardSelect(contests, currentContestId, onChange) {
    if (!contests?.length) return null;
    const select = h('select', {
        class: 'board-scope-select',
        'aria-label': 'Tableau affiché',
        onchange: (e) => onChange(e.target.value || null),
    },
        h('option', { value: '', selected: !currentContestId }, 'Classement global'),
        contests.map((c) => h('option', {
            value: c.id,
            selected: c.id === currentContestId,
        }, `${c.name} · ${contestStatusLabel(c.status)}`)),
    );
    return h('div', { class: 'board-scope panel' },
        h('label', { class: 'board-scope-label' }, 'Tableau'),
        select,
    );
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

// ---------- Sélecteur circuit (liste déroulante) ----------

export function trackSelect(tracks, currentId, onChange, { liveTrackId = null } = {}) {
    const current = tracks.find((t) => t.trackId === currentId);
    const label = (t) => (t.trackName || `Circuit ${t.trackId}`).trim();
    const select = h('select', {
        class: 'track-select',
        'aria-label': 'Circuit',
        onchange: (e) => onChange(Number(e.target.value)),
    },
        tracks.map((t) => {
            const live = liveTrackId != null && t.trackId === liveTrackId;
            const count = t.scoreCount != null ? ` (${t.scoreCount})` : '';
            return h('option', {
                value: String(t.trackId),
                selected: t.trackId === currentId,
            }, `${label(t)}${count}${live ? ' · en piste' : ''}`);
        }),
    );
    return h('div', { class: 'track-picker' },
        h('label', { class: 'track-picker-label' }, 'Circuit'),
        select,
        current
            ? h('p', { class: 'track-picker-current' },
                'Affiché : ',
                h('strong', {}, label(current)),
                liveTrackId != null && current.trackId === liveTrackId
                    ? h('span', { class: 'badge live-track' }, 'En piste')
                    : null,
            )
            : null,
    );
}

/** @deprecated Utiliser trackSelect — conservé pour compat interne admin si besoin */
export function trackChips(tracks, currentId, onSelect) {
    return trackSelect(tracks, currentId, onSelect);
}

// ---------- Tableau de classement ----------

export function boardTable(rows, { showSim = false, manage = null, highlightName = null } = {}) {
    const leaderMs = rows.length ? rows[0].bestLapMs : 0;
    const cols = 4 + (showSim ? 1 : 0) + (manage ? 1 : 0);
    const highlight = (highlightName || '').trim();
    const isMe = (name) => highlight
        && (name || '').trim().toLowerCase() === highlight.toLowerCase();

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
            const me = isMe(row.name);
            const cells = [
                h('td', {}, h('span', { class: `rank${row.rank <= 3 ? ` p${row.rank}` : ''}` }, `P${row.rank}`)),
                h('td', { class: `pilot${me ? ' pilot-me' : ''}` }, (row.name || '').trim() || '—'),
                showSim ? h('td', { class: 'sim-tag' }, row.simLabel || '—') : null,
                h('td', { class: 'time' }, row.formatted || fmtLap(row.bestLapMs)),
                h('td', { class: 'gap' }, fmtGap(row.bestLapMs - leaderMs)),
            ];
            if (manage) cells.push(h('td', {}, h('div', { class: 'row-actions' }, manage(row))));
            return h('tr', { class: me ? 'row-me' : '' }, cells);
        }) : h('tr', { class: 'empty-row' }, h('td', { colspan: String(cols) }, 'Aucun chrono.')),
    );

    return h('div', { class: 'board-wrap' }, h('table', { class: 'board' }, thead, body));
}

/** Derniers chronos enregistrés (tous circuits), triés par horodatage décroissant. */
export function recentLapsPanel(rows, { showSim = false, manage = null, highlightName = null } = {}) {
    if (!rows?.length) {
        return h('p', { class: 'lede' }, 'Aucun chrono enregistré pour l’instant.');
    }
    const highlight = (highlightName || '').trim();
    const isMe = (name) => highlight
        && (name || '').trim().toLowerCase() === highlight.toLowerCase();

    const thead = h('thead', {}, h('tr', {},
        h('th', { class: 'recent-when' }, 'Enregistré'),
        h('th', {}, 'Pilote'),
        h('th', {}, 'Circuit'),
        showSim ? h('th', {}, 'Simu') : null,
        h('th', { class: 'time' }, 'Temps'),
        manage ? h('th', {}) : null,
    ));

    const body = h('tbody', {},
        rows.map((row) => {
            const me = isMe(row.name);
            const cells = [
                h('td', { class: 'recent-when muted' }, fmtDateTime(row.startedAt)),
                h('td', { class: `pilot${me ? ' pilot-me' : ''}` }, (row.name || '').trim() || '—'),
                h('td', {}, (row.trackName || '').trim() || '—'),
                showSim ? h('td', { class: 'sim-tag' }, row.simLabel || '—') : null,
                h('td', { class: 'time' }, row.formatted || fmtLap(row.bestLapMs)),
            ];
            if (manage) cells.push(h('td', {}, h('div', { class: 'row-actions' }, manage(row))));
            return h('tr', { class: me ? 'row-me' : '' }, cells);
        }),
    );

    return h('div', { class: 'board-wrap recent-laps-wrap' },
        h('table', { class: 'board recent-laps-table' }, thead, body),
    );
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

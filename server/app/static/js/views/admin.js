// Administration — shell à onglets + onglet Simulateurs (gestion des chronos et des jobs).

import { h, clear, fmtDateTime } from '../dom.js';
import { get, post, patch, del } from '../api.js';
import { loadMe, isAdmin, invalidateTenants } from '../state.js';
import { navigate, setQuery, replace } from '../router.js';
import {
    presence, trackSelect, boardTable, pagination, toast,
    openModal, confirmDialog, promptDialog,
} from '../components.js';
import { tenantsTab, usersTab } from './admin_org.js';
import { settingsTab } from './admin_settings.js';

const TABS = [
    { id: 'sims', label: 'Simulateurs' },
    { id: 'tenants', label: 'Organisations' },
    { id: 'users', label: 'Utilisateurs' },
    { id: 'settings', label: 'Réglages' },
];

export async function adminView(container, _params, query) {
    clear(container);
    container.append(h('p', { class: 'loading' }, 'Chargement…'));

    await loadMe(true);
    if (!isAdmin()) {
        navigate('/login');
        return;
    }

    const tab = query.get('tab') || 'sims';
    clear(container);
    document.title = 'Administration — F1 Chronos';

    container.append(
        h('div', { class: 'page-head' },
            h('div', { class: 'titles' },
                h('p', { class: 'kicker' }, 'Administration'),
                h('h1', {}, 'Gérer le serveur'),
            ),
        ),
        h('div', { class: 'tabs', role: 'tablist' },
            TABS.map((t) => h('button', {
                class: `tab${tab === t.id ? ' active' : ''}`,
                role: 'tab',
                'aria-selected': tab === t.id ? 'true' : 'false',
                onclick: () => setQuery({ tab: t.id === 'sims' ? null : t.id, sim: null, contest: null, atrack: null, apage: null }),
            }, t.label)),
        ),
    );

    const slot = h('div', {});
    container.append(slot);

    try {
        if (tab === 'tenants') await tenantsTab(slot);
        else if (tab === 'users') await usersTab(slot);
        else if (tab === 'settings') await settingsTab(slot);
        else await simsTab(slot, query);
    } catch (err) {
        clear(slot);
        slot.append(h('div', { class: 'banner error' }, err.message));
    }
}

function refresh() {
    replace(location.pathname + location.search);
}

// ---------------------------------------------------------------------------
// Onglet Simulateurs
// ---------------------------------------------------------------------------

async function simsTab(slot, query) {
    const overview = await get('/api/v1/admin/overview');
    clear(slot);

    slot.append(createSimPanel(overview.tenants));

    if (!overview.sims.length) {
        slot.append(h('p', { class: 'lede' }, 'Aucun simulateur. Crée le premier ci-dessus.'));
        return;
    }

    const tenantLabel = (id) => overview.tenants.find((t) => t.id === id)?.label || '—';

    slot.append(
        h('div', { class: 'panel' },
            h('h2', {}, 'Simulateurs'),
            h('table', { class: 'admin-table' },
                h('thead', {}, h('tr', {},
                    h('th', {}, 'Nom'),
                    h('th', {}, 'Présence'),
                    h('th', {}, 'Organisation'),
                    h('th', {}, 'Pseudo session'),
                    h('th', {}),
                )),
                h('tbody', {},
                    overview.sims.map((s) => h('tr', {},
                        h('td', {}, h('a', { href: `/admin?sim=${s.id}`, 'data-link': true, class: 'pilot' }, s.label)),
                        h('td', {}, presence(s)),
                        h('td', { class: 'muted' }, tenantLabel(s.tenantId)),
                        h('td', { class: 'muted' }, s.playerName || '—'),
                        h('td', {}, h('div', { class: 'row-actions' },
                            h('a', { class: 'btn-sm btn', href: `/admin?sim=${s.id}`, 'data-link': true }, 'Gérer'),
                        )),
                    )),
                ),
            ),
        ),
    );

    const selected = overview.sims.find((s) => s.id === query.get('sim')) || null;
    if (selected) slot.append(await simDetail(selected, overview.tenants, query));
}

function createSimPanel(tenants) {
    const label = h('input', { type: 'text', maxlength: '40', placeholder: 'Box 1' });
    const tenantSelect = h('select', {},
        h('option', { value: '' }, '— Nouvelle organisation —'),
        tenants.map((t) => h('option', { value: t.id }, t.label)),
    );

    return h('form', {
        class: 'panel',
        onsubmit: async (e) => {
            e.preventDefault();
            try {
                const data = await post('/api/v1/admin/simulators', {
                    label: label.value,
                    tenant_id: tenantSelect.value,
                });
                invalidateTenants();
                tokenModal(data.token, `Simulateur « ${data.sim.label} » créé.`);
                refresh();
            } catch (err) {
                toast(err.message, 'error');
            }
        },
    },
        h('h2', {}, 'Nouveau simulateur'),
        h('div', { class: 'form-row' },
            h('div', { class: 'field' }, h('label', {}, 'Nom du simulateur'), label),
            h('div', { class: 'field' },
                h('label', {}, 'Organisation'),
                tenantSelect,
                h('p', { class: 'hint' }, '« Nouvelle organisation » crée une organisation dédiée.'),
            ),
        ),
        h('button', { type: 'submit', class: 'btn-primary' }, 'Créer et générer un jeton'),
    );
}

function tokenModal(token, title) {
    const box = h('div', { class: 'token-box' }, token);
    openModal(title, [
        h('p', { class: 'muted' }, 'Copie ce jeton maintenant dans l’admin F1 Chronos — il ne sera plus affiché. Laisse le champ jeton vide côté simu pour l’auto-enregistrement.'),
        box,
    ], [
        {
            label: 'Copier le jeton',
            class: 'btn-primary',
            onClick: async () => {
                try {
                    await navigator.clipboard.writeText(token);
                    toast('Jeton copié.', 'success');
                } catch {
                    box.focus?.();
                    toast('Copie manuelle : sélectionne le texte du jeton.', 'error');
                }
            },
        },
        { label: 'Fermer', onClick: (c) => c() },
    ]);
}

async function simDetail(sim, tenants, query) {
    const wrap = h('div', {});

    // --- fiche simu : renommer, déplacer, pseudo session, jeton, supprimer ---
    const tenantSelect = h('select', { style: 'max-width:260px' },
        tenants.map((t) => h('option', { value: t.id, selected: t.id === sim.tenantId }, t.label)),
    );
    tenantSelect.addEventListener('change', async () => {
        try {
            await patch(`/api/v1/admin/simulators/${sim.id}`, { tenant_id: tenantSelect.value });
            invalidateTenants();
            toast('Simulateur déplacé.', 'success');
            refresh();
        } catch (err) {
            toast(err.message, 'error');
        }
    });

    wrap.append(h('div', { class: 'panel' },
        h('h2', {}, `Simulateur « ${sim.label} »`),
        h('div', { class: 'flex', style: 'margin-bottom:12px' }, presence(sim)),
        h('div', { class: 'form-row' },
            h('div', { class: 'field' },
                h('label', {}, 'Organisation'),
                tenantSelect,
            ),
            h('div', { class: 'field' },
                h('label', {}, 'Pseudo de la session en cours'),
                h('div', { class: 'flex' },
                    h('span', { class: 'muted' }, sim.playerName || '—'),
                    h('button', {
                        class: 'btn-sm',
                        type: 'button',
                        onclick: async () => {
                            const name = await promptDialog('Pseudo de la session', {
                                label: 'Nouveau pseudo (appliqué par le simu à sa prochaine sync)',
                                value: sim.playerName,
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
                    }, 'Définir'),
                ),
                h('p', { class: 'hint' }, 'Le simu reste maître : un job lui est envoyé (pull à la prochaine sync).'),
            ),
        ),
        h('div', { class: 'flex' },
            h('button', {
                class: 'btn-sm', type: 'button',
                onclick: async () => {
                    const label = await promptDialog('Renommer le simulateur', { value: sim.label, maxlength: 40 });
                    if (!label) return;
                    try {
                        await patch(`/api/v1/admin/simulators/${sim.id}`, { label });
                        toast('Simulateur renommé.', 'success');
                        refresh();
                    } catch (err) { toast(err.message, 'error'); }
                },
            }, 'Renommer'),
            h('button', {
                class: 'btn-sm', type: 'button',
                onclick: async () => {
                    if (!await confirmDialog('Régénérer le jeton ? L’ancien cessera de fonctionner immédiatement.', { confirmLabel: 'Régénérer' })) return;
                    try {
                        const data = await post(`/api/v1/admin/simulators/${sim.id}/token`);
                        tokenModal(data.token, 'Nouveau jeton');
                    } catch (err) { toast(err.message, 'error'); }
                },
            }, 'Régénérer le jeton'),
            h('button', {
                class: 'btn-sm btn-danger', type: 'button',
                onclick: async () => {
                    if (!await confirmDialog(`Supprimer « ${sim.label} » et tous ses chronos ? Action irréversible.`, { confirmLabel: 'Supprimer', danger: true })) return;
                    try {
                        await del(`/api/v1/admin/simulators/${sim.id}`);
                        invalidateTenants();
                        toast('Simulateur supprimé.', 'success');
                        setQuery({ sim: null, contest: null, atrack: null, apage: null });
                    } catch (err) { toast(err.message, 'error'); }
                },
            }, 'Supprimer'),
        ),
    ));

    // --- gestion des chronos ---
    const contestId = query.get('contest') || null;
    const detail = await get(`/api/v1/admin/sims/${sim.id}/detail${contestId ? `?contest_id=${contestId}` : ''}`);

    const contestSelect = h('select', { style: 'max-width:260px' },
        h('option', { value: '' }, 'Global'),
        detail.contests.map((c) => h('option', { value: c.id, selected: c.id === contestId }, c.name)),
    );
    contestSelect.addEventListener('change', () => setQuery({ contest: contestSelect.value || null, atrack: null, apage: null }));

    wrap.append(h('div', { class: 'panel' },
        h('h2', {}, 'Chronos'),
        h('div', { class: 'field' }, h('label', {}, 'Tableau'), contestSelect),
    ));

    const tracks = detail.tracks;
    if (!tracks.length) {
        wrap.append(h('p', { class: 'lede' }, 'Aucun chrono sur ce tableau.'));
    } else {
        const trackId = Number(query.get('atrack')) || tracks[0].trackId;
        const page = Math.max(1, Number(query.get('apage')) || 1);
        const board = await get(
            `/api/v1/admin/laps?sim_id=${sim.id}&track_id=${trackId}&page=${page}${contestId ? `&contest_id=${contestId}` : ''}`,
        );

        wrap.lastChild.append(
            trackSelect(tracks, trackId, (id) => setQuery({ atrack: id, apage: null })),
            boardTable(board.rows, {
                manage: (row) => [
                    h('button', {
                        class: 'btn-sm', type: 'button',
                        onclick: () => renameEntry(sim.id, contestId, row),
                    }, 'Renommer'),
                    h('button', {
                        class: 'btn-sm', type: 'button',
                        onclick: () => renamePlayer(sim.id, contestId, row),
                    }, 'Renommer partout'),
                    h('button', {
                        class: 'btn-sm btn-danger', type: 'button',
                        onclick: () => deleteLap(sim.id, row),
                    }, 'Supprimer'),
                ],
            }),
            pagination(board, (p) => setQuery({ apage: p > 1 ? p : null })),
        );
    }

    // --- jobs ---
    wrap.append(h('div', { class: 'panel' },
        h('h2', {}, 'Jobs'),
        detail.jobs.length
            ? h('table', { class: 'admin-table' },
                h('thead', {}, h('tr', {}, h('th', {}, 'Quand'), h('th', {}, 'Type'), h('th', {}, 'État'), h('th', {}))),
                h('tbody', {}, detail.jobs.map((j) => h('tr', {},
                    h('td', { class: 'muted' }, fmtDateTime(j.createdAt)),
                    h('td', { class: 'mono' }, j.type),
                    h('td', {}, h('span', { class: `status-dot ${jobStatusClass(j.status)}` }, j.status)),
                    h('td', {}, h('div', { class: 'row-actions' },
                        j.canRevert ? h('button', {
                            class: 'btn-sm', type: 'button',
                            onclick: async () => {
                                if (!await confirmDialog(`Revert le job ${j.type} ?`)) return;
                                try {
                                    await post(`/api/v1/admin/jobs/${j.id}/revert`, { sim_id: sim.id });
                                    toast('Job revert.', 'success');
                                    refresh();
                                } catch (err) { toast(err.message, 'error'); }
                            },
                        }, 'Revert') : null,
                    )),
                ))),
            )
            : h('p', { class: 'lede' }, 'Aucun job.'),
    ));

    return wrap;
}

function jobStatusClass(status) {
    return { applied: 'ok', pending: 'warn', delivered: 'warn', cancelled: '', reverted: '' }[status] || '';
}

async function deleteLap(simId, row) {
    if (!await confirmDialog(`Supprimer le chrono de ${row.name} (${row.formatted}) ? Un job partira vers le simu.`, { confirmLabel: 'Supprimer', danger: true })) return;
    try {
        const res = await post(`/api/v1/admin/laps/${row.id}/delete`, { sim_id: simId });
        toast(res.message, 'success');
        refresh();
    } catch (err) { toast(err.message, 'error'); }
}

async function renameEntry(simId, contestId, row) {
    const name = await promptDialog('Renommer ce chrono', { value: row.name, maxlength: 20 });
    if (!name) return;
    try {
        const res = await post(`/api/v1/admin/laps/${row.id}/rename`, { sim_id: simId, new_name: name });
        toast(res.message, 'success');
        refresh();
    } catch (err) { toast(err.message, 'error'); }
}

async function renamePlayer(simId, contestId, row) {
    const name = await promptDialog(`Renommer « ${row.name} » sur tous ses chronos`, {
        value: row.name,
        maxlength: 20,
        label: contestId ? 'Nouveau pseudo (ce concours)' : 'Nouveau pseudo (tableau global)',
    });
    if (!name) return;
    try {
        const res = await post('/api/v1/admin/players/rename', {
            sim_id: simId,
            contest_id: contestId || null,
            old_name: row.name,
            new_name: name,
        });
        toast(res.message, 'success');
        refresh();
    } catch (err) { toast(err.message, 'error'); }
}

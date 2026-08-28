// Admin — onglets Organisations et Utilisateurs.

import { h, clear, fmtDateTime } from '../dom.js';
import { get, post, patch, del } from '../api.js';
import { invalidateTenants, state } from '../state.js';
import { replace } from '../router.js';
import { visibilityBadge, toast, confirmDialog, promptDialog, openModal } from '../components.js';

function refresh() {
    replace(location.pathname + location.search);
}

// ---------------------------------------------------------------------------
// Organisations
// ---------------------------------------------------------------------------

export async function tenantsTab(slot) {
    const overview = await get('/api/v1/admin/overview');
    clear(slot);

    const label = h('input', { type: 'text', maxlength: '60', placeholder: 'Club Sim Racing' });
    const visibility = h('select', {},
        h('option', { value: 'public' }, 'Public — visible sans connexion'),
        h('option', { value: 'private' }, 'Privé — visiteurs assignés uniquement'),
    );

    slot.append(h('form', {
        class: 'panel',
        onsubmit: async (e) => {
            e.preventDefault();
            try {
                await post('/api/v1/admin/tenants', { label: label.value, visibility: visibility.value });
                invalidateTenants();
                toast('Organisation créée.', 'success');
                refresh();
            } catch (err) { toast(err.message, 'error'); }
        },
    },
        h('h2', {}, 'Nouvelle organisation'),
        h('div', { class: 'form-row' },
            h('div', { class: 'field' }, h('label', {}, 'Nom'), label),
            h('div', { class: 'field' }, h('label', {}, 'Visibilité'), visibility),
        ),
        h('button', { type: 'submit', class: 'btn-primary' }, 'Créer'),
    ));

    if (!overview.tenants.length) {
        slot.append(h('p', { class: 'lede' }, 'Aucune organisation.'));
        return;
    }

    const simsByTenant = {};
    for (const s of overview.sims) {
        (simsByTenant[s.tenantId] = simsByTenant[s.tenantId] || []).push(s.label);
    }

    slot.append(h('div', { class: 'panel' },
        h('h2', {}, 'Organisations'),
        h('table', { class: 'admin-table' },
            h('thead', {}, h('tr', {},
                h('th', {}, 'Nom'),
                h('th', {}, 'Visibilité'),
                h('th', {}, 'Simulateurs'),
                h('th', {}),
            )),
            h('tbody', {}, overview.tenants.map((t) => {
                const visSelect = h('select', { style: 'max-width:150px' },
                    h('option', { value: 'public', selected: t.visibility === 'public' }, 'Public'),
                    h('option', { value: 'private', selected: t.visibility === 'private' }, 'Privé'),
                );
                visSelect.addEventListener('change', async () => {
                    try {
                        await patch(`/api/v1/admin/tenants/${t.id}`, { visibility: visSelect.value });
                        invalidateTenants();
                        toast('Visibilité mise à jour.', 'success');
                        refresh();
                    } catch (err) { toast(err.message, 'error'); }
                });

                const members = simsByTenant[t.id] || [];
                const deletable = members.length === 0;

                return h('tr', {},
                    h('td', { class: 'pilot' }, t.label),
                    h('td', {}, visSelect),
                    h('td', { class: 'muted' }, members.length ? members.join(', ') : '—'),
                    h('td', {}, h('div', { class: 'row-actions' },
                        h('button', {
                            class: 'btn-sm', type: 'button',
                            onclick: async () => {
                                const name = await promptDialog('Renommer l’organisation', { value: t.label, maxlength: 60 });
                                if (!name) return;
                                try {
                                    await patch(`/api/v1/admin/tenants/${t.id}`, { label: name });
                                    invalidateTenants();
                                    toast('Organisation renommée.', 'success');
                                    refresh();
                                } catch (err) { toast(err.message, 'error'); }
                            },
                        }, 'Renommer'),
                        h('button', {
                            class: 'btn-sm btn-danger',
                            type: 'button',
                            disabled: !deletable,
                            title: deletable ? '' : 'Déplace ou supprime d’abord les simulateurs',
                            onclick: async () => {
                                if (!await confirmDialog(`Supprimer l’organisation « ${t.label} » ?`, { confirmLabel: 'Supprimer', danger: true })) return;
                                try {
                                    await del(`/api/v1/admin/tenants/${t.id}`);
                                    invalidateTenants();
                                    toast('Organisation supprimée.', 'success');
                                    refresh();
                                } catch (err) { toast(err.message, 'error'); }
                            },
                        }, 'Supprimer'),
                    )),
                );
            })),
        ),
        h('p', { class: 'hint' }, 'La suppression est bloquée tant qu’une organisation contient des simulateurs.'),
    ));
}

// ---------------------------------------------------------------------------
// Utilisateurs
// ---------------------------------------------------------------------------

export async function usersTab(slot) {
    const [{ users }, overview] = await Promise.all([
        get('/api/v1/admin/users'),
        get('/api/v1/admin/overview'),
    ]);
    clear(slot);

    slot.append(createUserPanel(overview.tenants));

    if (!users.length) {
        slot.append(h('p', { class: 'lede' }, 'Aucun compte.'));
        return;
    }

    const tenantLabel = (id) => overview.tenants.find((t) => t.id === id)?.label || id;

    slot.append(h('div', { class: 'panel' },
        h('h2', {}, 'Comptes'),
        h('table', { class: 'admin-table' },
            h('thead', {}, h('tr', {},
                h('th', {}, 'E-mail'),
                h('th', {}, 'Rôle'),
                h('th', {}, 'Accès organisations'),
                h('th', {}, 'Statut'),
                h('th', {}),
            )),
            h('tbody', {}, users.map((u) => h('tr', {},
                h('td', { class: 'pilot' }, u.email),
                h('td', {}, h('span', { class: `badge ${u.role === 'admin' ? 'public' : 'private'}` }, u.role === 'admin' ? 'Admin' : 'Visiteur')),
                h('td', { class: 'muted' },
                    u.role === 'admin'
                        ? 'Toutes'
                        : (u.tenantIds.length ? u.tenantIds.map(tenantLabel).join(', ') : 'Tenants publics uniquement')),
                h('td', {}, h('span', { class: `status-dot ${u.disabled ? 'err' : 'ok'}` }, u.disabled ? 'Désactivé' : 'Actif')),
                h('td', {}, h('div', { class: 'row-actions' },
                    h('button', {
                        class: 'btn-sm', type: 'button',
                        onclick: () => editUserModal(u, overview.tenants),
                    }, 'Modifier'),
                    u.id !== state.me.user.id
                        ? h('button', {
                            class: 'btn-sm btn-danger', type: 'button',
                            onclick: async () => {
                                if (!await confirmDialog(`Supprimer le compte ${u.email} ?`, { confirmLabel: 'Supprimer', danger: true })) return;
                                try {
                                    await del(`/api/v1/admin/users/${u.id}`);
                                    toast('Compte supprimé.', 'success');
                                    refresh();
                                } catch (err) { toast(err.message, 'error'); }
                            },
                        }, 'Supprimer')
                        : null,
                )),
            ))),
        ),
        h('p', { class: 'hint' }, 'Un visiteur voit les organisations publiques + celles qui lui sont assignées. Un admin voit tout.'),
    ));
}

function tenantChecklist(tenants, selectedIds = []) {
    const boxes = tenants.map((t) => {
        const input = h('input', { type: 'checkbox', value: t.id, checked: selectedIds.includes(t.id) });
        return { input, el: h('label', { class: 'check-item' }, input, t.label) };
    });
    return {
        el: h('div', { class: 'check-list' }, boxes.map((b) => b.el)),
        values: () => boxes.filter((b) => b.input.checked).map((b) => b.input.value),
    };
}

function createUserPanel(tenants) {
    const email = h('input', { type: 'email', required: true, placeholder: 'pilote@club.fr', autocomplete: 'off' });
    const password = h('input', { type: 'password', required: true, minlength: '8', autocomplete: 'new-password' });
    const role = h('select', {},
        h('option', { value: 'visitor' }, 'Visiteur — lecture seule'),
        h('option', { value: 'admin' }, 'Admin — gestion complète'),
    );
    const access = tenantChecklist(tenants);
    const accessField = h('div', { class: 'field' },
        h('label', {}, 'Organisations accessibles (visiteur)'),
        access.el,
        h('p', { class: 'hint' }, 'Sans sélection : organisations publiques uniquement.'),
    );

    const toggleAccess = () => { accessField.style.display = role.value === 'visitor' ? '' : 'none'; };
    role.addEventListener('change', toggleAccess);
    toggleAccess();

    return h('form', {
        class: 'panel',
        onsubmit: async (e) => {
            e.preventDefault();
            try {
                await post('/api/v1/admin/users', {
                    email: email.value,
                    password: password.value,
                    role: role.value,
                    tenant_ids: role.value === 'visitor' ? access.values() : [],
                });
                toast('Compte créé.', 'success');
                refresh();
            } catch (err) { toast(err.message, 'error'); }
        },
    },
        h('h2', {}, 'Nouveau compte'),
        h('div', { class: 'form-row' },
            h('div', { class: 'field' }, h('label', {}, 'Adresse e-mail'), email),
            h('div', { class: 'field' }, h('label', {}, 'Mot de passe (8 min.)'), password),
            h('div', { class: 'field' }, h('label', {}, 'Rôle'), role),
        ),
        accessField,
        h('button', { type: 'submit', class: 'btn-primary' }, 'Créer le compte'),
    );
}

function editUserModal(user, tenants) {
    const role = h('select', {},
        h('option', { value: 'visitor', selected: user.role === 'visitor' }, 'Visiteur'),
        h('option', { value: 'admin', selected: user.role === 'admin' }, 'Admin'),
    );
    const disabled = h('input', { type: 'checkbox', checked: user.disabled });
    const newPassword = h('input', { type: 'password', minlength: '8', autocomplete: 'new-password', placeholder: 'Laisser vide pour ne pas changer' });
    const access = tenantChecklist(tenants, user.tenantIds);
    const accessField = h('div', { class: 'field' }, h('label', {}, 'Organisations accessibles'), access.el);

    const toggleAccess = () => { accessField.style.display = role.value === 'visitor' ? '' : 'none'; };
    role.addEventListener('change', toggleAccess);
    toggleAccess();

    openModal(`Modifier ${user.email}`, [
        h('div', { class: 'field' }, h('label', {}, 'Rôle'), role),
        accessField,
        h('div', { class: 'field' }, h('label', {}, 'Nouveau mot de passe'), newPassword),
        h('label', { class: 'check-item' }, disabled, 'Compte désactivé'),
    ], [
        { label: 'Annuler', onClick: (c) => c() },
        {
            label: 'Enregistrer',
            class: 'btn-primary',
            onClick: async (close) => {
                const body = {
                    role: role.value,
                    disabled: disabled.checked,
                    tenant_ids: role.value === 'visitor' ? access.values() : [],
                };
                if (newPassword.value) body.password = newPassword.value;
                try {
                    await patch(`/api/v1/admin/users/${user.id}`, body);
                    close();
                    toast('Compte mis à jour.', 'success');
                    refresh();
                } catch (err) {
                    toast(err.message, 'error');
                }
            },
        },
    ]);
}

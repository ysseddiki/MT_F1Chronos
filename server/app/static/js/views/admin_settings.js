// Admin — onglet Réglages : accès public + changement de mot de passe.

import { h, clear } from '../dom.js';
import { get, post } from '../api.js';
import { state } from '../state.js';
import { replace } from '../router.js';
import { toast } from '../components.js';

export async function settingsTab(slot) {
    const overview = await get('/api/v1/admin/overview');
    clear(slot);

    // --- Accès public ---
    const toggle = h('input', { type: 'checkbox', checked: overview.publicAccess });
    toggle.addEventListener('change', async () => {
        try {
            await post('/api/v1/admin/settings', { public_access: toggle.checked });
            toast(
                toggle.checked
                    ? 'Accès public activé : les organisations publiques sont visibles sans connexion.'
                    : 'Accès public désactivé : connexion requise pour tout le monde.',
                'success',
            );
            state.tenants = null;
        } catch (err) {
            toggle.checked = !toggle.checked;
            toast(err.message, 'error');
        }
    });

    slot.append(h('div', { class: 'panel' },
        h('h2', {}, 'Accès public'),
        h('div', { class: 'switch-row' },
            h('div', {},
                h('div', {}, 'Résultats visibles sans connexion'),
                h('p', { class: 'hint' },
                    'Activé : les organisations « publiques » sont lisibles par tout le monde. ',
                    'Désactivé : seuls les comptes connectés voient les résultats.'),
            ),
            h('span', { class: 'switch' }, toggle, h('span', { class: 'track' })),
        ),
    ));

    // --- Mot de passe du compte courant ---
    const current = h('input', { type: 'password', autocomplete: 'current-password', required: true });
    const next = h('input', { type: 'password', autocomplete: 'new-password', required: true, minlength: '8' });
    const confirm = h('input', { type: 'password', autocomplete: 'new-password', required: true, minlength: '8' });

    slot.append(h('form', {
        class: 'panel',
        onsubmit: async (e) => {
            e.preventDefault();
            if (next.value !== confirm.value) {
                toast('La confirmation ne correspond pas.', 'error');
                return;
            }
            try {
                const res = await post('/api/v1/auth/change-password', {
                    current_password: current.value,
                    new_password: next.value,
                });
                toast(res.message || 'Mot de passe mis à jour.', 'success');
                current.value = next.value = confirm.value = '';
            } catch (err) {
                toast(err.message, 'error');
            }
        },
    },
        h('h2', {}, 'Mon mot de passe'),
        h('p', { class: 'hint' }, 'Le .env ne sème que le tout premier compte. Ensuite, tout se passe ici.'),
        h('div', { class: 'form-row' },
            h('div', { class: 'field' }, h('label', {}, 'Mot de passe actuel'), current),
            h('div', { class: 'field' }, h('label', {}, 'Nouveau (8 min.)'), next),
            h('div', { class: 'field' }, h('label', {}, 'Confirmation'), confirm),
        ),
        h('button', { type: 'submit', class: 'btn-primary' }, 'Changer le mot de passe'),
    ));
}

// Admin — onglet Réglages : accès public + barème championnat.

import { h, clear } from '../dom.js';
import { get, post } from '../api.js';
import { state } from '../state.js';
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

    // --- Barème championnat (web uniquement) ---
    const pointsInput = h('input', {
        type: 'text',
        value: overview.pointsByPlace || '25,18,15,12,10,8,6,4,2,1',
        placeholder: '25,18,15,12,10,8,6,4,2,1',
        spellcheck: 'false',
        autocomplete: 'off',
    });

    slot.append(h('form', {
        class: 'panel',
        onsubmit: async (e) => {
            e.preventDefault();
            try {
                const res = await post('/api/v1/admin/settings', {
                    points_by_place: pointsInput.value,
                });
                pointsInput.value = res.pointsByPlace || pointsInput.value;
                toast('Barème Expérience enregistré.', 'success');
            } catch (err) {
                toast(err.message, 'error');
            }
        },
    },
        h('h2', {}, 'Expérience — points par place'),
        h('p', { class: 'hint' },
            'Système de points style F1, uniquement sur le site de résultats (pas l’overlay). ',
            'Liste séparée par des virgules : P1, P2, P3… Ajoutez un chiffre pour scorer une place de plus. ',
            'Exemple : 25,18,15,12,10,8,6,4,2,1 ou 25,18,10,8,6,5,4,3,2,1,1,1,1.'),
        h('div', { class: 'field' },
            h('label', {}, 'Points par places'),
            pointsInput,
        ),
        h('button', { type: 'submit', class: 'btn-primary' }, 'Enregistrer le barème'),
    ));

    slot.append(h('div', { class: 'panel' },
        h('h2', {}, 'Mon compte'),
        h('p', { class: 'hint' },
            'Mot de passe et infos personnelles : menu utilisateur (haut droite) → ',
            h('a', { href: '/account', 'data-link': true }, 'Mon compte'),
            '.'),
    ));
}

// Compte connecté : infos + mot de passe (+ pseudo SimRacer).

import { h, clear } from '../dom.js';
import { post, patch } from '../api.js';
import { loadMe, state, isSimRacer } from '../state.js';
import { navigate } from '../router.js';
import { toast } from '../components.js';

export async function accountView(container) {
    clear(container);
    container.append(h('p', { class: 'loading' }, 'Chargement…'));

    let me;
    try {
        me = await loadMe(true);
    } catch (err) {
        clear(container);
        container.append(h('p', { class: 'lede' }, err.message || 'Serveur injoignable.'));
        return;
    }
    if (!me.authenticated) {
        navigate('/login');
        return;
    }

    const user = me.user;
    const roleLabel = user.role === 'admin' ? 'Administrateur'
        : user.role === 'simracer' ? 'SimRacer'
            : 'Visiteur';

    clear(container);
    document.title = 'Mon compte — F1 Chronos';

    container.append(
        h('p', { class: 'kicker' }, 'Compte'),
        h('h1', {}, 'Mes informations'),
        h('p', { class: 'lede' }, 'Modifiez le mot de passe de votre session. Les droits restent gérés par l’administrateur.'),
        h('div', { class: 'panel', style: 'max-width:480px' },
            h('h2', {}, 'Identité'),
            h('div', { class: 'field' },
                h('label', {}, 'E-mail'),
                h('input', { type: 'email', value: user.email, disabled: true }),
            ),
            h('div', { class: 'field' },
                h('label', {}, 'Rôle'),
                h('input', { type: 'text', value: roleLabel, disabled: true }),
            ),
        ),
    );

    if (isSimRacer()) {
        const error = h('div', {});
        const pseudo = h('input', {
            type: 'text',
            required: true,
            maxlength: '20',
            autocomplete: 'nickname',
            value: user.simPseudo || '',
            placeholder: 'Pseudo affiché sur le simulateur',
        });
        const form = h('form', {
            class: 'panel',
            style: 'max-width:480px',
            onsubmit: async (e) => {
                e.preventDefault();
                clear(error);
                submit.disabled = true;
                try {
                    const res = await patch('/api/v1/profile/sim-pseudo', { sim_pseudo: pseudo.value });
                    state.me = null;
                    state.meLoaded = false;
                    const { invalidateLinkedPilots } = await import('../state.js');
                    invalidateLinkedPilots();
                    await loadMe(true);
                    toast(res.message || 'Pseudo mis à jour.', 'success');
                    submit.disabled = false;
                } catch (err) {
                    error.append(h('div', { class: 'banner error' }, err.message));
                    submit.disabled = false;
                }
            },
        },
            h('h2', {}, 'Pseudo simulateur'),
            error,
            h('div', { class: 'field' },
                h('label', {}, 'Pseudo'),
                pseudo,
                h('p', { class: 'hint' }, '20 caractères max. Appliquez-le ensuite depuis une feuille de temps.'),
            ),
        );
        const submit = h('button', { type: 'submit', class: 'btn-primary' }, 'Enregistrer le pseudo');
        form.append(submit);
        container.append(form);
    }

    const current = h('input', { type: 'password', autocomplete: 'current-password', required: true });
    const next = h('input', { type: 'password', autocomplete: 'new-password', required: true, minlength: '8' });
    const confirm = h('input', { type: 'password', autocomplete: 'new-password', required: true, minlength: '8' });

    container.append(h('form', {
        class: 'panel',
        style: 'max-width:480px',
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
        h('h2', {}, 'Mot de passe'),
        h('p', { class: 'hint' }, 'Minimum 8 caractères.'),
        h('div', { class: 'field' }, h('label', {}, 'Mot de passe actuel'), current),
        h('div', { class: 'field' }, h('label', {}, 'Nouveau'), next),
        h('div', { class: 'field' }, h('label', {}, 'Confirmation'), confirm),
        h('button', { type: 'submit', class: 'btn-primary' }, 'Changer le mot de passe'),
    ));
}

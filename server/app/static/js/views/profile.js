// Profil SimRacer : pseudo simulateur (obligatoire à la première connexion).

import { h, clear } from '../dom.js';
import { patch } from '../api.js';
import { loadMe, state } from '../state.js';
import { navigate } from '../router.js';
import { toast } from '../components.js';

export async function profileView(container) {
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
    if (me.user.role !== 'simracer') {
        navigate('/');
        return;
    }

    clear(container);
    document.title = 'Mon profil — F1 Chronos';

    const error = h('div', {});
    const pseudo = h('input', {
        type: 'text',
        required: true,
        maxlength: '20',
        autocomplete: 'nickname',
        value: me.user.simPseudo || '',
        placeholder: 'Pseudo affiché sur le simulateur',
    });

    const form = h('form', {
        onsubmit: async (e) => {
            e.preventDefault();
            clear(error);
            submit.disabled = true;
            try {
                const res = await patch('/api/v1/profile/sim-pseudo', { sim_pseudo: pseudo.value });
                state.me = null;
                state.meLoaded = false;
                await loadMe(true);
                toast(res.message || 'Profil mis à jour.', 'success');
                if (!state.me.profileRequired) navigate('/');
            } catch (err) {
                error.append(h('div', { class: 'banner error' }, err.message));
                submit.disabled = false;
            }
        },
    },
        error,
        h('div', { class: 'field' },
            h('label', { for: 'sim-pseudo' }, 'Pseudo simulateur'),
            pseudo,
            h('p', { class: 'hint' }, '20 caractères max. Enregistrer ne modifie pas le simulateur : utilisez « Appliquer mon pseudo » sur une feuille de temps.'),
        ),
    );

    const submit = h('button', { type: 'submit', class: 'btn-primary' }, 'Enregistrer');
    form.append(submit);

    container.append(
        h('p', { class: 'kicker' }, 'Profil'),
        h('h1', {}, 'Mon pseudo simulateur'),
        me.profileRequired
            ? h('p', { class: 'banner info' }, 'Bienvenue ! Choisissez le pseudo de votre profil. Il ne sera pas envoyé au simulateur tant que vous n’aurez pas cliqué sur « Appliquer mon pseudo ».')
            : h('p', { class: 'lede' }, `Compte ${me.user.email}. Modifiez votre pseudo puis appliquez-le sur un simulateur depuis les résultats.`),
        h('div', { class: 'panel', style: 'max-width:420px' }, form),
    );
}

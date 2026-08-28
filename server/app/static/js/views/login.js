// Connexion + bootstrap du premier compte admin (quand aucun utilisateur n'existe).

import { h, clear } from '../dom.js';
import { post } from '../api.js';
import { loadMe, state } from '../state.js';
import { navigate } from '../router.js';

export async function loginView(container) {
    clear(container);
    container.append(h('p', { class: 'loading' }, 'Chargement…'));

    const me = await loadMe(true);
    if (me.authenticated) {
        navigate('/');
        return;
    }

    clear(container);
    document.title = 'Connexion — F1 Chronos';

    if (me.setupRequired) renderSetup(container);
    else renderLogin(container);
}

function renderLogin(container) {
    const error = h('div', {});
    const email = h('input', { id: 'email', name: 'email', type: 'email', autocomplete: 'username', required: true, placeholder: 'admin@localhost' });
    const password = h('input', { id: 'password', name: 'password', type: 'password', autocomplete: 'current-password', required: true });

    const form = h('form', {
        onsubmit: async (e) => {
            e.preventDefault();
            clear(error);
            submit.disabled = true;
            try {
                await post('/api/v1/auth/login', { email: email.value, password: password.value });
                state.me = null;
                state.meLoaded = false;
                state.tenants = null;
                navigate('/');
                location.reload();
            } catch (err) {
                error.append(h('div', { class: 'banner error' }, err.message));
                submit.disabled = false;
            }
        },
    },
        error,
        h('div', { class: 'field' }, h('label', { for: 'email' }, 'Adresse e-mail'), email),
        h('div', { class: 'field' }, h('label', { for: 'password' }, 'Mot de passe'), password),
    );

    const submit = h('button', { type: 'submit', class: 'btn-primary' }, 'Se connecter');
    form.append(submit);

    container.append(
        h('p', { class: 'kicker' }, 'Connexion'),
        h('h1', {}, 'Se connecter'),
        h('p', { class: 'lede' }, 'Compte administrateur ou visiteur du serveur de résultats.'),
        h('div', { class: 'panel', style: 'max-width:420px' }, form),
    );
}

function renderSetup(container) {
    const error = h('div', {});
    const email = h('input', { id: 'email', name: 'email', type: 'email', autocomplete: 'username', required: true, value: 'admin@localhost' });
    const password = h('input', { id: 'password', name: 'password', type: 'password', autocomplete: 'new-password', required: true, minlength: '8' });
    const confirm = h('input', { id: 'confirm', name: 'confirm', type: 'password', autocomplete: 'new-password', required: true, minlength: '8' });

    const form = h('form', {
        onsubmit: async (e) => {
            e.preventDefault();
            clear(error);
            if (password.value !== confirm.value) {
                error.append(h('div', { class: 'banner error' }, 'La confirmation ne correspond pas.'));
                return;
            }
            submit.disabled = true;
            try {
                await post('/api/v1/auth/setup', { email: email.value, password: password.value });
                state.me = null;
                state.meLoaded = false;
                state.tenants = null;
                navigate('/admin');
                location.reload();
            } catch (err) {
                error.append(h('div', { class: 'banner error' }, err.message));
                submit.disabled = false;
            }
        },
    },
        error,
        h('div', { class: 'field' }, h('label', { for: 'email' }, 'Adresse e-mail administrateur'), email),
        h('div', { class: 'field' }, h('label', { for: 'password' }, 'Mot de passe (8 caractères min.)'), password),
        h('div', { class: 'field' }, h('label', { for: 'confirm' }, 'Confirmation'), confirm),
    );

    const submit = h('button', { type: 'submit', class: 'btn-accent' }, 'Créer le compte administrateur');
    form.append(submit);

    container.append(
        h('p', { class: 'kicker' }, 'Premier démarrage'),
        h('h1', {}, 'Créer le compte administrateur'),
        h('p', { class: 'lede' }, 'Aucun compte n’existe encore. Ce premier compte aura tous les droits.'),
        h('div', { class: 'panel', style: 'max-width:420px' }, form),
    );
}

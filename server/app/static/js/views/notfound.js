import { h, clear } from '../dom.js';

export function notFoundView(container) {
    clear(container);
    document.title = 'Introuvable — F1 Chronos';
    container.append(
        h('p', { class: 'kicker' }, '404'),
        h('h1', {}, 'Page introuvable'),
        h('p', { class: 'lede' }, 'Ce lien ne correspond à rien — ou tu n’y as pas accès.'),
        h('a', { class: 'btn', href: '/', 'data-link': true }, 'Retour aux résultats'),
    );
}

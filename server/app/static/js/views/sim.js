// Page simulateur : classements d'un seul simu.

import { h, clear } from '../dom.js';
import { get } from '../api.js';
import { presence, sessionPseudoEditor } from '../components.js';
import { renderBoardPage } from './board_page.js';

export async function simView(container, [simId], query) {
    let data, tracksData;
    try {
        [data, tracksData] = await Promise.all([
            get(`/api/v1/sims/${simId}`),
            get(`/api/v1/sims/${simId}/tracks`),
        ]);
    } catch (err) {
        clear(container);
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }

    const { sim, tenant } = data;
    const tracks = tracksData.tracks;

    const head = h('div', {},
        h('div', { class: 'page-head' },
            h('div', { class: 'titles' },
                tenant
                    ? h('a', { class: 'back-link', href: `/t/${tenant.id}`, 'data-link': true }, `← ${tenant.label}`)
                    : null,
                h('p', { class: 'kicker' }, 'Simulateur'),
                h('h1', {}, sim.label),
                h('div', { class: 'flex' },
                    presence(sim),
                    sessionPseudoEditor(sim)
                        || (sim.playerName ? h('span', { class: 'muted' }, `· Pilote : ${sim.playerName}`) : null),
                    sim.currentTrackName ? h('span', { class: 'muted' }, `· En piste : ${sim.currentTrackName}`) : null,
                ),
            ),
        ),
    );

    renderBoardPage(container, query, {
        head,
        tracks,
        focusTrackId: sim.currentTrackId >= 0 ? sim.currentTrackId : null,
        showSim: false,
        fetchBoard: (trackId, best, page) =>
            get(`/api/v1/sims/${simId}/leaderboard?track_id=${trackId}&best=${best}&page=${page}`),
    });
}

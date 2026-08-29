// Page concours : classement d'un concours d'un simulateur.

import { h, clear } from '../dom.js';
import { get } from '../api.js';
import { simPseudoControls } from '../components.js';
import { renderBoardPage } from './board_page.js';
import { statusLabel } from './contests.js';

export async function contestView(container, [simId, contestId], query) {
    let contestData, tracksData, simData;
    try {
        [contestData, tracksData, simData] = await Promise.all([
            get(`/api/v1/sims/${simId}/contests/${contestId}`),
            get(`/api/v1/sims/${simId}/tracks?contest_id=${contestId}`),
            get(`/api/v1/sims/${simId}`),
        ]);
    } catch (err) {
        clear(container);
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }

    const { contest } = contestData;
    const tracks = tracksData.tracks;
    const { sim } = simData;

    const head = h('div', { class: 'page-head' },
        h('div', { class: 'titles' },
            h('a', { class: 'back-link', href: `/contests?sim=${simId}`, 'data-link': true }, '← Concours'),
            h('p', { class: 'kicker' }, `Concours · ${statusLabel(contest.status)}`),
            h('h1', {}, contest.name),
            h('div', { class: 'flex' },
                h('p', { class: 'lede', style: 'margin:0' }, sim.label),
                simPseudoControls(sim),
            ),
        ),
    );

    renderBoardPage(container, query, {
        head,
        tracks,
        focusTrackId: contest.trackFilter != null && contest.trackFilter >= 0 ? contest.trackFilter : null,
        liveTrackId: sim.currentTrackId >= 0 ? sim.currentTrackId : null,
        showSim: false,
        fetchBoard: (trackId, best, page) =>
            get(`/api/v1/sims/${simId}/leaderboard?track_id=${trackId}&contest_id=${contestId}&best=${best}&page=${page}`),
    });
}

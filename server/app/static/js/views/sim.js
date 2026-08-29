// Page simulateur : classement global ou concours (liés à ce simu).

import { h, clear } from '../dom.js';
import { get } from '../api.js';
import { presence, simPseudoControls, contestBoardSelect } from '../components.js';
import { renderBoardPage } from './board_page.js';
import { tenantPath } from '../paths.js';
import { setQuery, replace } from '../router.js';

export async function simView(container, [simId], query) {
    const contestId = query.get('contest') || null;

    let data, contestsData;
    try {
        [data, contestsData] = await Promise.all([
            get(`/api/v1/sims/${simId}`),
            get(`/api/v1/sims/${simId}/contests`),
        ]);
    } catch (err) {
        clear(container);
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }

    const { sim, tenant } = data;
    const contests = contestsData.contests || [];
    const contest = contestId ? contests.find((c) => c.id === contestId) : null;

    if (contestId && !contest) {
        replace(`/sim/${simId}`);
        return;
    }

    let tracksData;
    try {
        tracksData = await get(
            `/api/v1/sims/${simId}/tracks${contest ? `?contest_id=${contest.id}` : ''}`,
        );
    } catch (err) {
        clear(container);
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }

    const tracks = tracksData.tracks;
    const boardScope = contestBoardSelect(contests, contest?.id ?? null, (id) => {
        setQuery({ contest: id || null, track: null, page: null });
    });

    const head = h('div', {},
        h('div', { class: 'page-head' },
            h('div', { class: 'titles' },
                tenant
                    ? h('a', { class: 'back-link', href: tenantPath(tenant), 'data-link': true }, `← ${tenant.label}`)
                    : null,
                h('p', { class: 'kicker' }, contest ? 'Concours' : 'Simulateur'),
                h('h1', {}, contest ? contest.name : sim.label),
                h('div', { class: 'flex' },
                    presence(sim),
                    simPseudoControls(sim)
                        || (sim.playerName ? h('span', { class: 'muted' }, `· Pilote : ${sim.playerName}`) : null),
                    sim.currentTrackName ? h('span', { class: 'muted' }, `· En piste : ${sim.currentTrackName}`) : null,
                ),
            ),
        ),
        boardScope,
    );

    const focusTrackId = contest?.trackFilter != null && contest.trackFilter >= 0
        ? contest.trackFilter
        : (sim.currentTrackId >= 0 ? sim.currentTrackId : null);

    renderBoardPage(container, query, {
        head,
        tracks,
        sims: [sim],
        defaultSimId: sim.id,
        contestId: contest?.id ?? null,
        focusTrackId,
        liveTrackId: sim.currentTrackId >= 0 ? sim.currentTrackId : null,
        showSim: false,
        fetchBoard: (trackId, best, page) => {
            const qs = new URLSearchParams({
                track_id: String(trackId),
                best: String(best),
                page: String(page),
            });
            if (contest) qs.set('contest_id', contest.id);
            return get(`/api/v1/sims/${simId}/leaderboard?${qs}`);
        },
    });
}

// Page organisation : classement agrégé multi-sims.

import { h, clear } from '../dom.js';
import { get } from '../api.js';
import { visibilityBadge } from '../components.js';
import { renderBoardPage } from './board_page.js';
import { tenantPath } from '../paths.js';
import { replace } from '../router.js';

export async function tenantView(container, [tenantKey], query) {
    let data, tracksData;
    try {
        [data, tracksData] = await Promise.all([
            get(`/api/v1/tenants/${tenantKey}`),
            get(`/api/v1/tenants/${tenantKey}/tracks`),
        ]);
    } catch (err) {
        clear(container);
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }

    const { tenant, sims } = data;
    const canonical = tenant.slug || tenant.id;
    if (canonical !== tenantKey) {
        replace(`/t/${canonical}${location.search}`);
        return;
    }
    const tenantId = tenant.id;
    const tracks = tracksData.tracks;
    const focusSim = sims.find((s) => s.currentTrackId >= 0);

    const head = h('div', {},
        h('div', { class: 'page-head' },
            h('div', { class: 'titles' },
                h('p', { class: 'kicker' }, 'Organisation'),
                h('h1', {}, tenant.label, ' ', visibilityBadge(tenant.visibility)),
                h('p', { class: 'lede' },
                    `Classement agrégé sur ${sims.length} simulateur${sims.length > 1 ? 's' : ''}.`),
            ),
        ),
    );

    renderBoardPage(container, query, {
        head,
        tracks,
        sims,
        focusTrackId: focusSim?.currentTrackId ?? null,
        liveTrackId: focusSim?.currentTrackId >= 0 ? focusSim.currentTrackId : null,
        showSim: sims.length > 1,
        fetchBoard: (trackId, best, page) =>
            get(`/api/v1/tenants/${tenantId}/leaderboard?track_id=${trackId}&best=${best}&page=${page}`),
    });
}

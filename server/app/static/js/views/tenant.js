// Page organisation : classement agrégé multi-sims.

import { h, clear } from '../dom.js';
import { get } from '../api.js';
import { presence, visibilityBadge, sessionPseudoEditor } from '../components.js';
import { renderBoardPage } from './board_page.js';

export async function tenantView(container, [tenantId], query) {
    let data, tracksData;
    try {
        [data, tracksData] = await Promise.all([
            get(`/api/v1/tenants/${tenantId}`),
            get(`/api/v1/tenants/${tenantId}/tracks`),
        ]);
    } catch (err) {
        clear(container);
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }

    const { tenant, sims } = data;
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
        sims.length ? h('div', { class: 'panel' },
            h('h2', {}, 'Simulateurs'),
            h('div', { class: 'flex' },
                sims.map((s) => h('span', { class: 'flex' },
                    h('a', {
                        class: 'btn btn-sm',
                        href: `/sim/${s.id}`,
                        'data-link': true,
                        style: 'justify-content:flex-start',
                    }, s.label, ' — ', presence(s)),
                    sessionPseudoEditor(s),
                )),
            ),
        ) : null,
    );

    renderBoardPage(container, query, {
        head,
        tracks,
        focusTrackId: focusSim?.currentTrackId ?? null,
        showSim: sims.length > 1,
        fetchBoard: (trackId, best, page) =>
            get(`/api/v1/tenants/${tenantId}/leaderboard?track_id=${trackId}&best=${best}&page=${page}`),
    });
}

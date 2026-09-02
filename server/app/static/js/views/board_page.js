// Moteur commun des pages de classement : onglets Classement / Derniers chronos,
// sélecteur circuit, switch best/all, tableau paginé (20/page), live SSE (repli 60 s).

import { h, clear } from '../dom.js';
import { segmented, trackSelect, boardTable, recentLapsPanel, pagination, banner, simToolbarStrip } from '../components.js';
import { setQuery, onCleanup } from '../router.js';
import { subscribeChanges, mySimulatorPseudo, isAdmin } from '../state.js';
import { boardRowManageMenu } from '../board_manage.js';

export const FALLBACK_REFRESH_MS = 60_000;
const LIVE_DEBOUNCE_MS = 1500;
const RECENT_LAPS_LIMIT = 15;

export function renderBoardPage(container, query, ctx) {
    // ctx: { head, tracks, focusTrackId, liveTrackId, showSim, sims, defaultSimId, contestId, fetchBoard, fetchRecent }
    clear(container);

    if (query.get('view') === 'recent' && !isAdmin()) {
        setQuery({ view: null });
        return;
    }

    const view = isAdmin() && query.get('view') === 'recent' ? 'recent' : 'leaderboard';
    const best = query.get('best') !== 'false';
    const page = Math.max(1, Number(query.get('page')) || 1);
    const trackId = pickTrack(query, ctx.tracks, ctx.focusTrackId);

    container.append(ctx.head);

    if (!ctx.tracks.length) {
        container.append(h('p', { class: 'lede' }, 'Aucun chrono reçu pour l’instant.'));
        return;
    }

    if (isAdmin()) {
        container.append(
            h('div', { class: 'toolbar board-view-tabs' },
                segmented(
                    [
                        { value: 'leaderboard', label: 'Classement' },
                        { value: 'recent', label: 'Derniers chronos' },
                    ],
                    view,
                    (value) => setQuery({
                        view: value === 'recent' ? 'recent' : null,
                        page: null,
                    }),
                ),
            ),
        );
    }

    const simStrip = simToolbarStrip(ctx.sims);
    if (simStrip) container.append(simStrip);

    const contentSlot = h('div', { class: 'board-view-slot' });
    container.append(contentSlot);

    const trackName = ctx.tracks.find((t) => t.trackId === trackId)?.trackName || '';

    let loadGen = 0;
    let recentGen = 0;

    function buildManage(onDone) {
        return isAdmin()
            ? (row) => boardRowManageMenu(row, {
                simId: row.simId || ctx.defaultSimId,
                contestId: ctx.contestId ?? null,
                onDone,
            })
            : null;
    }

    function renderLeaderboardChrome() {
        return h('div', { class: 'board-leaderboard-view' },
            trackSelect(
                ctx.tracks,
                trackId,
                (id) => setQuery({ track: id, page: null }),
                { liveTracks: collectLiveTracks(ctx.sims, ctx.tracks) },
            ),
            h('div', { class: 'toolbar' },
                segmented(
                    [
                        { value: 'best', label: 'Meilleur / joueur' },
                        { value: 'all', label: 'Tous les tours' },
                    ],
                    best ? 'best' : 'all',
                    (value) => setQuery({ best: value === 'all' ? 'false' : null, page: null }),
                ),
            ),
            h('div', { class: 'board-data-slot' }, h('p', { class: 'loading' }, 'Chargement du classement…')),
        );
    }

    function renderRecentChrome() {
        return h('div', { class: 'board-recent-view' },
            h('p', { class: 'lede board-recent-lede' },
                'Les 15 derniers tours enregistrés, tous circuits confondus.'),
            h('div', { class: 'board-data-slot' }, h('p', { class: 'loading' }, 'Chargement des chronos…')),
        );
    }

    clear(contentSlot);
    contentSlot.append(view === 'recent' ? renderRecentChrome() : renderLeaderboardChrome());
    const dataSlot = contentSlot.querySelector('.board-data-slot');

    async function loadRecent() {
        if (!ctx.fetchRecent || view !== 'recent') return;
        const gen = ++recentGen;
        try {
            const data = await ctx.fetchRecent(RECENT_LAPS_LIMIT);
            if (gen !== recentGen) return;
            clear(dataSlot);
            const refreshRecent = () => loadRecent();
            dataSlot.append(recentLapsPanel(data.rows || [], {
                showSim: ctx.showSim,
                highlightName: mySimulatorPseudo() || null,
                manage: buildManage(refreshRecent),
            }));
        } catch (err) {
            if (gen !== recentGen) return;
            clear(dataSlot);
            dataSlot.append(banner(err.message || 'Erreur de chargement.', 'error'));
        }
    }

    async function loadBoard() {
        if (view !== 'leaderboard') return;
        const gen = ++loadGen;
        try {
            const board = await ctx.fetchBoard(trackId, best, page);
            if (gen !== loadGen) return;
            clear(dataSlot);
            const refreshBoard = () => loadBoard();
            dataSlot.append(
                boardTable(board.rows, {
                    showSim: ctx.showSim,
                    highlightName: mySimulatorPseudo() || null,
                    manage: buildManage(refreshBoard),
                }),
                pagination(board, (p) => setQuery({ page: p > 1 ? p : null })),
            );
        } catch (err) {
            if (gen !== loadGen) return;
            clear(dataSlot);
            dataSlot.append(banner(err.message || 'Erreur de chargement.', 'error'));
        }
    }

    if (view === 'recent') loadRecent();
    else loadBoard();

    const reload = () => {
        if (document.hidden) return;
        if (view === 'recent') loadRecent();
        else loadBoard();
    };
    let lastLiveEvent = 0;
    const unsubscribe = subscribeChanges(() => {
        const now = Date.now();
        if (now - lastLiveEvent < LIVE_DEBOUNCE_MS) return;
        lastLiveEvent = now;
        reload();
    });
    const fallback = setInterval(reload, FALLBACK_REFRESH_MS);
    const onVisible = () => { if (!document.hidden) reload(); };
    document.addEventListener('visibilitychange', onVisible);
    onCleanup(() => {
        unsubscribe();
        clearInterval(fallback);
        document.removeEventListener('visibilitychange', onVisible);
    });

    const titleSuffix = view === 'recent'
        ? 'Derniers chronos'
        : (trackName || 'Classement');
    document.title = `${titleSuffix} — F1 Chronos — Résultats`;
}

function pickTrack(query, tracks, focusTrackId) {
    const fromQuery = Number(query.get('track'));
    if (tracks.some((t) => t.trackId === fromQuery)) return fromQuery;
    if (focusTrackId != null && tracks.some((t) => t.trackId === focusTrackId)) return focusTrackId;
    return tracks[0].trackId;
}

/** Circuits en cours sur les simulateurs affichés (dédoublonnés par trackId). */
function collectLiveTracks(sims, tracks) {
    const known = new Set((tracks || []).map((t) => t.trackId));
    const seen = new Set();
    const out = [];
    for (const sim of sims || []) {
        if (sim.currentTrackId == null || sim.currentTrackId < 0) continue;
        if (!known.has(sim.currentTrackId)) continue;
        if (seen.has(sim.currentTrackId)) continue;
        seen.add(sim.currentTrackId);
        out.push({
            trackId: sim.currentTrackId,
            trackName: (sim.currentTrackName || '').trim() || `Circuit ${sim.currentTrackId}`,
            simLabel: sims.length > 1 ? sim.label : null,
        });
    }
    return out;
}

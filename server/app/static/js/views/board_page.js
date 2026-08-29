// Moteur commun des pages de classement : sélecteur circuit, switch best/all,
// tableau paginé (20/page), mises à jour live via SSE (repli 60 s).

import { h, clear } from '../dom.js';
import { segmented, trackSelect, boardTable, pagination, banner } from '../components.js';
import { setQuery, replace, onCleanup } from '../router.js';
import { subscribeChanges, mySimulatorPseudo } from '../state.js';

export const FALLBACK_REFRESH_MS = 60_000;
const LIVE_DEBOUNCE_MS = 1500;

export function renderBoardPage(container, query, ctx) {
    // ctx: { head, tracks, focusTrackId, liveTrackId, showSim, fetchBoard }
    clear(container);

    // Meilleur par pilote par défaut ; ?best=false pour tous les tours
    const best = query.get('best') !== 'false';
    const page = Math.max(1, Number(query.get('page')) || 1);
    const trackId = pickTrack(query, ctx.tracks, ctx.focusTrackId);
    const liveTrackId = ctx.liveTrackId ?? ctx.focusTrackId ?? null;

    container.append(ctx.head);

    if (!ctx.tracks.length) {
        container.append(h('p', { class: 'lede' }, 'Aucun chrono reçu pour l’instant.'));
        return;
    }

    container.append(
        trackSelect(ctx.tracks, trackId, (id) => setQuery({ track: id, page: null }), { liveTrackId }),
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
    );

    const slot = h('div', {}, h('p', { class: 'loading' }, 'Chargement du classement…'));
    container.append(slot);

    const trackName = ctx.tracks.find((t) => t.trackId === trackId)?.trackName || '';

    async function loadBoard() {
        try {
            const board = await ctx.fetchBoard(trackId, best, page);
            clear(slot);
            slot.append(
                boardTable(board.rows, {
                    showSim: ctx.showSim,
                    highlightName: mySimulatorPseudo() || null,
                }),
                pagination(board, (p) => setQuery({ page: p > 1 ? p : null })),
            );
        } catch (err) {
            clear(slot);
            slot.append(banner(err.message || 'Erreur de chargement.', 'error'));
        }
    }

    loadBoard();

    const reload = () => {
        if (!document.hidden) replace(location.pathname + location.search);
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

    document.title = `${trackName ? `${trackName} — ` : ''}F1 Chronos — Résultats`;
}

function pickTrack(query, tracks, focusTrackId) {
    const fromQuery = Number(query.get('track'));
    if (tracks.some((t) => t.trackId === fromQuery)) return fromQuery;
    if (focusTrackId != null && tracks.some((t) => t.trackId === focusTrackId)) return focusTrackId;
    return tracks[0].trackId;
}

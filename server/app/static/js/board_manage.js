// Actions admin sur les feuilles de temps publiques (tenant / simu / concours).

import { post } from './api.js';
import { actionMenu, confirmDialog, promptDialog, toast } from './components.js';

export async function deleteBoardLap(simId, row, onDone) {
    if (!await confirmDialog(
        `Supprimer le chrono de ${row.name} (${row.formatted}) ? Un job partira vers le simu.`,
        { confirmLabel: 'Supprimer', danger: true },
    )) return;
    try {
        const res = await post(`/api/v1/admin/laps/${row.id}/delete`, { sim_id: simId });
        toast(res.message, 'success');
        onDone?.();
    } catch (err) {
        toast(err.message, 'error');
    }
}

export async function renameBoardEntry(simId, contestId, row, onDone) {
    const name = await promptDialog('Renommer ce chrono', { value: row.name, maxlength: 20 });
    if (!name) return;
    try {
        const res = await post(`/api/v1/admin/laps/${row.id}/rename`, { sim_id: simId, new_name: name });
        toast(res.message, 'success');
        onDone?.();
    } catch (err) {
        toast(err.message, 'error');
    }
}

export async function renameBoardPlayer(simId, contestId, row, onDone) {
    const name = await promptDialog(`Renommer « ${row.name} » sur tous ses chronos`, {
        value: row.name,
        maxlength: 20,
        label: contestId ? 'Nouveau pseudo (ce concours)' : 'Nouveau pseudo (tableau global)',
    });
    if (!name) return;
    try {
        const res = await post('/api/v1/admin/players/rename', {
            sim_id: simId,
            contest_id: contestId || null,
            old_name: row.name,
            new_name: name,
        });
        toast(res.message, 'success');
        onDone?.();
    } catch (err) {
        toast(err.message, 'error');
    }
}

/** Menu « … » sur une ligne de classement (admin). */
export function boardRowManageMenu(row, { simId, contestId = null, onDone } = {}) {
    const sid = simId || row.simId;
    if (!sid) return null;
    return actionMenu('⋯', [
        { label: 'Renommer ce chrono', onClick: () => renameBoardEntry(sid, contestId, row, onDone) },
        { label: 'Renommer partout', onClick: () => renameBoardPlayer(sid, contestId, row, onDone) },
        { label: 'Supprimer', danger: true, onClick: () => deleteBoardLap(sid, row, onDone) },
    ], { title: `Actions · ${row.name}` });
}

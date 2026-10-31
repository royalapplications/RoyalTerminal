import { placementActions as layoutActions } from './workspace-placement-commands.js';
export { deferPlacement, dragBounds, floatFrame, windowPlanes } from './workspace-placement-commands.js';

// Render only known enums, counts, and plain-text labels. Terminal programs cannot inject markup.
export function formatAttention(summary) {
  const counts = summary.counts;
  const lines = [
    `${counts.blocked} blocked · ${counts.error} failed · ${counts.working} working · ${counts.done} done · ${counts.idle} idle`,
    `Blocked: ${counts.permission} approval · ${counts.question} question · ${counts.auth} authentication`,
  ];
  if (summary.unavailableBlocks) lines.push(`${summary.unavailableBlocks} terminal(s) could not be inspected.`);
  for (const window of summary.windows) {
    lines.push('', window.label);
    for (const block of window.blocks) {
      const c = block.counts;
      lines.push(c ? `  ${block.label}: ${c.blocked} blocked, ${c.error} failed, ${c.working} working, ${c.done} done, ${c.idle} idle`
        : `  ${block.label}: unavailable`);
    }
  }
  return lines.join('\n');
}

export function placementActions(ctx) {
  return [...layoutActions(ctx), {
    title: 'Inspect workspace attention', hint: 'Program status', session: true,
    run: async () => {
      const session = ctx.session(); if (!session) return;
      const summary = await ctx.call('session.attention', { sessionId: session.id });
      if (ctx.state.stopped || ctx.session()?.id !== session.id) return;
      await ctx.dialog('Workspace attention', 'Reported program records, including floating and unplaced terminals. No input or control lease is required.',
        [{ name: 'activity', label: 'Activity by terminal', type: 'textarea', readOnly: true, value: formatAttention(summary) }], 'Done');
    },
  }];
}

// xterm 6.0.0 emits onUserInput immediately before onData for keyboard,
// paste, and deferred IME input. Parser-generated replies omit that signal.
// This small adapter deliberately depends on that pinned internal contract:
// xterm's public onData API does not distinguish input from VT query replies.
// Revalidate this adapter against real xterm when updating the dependency.
export function bindUserInput(terminal, onData, onBinary) {
  const core = terminal._core?.coreService;
  if (!core || typeof core.onUserInput !== 'function')
    throw new Error('The terminal input-origin adapter is incompatible with this xterm version.');
  let userInput = false;
  const subscriptions = [
    core.onUserInput(() => { userInput = true; }),
    terminal.onData(data => {
      const forward = userInput;
      userInput = false;
      if (forward) onData(data);
    }),
    // xterm's binary event is used only by its mouse-reporting path.
    terminal.onBinary(onBinary),
  ];
  return { dispose() { for (const subscription of subscriptions) subscription.dispose(); } };
}

export function base64Bytes(value) {
  const binary = atob(value); const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}
export function encodeInput(value) {
  const bytes = new TextEncoder().encode(value); let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}
export function nextSequence(current, incoming) {
  if (!Number.isSafeInteger(incoming) || incoming < 1) throw new Error('Invalid server sequence.');
  if (incoming <= current) return false;
  if (incoming !== current + 1) throw new Error(`History gap after ${current}; received ${incoming}. Reattach requires matching terminal state.`);
  return true;
}
export function leaves(layout) {
  if (!layout) return [];
  if (layout.blockId) return [layout.blockId];
  return [...leaves(layout.first), ...leaves(layout.second)];
}
export function replaceRatio(layout, path, ratio) {
  if (!Number.isFinite(ratio) || ratio <= 0 || ratio >= 1) throw new Error('Invalid split ratio.');
  if (!path.length) { if (layout.blockId) throw new Error('A leaf cannot be resized as a split.'); return { ...layout, ratio }; }
  const [child, ...tail] = path;
  if (child !== 'first' && child !== 'second') throw new Error('Invalid split path.');
  return { ...layout, [child]: replaceRatio(layout[child], tail, ratio) };
}
export function formatState(block) { return String(block.state || 'running').toLowerCase(); }
export function safeFilename(label, extension) { return `${String(label).replace(/[^a-z0-9_-]+/gi, '_').slice(0, 80) || 'terminal'}.${extension}`; }

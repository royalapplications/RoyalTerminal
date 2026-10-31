import test from 'node:test';
import assert from 'node:assert/strict';
import { base64Bytes, encodeInput, nextSequence, leaves, replaceRatio, safeFilename } from '../wwwroot/workspace-model.js';
test('input round trips Unicode without confusing text and bytes', () => assert.equal(new TextDecoder().decode(base64Bytes(encodeInput('Zażółć 🦖\r\n'))), 'Zażółć 🦖\r\n'));
test('replay deduplicates applied events and rejects missing VT history', () => { assert.equal(nextSequence(5,5),false); assert.equal(nextSequence(5,6),true); assert.throws(() => nextSequence(5,7), /History gap/); assert.throws(() => nextSequence(0,0), /Invalid/); });
test('nested split edits preserve block identity without mutating server snapshot', () => { const tree={orientation:'Horizontal',ratio:.5,first:{blockId:'a'},second:{orientation:'Vertical',ratio:.5,first:{blockId:'b'},second:{blockId:'c'}}}; const result=replaceRatio(tree,['second'],.7); assert.equal(tree.second.ratio,.5); assert.equal(result.second.ratio,.7); assert.deepEqual(leaves(result),['a','b','c']); assert.throws(()=>replaceRatio(tree,[],NaN)); });
test('capture filenames cannot produce paths', () => assert.equal(safeFilename('../../shell: logs','html'),'_shell_logs.html'));

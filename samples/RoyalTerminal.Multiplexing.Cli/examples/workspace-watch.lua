-- Usage: royalterminal-mux lua workspace-watch.lua
-- Set ROYALTERMINAL_MUX_SESSION to the exact session GUID before invoking.
local initial = assert(rex.session.view())
print('Observing', initial.label, 'revision', initial.revision)
rex.on('session_view_changed', function(ctx, ev)
  local current = assert(rex.session.view{session_id = ctx.session_id})
  print('Revision', ev.revision, 'windows', #current.windows, 'blocks', #current.blocks)
end)
rex.after(60, function() rex.stop(0) end)

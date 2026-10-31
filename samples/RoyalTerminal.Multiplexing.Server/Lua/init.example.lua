-- Opt-in trusted operator configuration. Put a reviewed copy outside the served web root.
rex.action {
  name = 'work.rename',
  run = function(ctx, args)
    assert(type(ctx.session_id) == 'string', 'select a session')
    assert(type(args.label) == 'string' and #args.label > 0, 'supply a label')
    local before = assert(rex.session.view())
    local after = assert(rex.session.set_label { label = args.label, expected_revision = before.revision })
    return { session = ctx.session_id, before = before.label, after = after.label }
  end
}

rex.action {
  name = 'work.attention',
  run = function(ctx)
    return assert(rex.call('session.attention', { sessionId = ctx.session_id }))
  end
}

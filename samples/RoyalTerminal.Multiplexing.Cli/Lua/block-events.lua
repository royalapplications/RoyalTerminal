-- Translate the managed host's terminal metadata into the public Lua block-event shape.
return function(rex)
  local function opt(v) if v~=rex.json.null then return v end end
  return function(item,session,block,sequence)
    local name=assert(item.name):gsub('^terminal%.',''); local p={}
    if name=='program_status_changed' and item.reason=='removed' then name='program_status_removed' end
    if name=='child_exited' then p.exit_code=opt(item.exitCode); if p.exit_code and p.exit_code~=0 then p.error='exit status '..p.exit_code end
    elseif name=='title_changed' then p.title=opt(item.text)
    elseif name=='pwd_changed' then p.pwd=opt(item.pwd)
    elseif name=='desktop_notification' and opt(item.notification) then p.title,p.body=item.notification.title,item.notification.body
    elseif name=='progress_report' and opt(item.progressReport) then p.state,p.progress=item.progressReport.state,opt(item.progressReport.progress)
    elseif name=='program_status_changed' then p.id,p.record,p.reason=item.recordId,item.record,item.reason
    elseif name=='program_status_removed' then p.ids,p.reason={item.recordId},item.reason end
    local ev={type='block_event',name=name,qualified_name='terminal.'..name,creator_name='com.superlogical.terminal',session_id=session,block_id=block,sequence=sequence,block_sequence=opt(item.blockSequence) or item.sequence,payload=p,raw=item}
    for k,v in pairs(p) do ev[k]=v end
    return ev
  end
end

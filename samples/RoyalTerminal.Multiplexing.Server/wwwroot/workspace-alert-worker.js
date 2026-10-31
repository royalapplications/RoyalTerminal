// No fetch interception, cache, push endpoint, bearer, session identifiers or notification text from the PTY.
self.addEventListener('notificationclick', event => {
  event.notification.close();
  event.waitUntil((async()=>{
    const root=new URL('./',self.location.href);
    const windows=await self.clients.matchAll({type:'window',includeUncontrolled:true});
    const workspace=windows.find(c=>{const u=new URL(c.url);return u.origin===root.origin && (u.pathname===root.pathname || u.pathname===root.pathname+'index.html');});
    if(workspace)await workspace.focus();else await self.clients.openWindow(root.href);
  })());
});

// Copyright (c) Royal Apps. Licensed under the MIT license.
package main

import (
 "context"
 "net"
 "net/http"
 "net/http/httptest"
 "os"
 "path/filepath"
 "testing"
)

func TestIdentityOriginAndCapabilityBoundaries(t *testing.T){
 dir:=t.TempDir();path:=filepath.Join(dir,"up.sock");ln,err:=net.Listen("unix",path);if err!=nil{t.Fatal(err)};defer ln.Close()
 backend:=&http.Server{Handler:http.HandlerFunc(func(w http.ResponseWriter,r *http.Request){
   if r.Header.Get("Tailscale-User-Login")!="" || r.Header.Get("X-Forwarded-Host")!="" || r.Header.Get("Forwarded")!="" {t.Error("proxy metadata reached the broker")}
   if r.Host!="localhost" || (r.Header.Get("Origin")!="" && r.Header.Get("Origin")!="http://localhost"){t.Error("invalid upstream origin")}
   if r.Header.Get("Authorization")!="Bearer valid"{w.WriteHeader(401);return}; w.WriteHeader(204)
 })};go backend.Serve(ln);defer backend.Close()
 const host="terminal.lab.ts.net";allow:=map[string]bool{"user@example.com":true}
 h:=makeGateway(host,path,allow,uint32(os.Geteuid()))
 request:=func(handler http.Handler,trusted bool,host,origin,path,token string,logins ...string)int{
   r:=httptest.NewRequest("GET",path,nil);r.Host=host;r=r.WithContext(context.WithValue(r.Context(),daemonPeer{},trusted));for _,v:=range logins{r.Header.Add("Tailscale-User-Login",v)};if origin!=""{r.Header.Set("Origin",origin)};if token!=""{r.Header.Set("Authorization",token)};r.Header.Set("X-Forwarded-Host","evil.test");r.Header.Set("Forwarded","host=evil.test");w:=httptest.NewRecorder();handler.ServeHTTP(w,r);return w.Code
 }
 for _,c:=range []struct{trusted bool;host,origin,token string;logins []string;code int}{
 {false,host,"","Bearer valid",[]string{"user@example.com"},403},{true,host,"","Bearer valid",nil,403},
 {true,host,"","Bearer valid",[]string{"other"},403},{true,host,"","Bearer valid",[]string{"user@example.com","user@example.com"},403},
 {true,"evil.test","","Bearer valid",[]string{"user@example.com"},403},{true,host,"https://evil.test","Bearer valid",[]string{"user@example.com"},403},
 {true,host,"https://"+host,"",[]string{"user@example.com"},401},{true,"localhost","https://"+host,"Bearer valid",[]string{"user@example.com"},204},
 }{if got:=request(h,c.trusted,c.host,c.origin,"/api/context",c.token,c.logins...);got!=c.code{t.Errorf("wanted %d got %d",c.code,got)}}
 if request(h,true,host,"",manifestPath,"","user@example.com")!=200 || request(h,false,host,"",manifestPath,"","user@example.com")!=403{t.Fatal("discovery bypassed access policy")}
 if got:=request(makeGateway(host,path,allow,uint32(os.Geteuid())+1),true,host,"","/api/context","Bearer valid","user@example.com");got!=502{t.Errorf("credential forwarded to wrong Unix peer: %d",got)}
}

func TestUnsafeDirectoriesAreRejected(t *testing.T){dir:=t.TempDir();p:=filepath.Join(dir,"socket");if err:=os.Chmod(dir,0755);err!=nil{t.Fatal(err)};if privatePath(p,uint32(os.Geteuid()))==nil{t.Fatal("accepted exposed directory")};if err:=os.Chmod(dir,0700);err!=nil{t.Fatal(err)};link:=filepath.Join(t.TempDir(),"link");if err:=os.Symlink(dir,link);err!=nil{t.Fatal(err)};if privatePath(filepath.Join(link,"socket"),uint32(os.Geteuid()))==nil{t.Fatal("accepted symbolic link")}}

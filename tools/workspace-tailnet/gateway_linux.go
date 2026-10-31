// Copyright (c) Royal Apps. Licensed under the MIT license.
package main

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"net/http/httputil"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"syscall"
	"time"
)

// Credentials authenticate the immediate socket peer, never the uid claimed by a request.
func peerUID(c net.Conn) (uint32, error) {
	u, ok := c.(*net.UnixConn)
	if !ok { return 0, errors.New("not a Unix socket") }
	raw, err := u.SyscallConn()
	if err != nil { return 0, err }
	var cred *syscall.Ucred
	var optErr error
	err = raw.Control(func(fd uintptr) { cred, optErr = syscall.GetsockoptUcred(int(fd), syscall.SOL_SOCKET, syscall.SO_PEERCRED) })
	if err != nil { return 0, err }; if optErr != nil { return 0, optErr }
	return cred.Uid, nil
}

func privatePath(path string, uid uint32) error {
	if !filepath.IsAbs(path) || filepath.Clean(path) != path || len(path)>107 || path=="/" { return errors.New("expected a canonical absolute Unix socket path") }
	for d := filepath.Dir(path); ; d = filepath.Dir(d) {
		fi,err := os.Lstat(d); if err != nil { return err }
		st,ok:=fi.Sys().(*syscall.Stat_t)
		if !ok || !fi.IsDir() || (st.Uid != 0 && st.Uid != uid) { return fmt.Errorf("untrusted socket directory: %s",d) }
		if d == filepath.Dir(path) && (st.Uid != uid || fi.Mode().Perm()&0077 != 0) { return errors.New("socket parent must be private (0700) and owned by the gateway user") }
		if fi.Mode().Perm()&0022 != 0 && !(st.Uid==0 && fi.Mode()&os.ModeSticky!=0) { return fmt.Errorf("writable socket ancestor: %s",d) }
		if d=="/" { break }
	}
	return nil
}

func validLogin(s string) bool {
	if len(s)==0 || len(s)>254 { return false }
	for _,c:=range s { if c<'!' || c>'~' || strings.ContainsRune(",*?;=",c) {return false} }
	return true
}

func makeGateway(host, backend string, allow map[string]bool, owner uint32) http.Handler {
	transport:= &http.Transport{DisableCompression:true, MaxIdleConnsPerHost:16, IdleConnTimeout:60*time.Second,
		ResponseHeaderTimeout:35*time.Second, MaxResponseHeaderBytes:65536,
		DialContext:func(ctx context.Context,_,_ string)(net.Conn,error){
			conn,err:=(&net.Dialer{Timeout:3*time.Second}).DialContext(ctx,"unix",backend); if err!=nil{return nil,err}
			uid,e:=peerUID(conn); if e!=nil || uid!=owner {conn.Close();return nil,errors.New("backend peer owner mismatch")};return conn,nil
		}}
	proxy:= &httputil.ReverseProxy{Transport:transport, ErrorLog:log.New(io.Discard,"",0),
		ErrorHandler:func(w http.ResponseWriter,_ *http.Request,_ error){http.Error(w,"workspace unavailable",http.StatusBadGateway)},
		Rewrite:func(p *httputil.ProxyRequest){
			p.SetURL(&url.URL{Scheme:"http",Host:"localhost"}); p.Out.Host="localhost"
			// Existing HTTP bearer or WebSocket subprotocol credentials pass through, never injected or logged.
			for k:=range p.Out.Header { n:=strings.ToLower(k);if strings.HasPrefix(n,"tailscale-") || strings.HasPrefix(n,"x-forwarded-") || n=="forwarded" {p.Out.Header.Del(k)} }
			if p.In.Header.Get("Origin")!="" {p.Out.Header.Set("Origin","http://localhost")}
		}}
	return http.HandlerFunc(func(w http.ResponseWriter,r *http.Request){
		w.Header().Set("Cache-Control","no-store")
		if trusted,_:=r.Context().Value(daemonPeer{}).(bool); !trusted {http.Error(w,"untrusted socket peer",403);return}
		login:=r.Header.Values("Tailscale-User-Login");if len(login)!=1 || !validLogin(login[0]) || !allow[login[0]] {http.Error(w,"tailnet access denied",403);return}
		if r.Method=="CONNECT" || r.URL.IsAbs() || r.Header.Get("Trailer")!="" || len(r.Trailer)!=0 {http.Error(w,"unsupported request",400);return}
		// tailscaled may use localhost as Host when dialing a Unix backend. The external origin is pinned, not inferred.
		if !strings.EqualFold(r.Host,host) && r.Host!="localhost" {http.Error(w,"unrecognized authority",403);return}
		origins:=r.Header.Values("Origin"); if len(origins)>1 || (len(origins)==1 && !strings.EqualFold(origins[0],"https://"+host)) {http.Error(w,"unrecognized origin",403);return}
		if r.URL.Path==manifestPath {
			if r.Method!="GET" && r.Method!="HEAD" {w.Header().Set("Allow","GET, HEAD");http.Error(w,"method not allowed",405);return}
			w.Header().Set("Content-Type","application/json");if r.Method=="GET" {_=json.NewEncoder(w).Encode(descriptor)};return
		}
		proxy.ServeHTTP(w,r)
	})
}

type daemonPeer struct{}

func serve(ctx context.Context,args []string) error {
	fs:=flag.NewFlagSet("serve",flag.ContinueOnError)
	listen:=fs.String("listen","","private socket for root tailscaled")
	backend:=fs.String("backend","","existing personal workspace Unix socket")
	host:=fs.String("host","","external MagicDNS authority, optionally with port")
	users:=fs.String("allow","","comma-separated exact Tailscale login names")
	if err:=fs.Parse(args);err!=nil{return err}; if fs.NArg()!=0{return errors.New("unexpected serve argument")}
	origin,err:=url.Parse("https://"+*host);if err!=nil || origin.User!=nil || origin.Host!=*host || origin.Path!="" || !validDNS(origin.Hostname()) || !strings.HasSuffix(origin.Hostname(),".ts.net") {return errors.New("invalid external MagicDNS authority")}
	if os.Geteuid()==0 {return errors.New("run workspace-tailnet as the unprivileged workspace owner, not as root")}
	uid:=uint32(os.Geteuid()); if err:=privatePath(*listen,uid);err!=nil{return err};if err:=privatePath(*backend,uid);err!=nil{return err};if *listen==*backend{return errors.New("listener and backend must differ")}
	allow:=map[string]bool{};for _,user:=range strings.Split(*users,","){if !validLogin(user) || allow[user] {return errors.New("invalid or duplicate allowed login")};allow[user]=true}
	// Refuse to unlink a live or stale socket automatically. Keep restrictive process umask throughout startup.
	syscall.Umask(0077)
	ln,err:=net.ListenUnix("unix",&net.UnixAddr{Name:*listen,Net:"unix"});if err!=nil{return err};defer ln.Close()
	srv:= &http.Server{Handler:makeGateway(*host,*backend,allow,uid), ReadHeaderTimeout:5*time.Second,IdleTimeout:60*time.Second,MaxHeaderBytes:32768,
		ConnContext:func(ctx context.Context,c net.Conn) context.Context {id,e:=peerUID(c);return context.WithValue(ctx,daemonPeer{},e==nil && id==0)}}
	go func(){<-ctx.Done();_ = srv.Close()}()
	err=srv.Serve(ln);if errors.Is(err,http.ErrServerClosed) {return nil};return err
}

// Copyright (c) Royal Apps. Licensed under the MIT license.
package main

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"mime"
	"net"
	"net/http"
	"net/netip"
	"slices"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"
)

var (
	tail4 = netip.MustParsePrefix("100.64.0.0/10")
	tail6 = netip.MustParsePrefix("fd7a:115c:a1e0::/48")
)

type peer struct {
	DNSName string
	Online bool
	TailscaleIPs []string
}

type status struct {
	BackendState string
	CurrentTailnet struct{ MagicDNSSuffix string }
	MagicDNSSuffix string
	Self *peer
	Peer map[string]*peer
}

type candidate struct {
	Name string `json:"name"`
	Endpoint string `json:"endpoint"`
	Verified bool `json:"verified"`
	addresses []netip.Addr
}

func validDNS(s string) bool {
	if len(s)==0 || len(s)>253 { return false }
	for _,label:=range strings.Split(s,".") {
		if len(label)==0 || len(label)>63 || label[0]=='-' || label[len(label)-1]=='-' { return false }
		for _,c:=range label {if !(c>='a'&&c<='z' || c>='0'&&c<='9' || c=='-') { return false }}
	}
	return true
}

func limited(r io.Reader,max int64)([]byte,error){
	b,err:=io.ReadAll(io.LimitReader(r,max+1)); if err!=nil{return nil,err};if int64(len(b))>max{return nil,errors.New("input exceeds size limit")};return b,nil
}

func candidates(r io.Reader,port int)([]candidate,error){
	if port<1 || port>65535 {return nil,errors.New("invalid port")}
	data,err:=limited(r,4<<20);if err!=nil{return nil,err}
	var s status;if err=json.Unmarshal(data,&s);err!=nil{return nil,err}
	if s.BackendState!="Running" {return nil,errors.New("local tailscaled is not running")}
	suffix:=s.CurrentTailnet.MagicDNSSuffix;if suffix=="" {suffix=s.MagicDNSSuffix};suffix=strings.TrimSuffix(strings.ToLower(suffix),".")
	if !validDNS(suffix) || !strings.HasSuffix(suffix,".ts.net") {return nil,errors.New("status lacks a supported MagicDNS tailnet suffix")}
	found:=map[string]candidate{};ambiguous:=map[string]bool{}
	add:=func(p *peer){
		if p==nil || !p.Online {return};name:=strings.TrimSuffix(strings.ToLower(p.DNSName),".");label,ok:=strings.CutSuffix(name,"."+suffix)
		if !ok || strings.Contains(label,".") || !validDNS(name) {return}
		var addresses []netip.Addr
		for _,raw:=range p.TailscaleIPs {ip,err:=netip.ParseAddr(raw);if err==nil && ip.Zone()=="" && (tail4.Contains(ip)||tail6.Contains(ip)) && !slices.Contains(addresses,ip) && len(addresses)<4 {addresses=append(addresses,ip)}}
		if len(addresses)==0 {return};slices.SortFunc(addresses,func(a,b netip.Addr)int{return a.Compare(b)})
		if old,exists:=found[name];exists && !slices.Equal(old.addresses,addresses){ambiguous[name]=true;return}
		authority:=name;if port!=443 {authority=net.JoinHostPort(name,strconv.Itoa(port))};found[name]=candidate{Name:name,Endpoint:"https://"+authority,addresses:addresses}
	}
	add(s.Self);for _,p:=range s.Peer{add(p)}
	out:=make([]candidate,0,len(found));for k,c:=range found{if !ambiguous[k]{out=append(out,c)}}
	sort.Slice(out,func(i,j int)bool{return out[i].Name<out[j].Name});return out,nil
}

func verify(ctx context.Context,c candidate,port int)bool{
	// Preserve the MagicDNS TLS name and certificate verification, but dial only addresses from this local peer record.
	t:=&http.Transport{Proxy:nil,DisableKeepAlives:true,DisableCompression:true,MaxResponseHeaderBytes:16<<10,
		DialContext:func(ctx context.Context,_,_ string)(net.Conn,error){
			for _,ip:=range c.addresses{conn,err:=(&net.Dialer{Timeout:700*time.Millisecond}).DialContext(ctx,"tcp",net.JoinHostPort(ip.String(),strconv.Itoa(port)));if err==nil{return conn,nil};if ctx.Err()!=nil{break}};return nil,errors.New("peer unavailable")
		}}
	defer t.CloseIdleConnections()
	client:=&http.Client{Transport:t,Timeout:3*time.Second,CheckRedirect:func(_ *http.Request,_ []*http.Request)error{return http.ErrUseLastResponse}}
	req,err:=http.NewRequestWithContext(ctx,http.MethodGet,c.Endpoint+manifestPath,nil);if err!=nil{return false}
	resp,err:=client.Do(req);if err!=nil{return false};defer resp.Body.Close()
	return validManifest(resp)
}

func validManifest(resp *http.Response)bool{
	if resp.StatusCode!=200{return false};media,_,err:=mime.ParseMediaType(resp.Header.Get("Content-Type"));if err!=nil||media!="application/json" {return false}
	b,err:=limited(resp.Body,4096);if err!=nil{return false};var got manifest;return json.Unmarshal(b,&got)==nil && got==descriptor
}

func discover(ctx context.Context,args []string, in io.Reader,out io.Writer)error{
	fs:=flag.NewFlagSet("discover",flag.ContinueOnError);active:=fs.Bool("probe",false,"verify up to 128 eligible peers over TLS");port:=fs.Int("port",443,"Serve HTTPS port");if err:=fs.Parse(args);err!=nil{return err};if fs.NArg()!=0{return errors.New("discover reads tailscale status --json from stdin")}
	items,err:=candidates(in,*port);if err!=nil{return err}
	if *active {
		if len(items)>128 {return fmt.Errorf("refusing to probe %d peers (maximum 128); narrow the local status input",len(items))}
		deadline,cancel:=context.WithTimeout(ctx,30*time.Second);defer cancel()
		jobs:=make(chan int);var workers sync.WaitGroup
		for range min(6,len(items)){workers.Add(1);go func(){defer workers.Done();for i:=range jobs{items[i].Verified=verify(deadline,items[i],*port)}}()}
		for i:=range items {if deadline.Err()!=nil{break};jobs<-i};close(jobs);workers.Wait();if err:=deadline.Err();err!=nil{return err}
		items=slices.DeleteFunc(items,func(c candidate)bool{return !c.Verified})
	}
	if err:=ctx.Err();err!=nil{return err};return json.NewEncoder(out).Encode(items)
}

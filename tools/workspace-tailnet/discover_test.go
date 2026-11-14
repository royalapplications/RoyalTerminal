// Copyright (c) Royal Apps. Licensed under the MIT license.
package main

import (
 "context"
 "io"
 "net/http"
 "strings"
 "testing"
)

func TestCandidateSelectionAndAmbiguousNames(t *testing.T) {
 const input=`{"BackendState":"Running","CurrentTailnet":{"MagicDNSSuffix":"mine.ts.net"},"Peer":{
 "1":{"DNSName":"DEV.mine.ts.net.","Online":true,"TailscaleIPs":["127.0.0.1","100.64.1.2","8.8.8.8","fd7a:115c:a1e0::1"]},
 "2":{"DNSName":"other.mine.ts.net.attacker.invalid","Online":true,"TailscaleIPs":["100.64.1.3"]},
 "3":{"DNSName":"evil.mine.ts.net","Online":true,"TailscaleIPs":["192.168.1.1"]},
 "4":{"DNSName":"off.mine.ts.net","Online":false,"TailscaleIPs":["100.64.1.3"]},
 "5":{"DNSName":"dup.mine.ts.net","Online":true,"TailscaleIPs":["100.64.1.3"]},
 "6":{"DNSName":"DUP.mine.ts.net.","Online":true,"TailscaleIPs":["100.64.1.4"]}}}`
 got,err:=candidates(strings.NewReader(input),8443);if err!=nil {t.Fatal(err)};if len(got)!=1 || got[0].Endpoint!="https://dev.mine.ts.net:8443" || len(got[0].addresses)!=2 || got[0].Verified {t.Fatalf("bad candidates: %+v",got)}
 var output strings.Builder;if err:=discover(context.Background(),nil,strings.NewReader(input),&output);err!=nil || strings.Contains(output.String(),"100.64") || !strings.Contains(output.String(),`"verified":false`) {t.Fatalf("unexpected passive discovery: %s %v",&output,err)}
}
func TestRejectInvalidStatus(t *testing.T){for _,s:=range []string{"",`{"BackendState":"Stopped"}`,`{"BackendState":"Running","MagicDNSSuffix":"localhost"}`,strings.Repeat(" ",4<<20+1)}{if _,err:=candidates(strings.NewReader(s),443);err==nil{t.Fatal("expected rejection")}}}
func TestVerifiedManifestRequiresTypedBoundedResponse(t *testing.T){for _,c:=range []struct{code int;media,body string;ok bool}{
 {200,"application/json",`{"service":"royalterminal-workspace","version":1,"auth":"bearer"}`,true},
 {301,"application/json","{}",false},{200,"text/plain",`{"service":"royalterminal-workspace","version":1,"auth":"bearer"}`,false},
 {200,"application/json",`{"service":"royalterminal-workspace","version":2,"auth":"bearer"}`,false},{200,"application/json",strings.Repeat(" ",4097),false},
 }{r:=&http.Response{StatusCode:c.code,Header:http.Header{"Content-Type":[]string{c.media}},Body:io.NopCloser(strings.NewReader(c.body))};if validManifest(r)!=c.ok{t.Errorf("unexpected result for %d %s",c.code,c.media)}}}

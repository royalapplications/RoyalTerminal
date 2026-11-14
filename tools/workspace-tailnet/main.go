// Copyright (c) Royal Apps. Licensed under the MIT license.
// Command workspace-tailnet discovers opted-in workspace gateways or runs a Linux Serve gateway.
package main

import (
	"context"
	"errors"
	"fmt"
	"os"
	"os/signal"
	"syscall"
)

const manifestPath = "/.well-known/royalterminal-workspace"

type manifest struct {
	Service string `json:"service"`
	Version int `json:"version"`
	Auth string `json:"auth"`
}

var descriptor = manifest{"royalterminal-workspace", 1, "bearer"}

func main() {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	var err error
	switch {
	case len(os.Args) > 1 && os.Args[1] == "serve":
		err = serve(ctx, os.Args[2:])
	case len(os.Args) > 1 && os.Args[1] == "discover":
		err = discover(ctx, os.Args[2:], os.Stdin, os.Stdout)
	default:
		err = errors.New("usage: workspace-tailnet serve -listen PATH -backend PATH -host FQDN -allow LOGIN[,LOGIN...] | discover [-probe] [-port PORT] < tailscale-status.json")
	}
	if err != nil { fmt.Fprintln(os.Stderr, err); os.Exit(1) }
}

//go:build !linux

// Copyright (c) Royal Apps. Licensed under the MIT license.
package main

import ("context";"errors")

func serve(context.Context, []string) error { return errors.New("the Serve gateway requires Linux SO_PEERCRED; discovery is available on this platform") }

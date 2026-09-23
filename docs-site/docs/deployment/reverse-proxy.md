---
sidebar_position: 3
title: Reverse Proxy
---

# Reverse Proxy Configuration

Weir is designed for a single-process, single-instance deployment. Production deployments should expose one canonical HTTPS origin.

## Trusted proxies

Weir ignores `X-Forwarded-For` unless `WEIR_TRUSTED_PROXY_IPS` is configured:

```
WEIR_TRUSTED_PROXY_IPS=172.18.0.1,10.0.0.0/24
```

When the immediate peer is trusted, Weir uses the right-most untrusted address in `X-Forwarded-For` as the client key. Forwarded headers from untrusted peers are ignored.

## HTTPS and the sign-in cookie

`WEIR_SESSION_COOKIE_SECURE` defaults to `auto`: the sign-in cookie is marked HTTPS-only when a
request arrives over HTTPS, either directly or through a proxy listed in `WEIR_TRUSTED_PROXY_IPS`
that sends `X-Forwarded-Proto: https`. Behind a proxy that terminates HTTPS, setting
`WEIR_TRUSTED_PROXY_IPS` is enough. Set `WEIR_SESSION_COOKIE_SECURE=true` only to force it.
The Windows tray app is the exception: it always starts the server with
`WEIR_SESSION_COOKIE_SECURE=false`.

Don't force it on an install you also reach over plain HTTP. The browser discards an HTTPS-only
cookie on an HTTP page, so sign-in sends you straight back to the login page.

## CORS

Credentialed browser requests require explicit origins. Setting `WEIR_CORS_ORIGINS=*` is rejected at startup.

For split-origin deployments (static site and API on different origins):

- Use HTTPS everywhere
- Set `WEIR_CORS_ORIGINS` to the real web origin
- Set `WEIR_TRUSTED_BROWSER_ORIGINS` if stricter POST checks are needed
- Set `WEIR_SESSION_COOKIE_SAMESITE=none` and `WEIR_SESSION_COOKIE_SECURE=true`: credentialed cross-origin requests need a `SameSite=None; Secure` cookie

## Rate limiting

Login and bootstrap rate limiting is process-local memory. This is correct only because the supported runtime is single-process. Multiple app processes would each have their own rate-limit buckets.

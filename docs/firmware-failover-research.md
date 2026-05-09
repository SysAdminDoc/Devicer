# Samsung firmware download — failover sources

Research dated 2026-05-09 against live endpoints. Findings inform Devicer v0.3.2's
"automatic failover" feature: when Samsung's FUS CDN refuses a download
(geofence, rate-limit, regional ACL deny), Devicer can deep-link the user to a
working public mirror instead of leaving them stuck.

## Why we need failover at all

Samsung's primary firmware-download host (`cloud-neofussvr.sslcs.cdngc.net`)
enforces a region-IP ACL at the Squid edge. From a US IP, EUX/EUR/DBT firmware
returns HTTP 403 "Invalid Request / access control configuration prevents your
request at this time" — even with a valid IMEI, valid auth signature, and
correct LOGIC_CHECK. This isn't a Devicer bug; every public FUS-protocol
client (samloader, SamFirm, Frija, Bifrost / SamloaderKotlin) fails the same
way under the same conditions.

The fix that doesn't involve a VPN: route the user to a community mirror that
already pulled the firmware down to a non-region-locked CDN.

## Mirror inventory (verified 2026-05-09)

| Mirror | Status | Reachable | Auth gate | Geofence | Notes |
|---|---|---|---|---|---|
| **SamMobile** ([sammobile.com](https://www.sammobile.com/)) | active | ✓ HTTP 200 | free login required | no | listed every SM-S938B/EUX build; deep-link works |
| **SamFW** ([samfw.com](https://samfw.com/)) | active | Cloudflare Turnstile | browser challenge | no | full Cloudflare; not scriptable, but user's browser handles it |
| **SamFrew** ([samfrew.com](https://samfrew.com/)) | active | ✓ Next.js page | Clerk auth on download | no | listings public, downloads behind auth |
| **SamFirms** ([samfirms.com](https://samfirms.com/)) | active | ✓ | none | no | smaller catalog, ad-heavy |
| **galaxyfirmware.com** | active | ✓ | partial | no | JS-rendered listings |
| **firmwarefile.com** | active | ✓ Sucuri | partial | no | hit-or-miss for new models |
| **Frija** (Windows .NET tool) | active | n/a | uses FUS protocol | **yes (same as us)** | not a failover; geofences identically |
| **Bifrost / SamloaderKotlin** | active | n/a | uses FUS protocol | **yes (same as us)** | not a failover |
| **SamFirm_Reborn** | active | n/a | uses FUS protocol | **yes (same as us)** | not a failover |
| **updato.com** | **dead** | ✗ HTTP 521 | n/a | n/a | Cloudflare origin offline as of probe |
| **Internet Archive** | partial | ✓ | none | no | hit-or-miss; community uploads, not authoritative |

The **only category that bypasses the FUS-protocol geofence is the website
mirrors** — they pulled the firmware once into their own CDN and serve it
without region checks. Every protocol-level client (Frija, Bifrost, samloader,
us) inherits Samsung's geofence because they all hit the same `cloud-neofussvr`
edge.

## Constructable URL patterns

These URLs deep-link directly to the model+region firmware list. The user
clicks → mirror's site renders → user picks a build → downloads via browser.

```text
SamMobile : https://www.sammobile.com/firmwares/database/<MODEL>/<CSC>/
            (301 → /samsung/<model-slug>/firmware/<MODEL>/<CSC>/)

SamFW     : https://samfw.com/firmware/<MODEL>/<CSC>

SamFrew   : https://samfrew.com/firmware/model/<MODEL>/region/<CSC>/upload/Desc/0/10

SamFirms  : https://samfirms.com/?s=<MODEL>%20<CSC>
```

For Devicer's currently-connected S25 Ultra (`SM-S938B` / `EUX`):
- <https://www.sammobile.com/firmwares/database/SM-S938B/EUX/>
- <https://samfw.com/firmware/SM-S938B/EUX>
- <https://samfrew.com/firmware/model/SM-S938B/region/EUX/upload/Desc/0/10>

All three resolve to the same set of builds the user's FUS query just
returned (PDA `S938BXXS9BZCH` etc.).

## Tradeoffs vs. native FUS

| dimension | Native FUS (Devicer) | Mirror deep-link |
|---|---|---|
| auth | Samsung-direct, no third-party | trust the mirror operator |
| encryption | server-side AES-128-ECB; we decrypt locally | depends on mirror — usually serves the already-decrypted ZIP |
| integrity | SHA256 inferred from blob length + LOGIC_CHECK | mirror typically publishes its own MD5/SHA |
| speed | direct from Samsung CDN | mirror's CDN; sometimes slower or rate-limited |
| ads / paywall | none | most mirrors are ad-supported; SamMobile gates free tier behind login |
| region lock | **YES — primary failure mode** | **NO** — mirrors don't geofence |
| version coverage | every published build | mirrors usually carry latest + last 6-12 months |

## Recommended Devicer integration

**v0.3.2** — when `FusErrorClassifier.Classify(...)` returns the
`Samsung CDN geographic restriction` error, augment the diagnostic banner with
a row of "Open in browser" buttons that deep-link to the three primary mirrors
(SamMobile, SamFW, SamFrew) pre-filled with the user's `MODEL` and `CSC`.

That requires no FUS-protocol changes and no new external deps — just URL
construction and `Process.Start("https://...")`.

**v0.3.3** — Settings page gains a SOCKS5 / HTTP-proxy field. When set, the
FUS HttpClient routes through it. This lets a user with a UK/EU VPN endpoint
just point Devicer at their proxy and have the native flow work end-to-end.

**v0.3.4 (stretch)** — Optional integration with [archive.org Wayback
Machine](https://archive.org/web/) as a last-resort failover for very old
firmware builds that the mainline mirrors have rotated off.

## Caveats / safety

1. **Trust** — mirrors are third-party. They pull the firmware via the same
   FUS protocol then re-host. If a mirror is compromised, malicious firmware
   could be served. Always cross-check the SHA256 from at least two mirrors
   before flashing.
2. **Login walls** — SamMobile and SamFrew require account creation for
   anything past the latest build. Free, but it's an account.
3. **Cloudflare** — SamFW is fully Cloudflare-protected (Turnstile +
   bot-detection). The deep-link works in a normal browser; programmatic
   downloads from Devicer will be challenged.
4. **Mirror lifetime** — historically these sites cycle. `updato.com` is dead
   as of this probe; `mobifirmware`, `samfirmware`, `getdroidtips` come and go.
   Devicer should encode the URL templates as configurable rather than
   hardcoded so a future user can swap in a working mirror without recompiling.

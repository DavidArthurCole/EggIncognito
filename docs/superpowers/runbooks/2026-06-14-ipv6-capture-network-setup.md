# IPv6 Capture Network Setup (one-time)

Proven on the wire 2026-06-14. Routes the public /64 into the WG tunnel so per-user destination
addresses survive to frame. No DNAT, no per-session binds.

Prefix: `2a01:4f8:c012:e15b::/64`. VPS public v4 `88.99.81.179`, WG `10.8.0.1` / `fd00:8::1`.
frame WG `10.8.0.2` / `fd00:8::2`.

## VPS (capture-relay)
1. `wg set wg0 peer <FRAME_PUBKEY> allowed-ips 10.8.0.2/32,fd00:8::2/128,2a01:4f8:c012:e15b::/64`
   Persist in `/etc/wireguard/wg0.conf` peer AllowedIPs.
2. `ip -6 route replace 2a01:4f8:c012:e15b::/64 dev wg0`. Persist via wg `PostUp`.
3. nft forward chain already accepts eth0->wg0 tcp dport 8443; NO DNAT rule for the /64.

## frame
1. `wg set wg0 peer <VPS_PUBKEY> allowed-ips 10.8.0.1/32,fd00:8::1/128,2a01:4f8:c012:e15b::/64`
   Persist in `/etc/wireguard/wg0.conf`.
2. `ip -6 route replace local 2a01:4f8:c012:e15b::/64 dev lo` (AnyIP: accept the whole /64). Persist via wg `PostUp`.
3. Front-door container MUST run host-network so it reads the original destination address. Switch the
   stack service to `network_mode: host`. A bridged Docker publish rewrites the destination and breaks identity.

## Verify
On frame, listen and confirm getsockname sees the per-user dest:
```
python3 -c 'import socket;s=socket.socket(socket.AF_INET6,socket.SOCK_STREAM);s.bind(("::",8443));s.listen(1);c,a=s.accept();print(c.getsockname())'
```
Hit `[2a01:4f8:c012:e15b::<derived>]:8443` from a phone over cellular; expect the derived address printed.
Requires the device to have working IPv6.

## Env (frame stack)
`Capture__Ipv6Prefix=2a01:4f8:c012:e15b::/64`, `Capture__AddressSecret=<fixed random secret>`.
Rotating AddressSecret changes every user's address; treat as fixed.

## Why no DNAT
DNAT rewrites the destination to the inner target before frame sees it, destroying the per-user
identity (frame's getsockname would read the DNAT target). Plain routing preserves the original
destination all the way to the front-door socket.

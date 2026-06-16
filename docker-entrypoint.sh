#!/bin/sh
# Hosted-capture per-user IPv6: the host routes Capture__Ipv6Prefix to this container; the container
# kernel must accept those destinations (AnyIP) so the front-door socket reads the real per-user dest
# instead of dropping the packet. Added here, not on the host, so it survives container recreation.
# Needs NET_ADMIN (cap_add in compose). Best-effort: if hosted capture is off, the prefix is unset, or
# the cap is missing, log and start the app anyway (local/self-host mode does not use this path).
#
# AnyIP routes onto lo, not a named interface: loopback always exists and its name never changes, so
# this works whether the container is bridged (eth0) or host-network (the host's enpXsY). CAPTURE_IFACE
# overrides for the rare setup that needs a specific device.
CAPTURE_IFACE="${CAPTURE_IFACE:-lo}"

if [ "${HostedCaptureEnabled}" = "true" ] && [ -n "${Capture__Ipv6Prefix}" ]; then
    if ip -6 route replace local "${Capture__Ipv6Prefix}" dev "${CAPTURE_IFACE}" 2>/tmp/anyip.err; then
        echo "capture-anyip: accepting ${Capture__Ipv6Prefix} on ${CAPTURE_IFACE}"
    else
        echo "capture-anyip: FAILED to add local route for ${Capture__Ipv6Prefix} (need NET_ADMIN?): $(cat /tmp/anyip.err)" >&2
    fi
fi

exec dotnet EggIncognito.dll "$@"

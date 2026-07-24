#!/bin/sh
#
CAPTURE_IFACE="${CAPTURE_IFACE:-lo}"

if [ "${HostedCaptureEnabled}" = "true" ] && [ -n "${Capture__Ipv6Prefix}" ]; then
    if ip -6 route replace local "${Capture__Ipv6Prefix}" dev "${CAPTURE_IFACE}" 2>/tmp/anyip.err; then
        echo "capture-anyip: accepting ${Capture__Ipv6Prefix} on ${CAPTURE_IFACE}"
    else
        echo "capture-anyip: FAILED to add local route for ${Capture__Ipv6Prefix} (need NET_ADMIN?): $(cat /tmp/anyip.err)" >&2
    fi
fi

exec dotnet EggIncognito.dll "$@"

FROM debian:bookworm-slim

ARG SIGNALWIRE_PAT

# Install FreeSWITCH from SignalWire's authenticated Debian Bookworm repo.
# The PAT is used only during build and wiped from auth.conf in the same layer.
# No SIP provider dependency — only using SignalWire as a package distributor.
RUN apt-get update && apt-get install -y --no-install-recommends \
    gnupg2 wget ca-certificates \
    && wget --quiet \
       --http-user=signalwire \
       --http-password=${SIGNALWIRE_PAT} \
       -O /usr/share/keyrings/signalwire-freeswitch-repo.gpg \
       https://freeswitch.signalwire.com/repo/deb/debian-release/signalwire-freeswitch-repo.gpg \
    && echo "machine freeswitch.signalwire.com login signalwire password ${SIGNALWIRE_PAT}" \
       > /etc/apt/auth.conf \
    && chmod 600 /etc/apt/auth.conf \
    && echo "deb [signed-by=/usr/share/keyrings/signalwire-freeswitch-repo.gpg] \
https://freeswitch.signalwire.com/repo/deb/debian-release/ bookworm main" \
       > /etc/apt/sources.list.d/freeswitch.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
       freeswitch \
       freeswitch-mod-sofia \
       freeswitch-mod-event-socket \
       freeswitch-mod-commands \
       freeswitch-mod-dptools \
       freeswitch-mod-dialplan-xml \
       freeswitch-mod-console \
       freeswitch-mod-logfile \
       freeswitch-mod-xml-curl \
       freeswitch-mod-opus \
       freeswitch-mod-tone-stream \
       freeswitch-mod-sndfile \
       freeswitch-mod-native-file \
       freeswitch-mod-local-stream \
       freeswitch-sounds-en-us-callie \
       freeswitch-music-default \
    && rm -rf /var/lib/apt/lists/* /etc/apt/auth.conf

EXPOSE 5060/udp 5060/tcp 5080/udp 5080/tcp 8021/tcp 16384-16394/udp

ENTRYPOINT ["/usr/bin/freeswitch", "-nc", "-nf"]

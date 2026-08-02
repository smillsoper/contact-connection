# ── Stage 1: FreeSWITCH core headers + libfreeswitch.so + pkgconfig ──────────
# SignalWire's binary package repo ships runtime libs and pre-built modules only,
# no "-dev"/headers package at all, so third-party modules (mod_audio_stream) have
# nothing to build against. This stage compiles just enough of FreeSWITCH itself
# (core library, no optional modules — modules.conf emptied) to produce those
# headers/pkgconfig/lib. Discarded after Stage 2 copies out the compiled module;
# none of this toolchain or source tree reaches the final runtime image.
FROM debian:bookworm-slim AS fsheaders

RUN apt-get update && apt-get install -y --no-install-recommends \
    git ca-certificates build-essential autoconf automake libtool libtool-bin \
    pkg-config cmake wget \
    uuid-dev libssl-dev libsqlite3-dev libcurl4-openssl-dev \
    libspeex-dev libspeexdsp-dev libedit-dev zlib1g-dev \
    libpcre2-dev libapr1-dev libaprutil1-dev \
    libjpeg62-turbo-dev libtiff-dev yasm nasm \
    && rm -rf /var/lib/apt/lists/*

# spandsp >= 3.0 — unconditional hard dependency of FreeSWITCH's own configure.ac
# (checked even though we never use fax functionality), not published as -dev
# anywhere. github.com/freeswitch/spandsp has no v3.0.0 tag matching the exact
# commit SignalWire's runtime package was built from — v3.1.1 (latest stable tag)
# satisfies the ">= 3.0" check and is what we need headers/pkgconfig from, not the
# runtime binary itself (the real libspandsp3 already in the final image is what
# actually gets linked against at container runtime).
WORKDIR /src
RUN git clone --depth 1 --branch v3.1.1 https://github.com/freeswitch/spandsp.git
WORKDIR /src/spandsp
RUN ./bootstrap.sh > /tmp/spandsp-bootstrap.log 2>&1 || (tail -150 /tmp/spandsp-bootstrap.log; exit 1)
RUN ./configure --prefix=/usr > /tmp/spandsp-configure.log 2>&1 || (tail -150 /tmp/spandsp-configure.log; exit 1)
RUN make -j"$(nproc)" > /tmp/spandsp-make.log 2>&1 || (tail -150 /tmp/spandsp-make.log; exit 1)
RUN make install > /tmp/spandsp-install.log 2>&1 || (tail -150 /tmp/spandsp-install.log; exit 1)
RUN ldconfig

# sofia-sip-ua >= 1.13.17 — same situation as spandsp: unconditional hard
# dependency, no -dev package published. v1.13.17 is an exact tag match.
WORKDIR /src
RUN git clone --depth 1 --branch v1.13.17 https://github.com/freeswitch/sofia-sip.git
WORKDIR /src/sofia-sip
RUN ./bootstrap.sh > /tmp/sofia-bootstrap.log 2>&1 || (tail -150 /tmp/sofia-bootstrap.log; exit 1)
RUN ./configure --prefix=/usr > /tmp/sofia-configure.log 2>&1 || (tail -150 /tmp/sofia-configure.log; exit 1)
RUN make -j"$(nproc)" > /tmp/sofia-make.log 2>&1 || (tail -150 /tmp/sofia-make.log; exit 1)
RUN make install > /tmp/sofia-install.log 2>&1 || (tail -150 /tmp/sofia-install.log; exit 1)
RUN ldconfig

# FreeSWITCH core itself — commit c2c59645f6... (v1.11.1 tag) is the exact source
# our installed binary package (1.11.1-release-26476433763-c2c59645f6) was built
# from. modules.conf emptied — we only need the core lib + headers + pkgconfig,
# not any of FreeSWITCH's own modules (we already have those via the binary
# package in the final stage).
WORKDIR /src
RUN git clone --depth 1 --branch v1.11.1 https://github.com/signalwire/freeswitch.git
WORKDIR /src/freeswitch
RUN printf '' > modules.conf
RUN ./bootstrap.sh -j > /tmp/fs-bootstrap.log 2>&1 || (tail -150 /tmp/fs-bootstrap.log; exit 1)
RUN ./configure --prefix=/usr > /tmp/fs-configure.log 2>&1 || (tail -200 /tmp/fs-configure.log; exit 1)
RUN make -j"$(nproc)" > /tmp/fs-make.log 2>&1 || (tail -200 /tmp/fs-make.log; exit 1)
RUN make install > /tmp/fs-install.log 2>&1 || (tail -150 /tmp/fs-install.log; exit 1)

# ── Stage 2: mod_audio_stream ─────────────────────────────────────────────────
# MIT-licensed, genuinely bidirectional fork (unlike upstream amigniter/mod_audio_
# stream, whose playback-into-call feature is a paid commercial add-on capped at
# 10 concurrent channels). Attaches a media bug and streams call audio to a
# WebSocket server we run; that server can send audio back for FreeSWITCH to play
# into the call — this is what lets externally-synthesized TTS (Azure/ElevenLabs/
# etc.) reach a live call without writing a file to disk first.
FROM fsheaders AS masbuilder

WORKDIR /src
RUN git clone --depth 1 --recurse-submodules https://github.com/sptmru/freeswitch_mod_audio_stream.git
WORKDIR /src/freeswitch_mod_audio_stream
RUN mkdir build
WORKDIR /src/freeswitch_mod_audio_stream/build
RUN cmake -DCMAKE_BUILD_TYPE=Release .. > /tmp/mas-cmake.log 2>&1 || (tail -150 /tmp/mas-cmake.log; exit 1)
RUN make -j"$(nproc)" > /tmp/mas-make.log 2>&1 || (tail -200 /tmp/mas-make.log; exit 1)

# ── Stage 3: runtime image ────────────────────────────────────────────────────
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
       freeswitch-mod-flite \
       freeswitch-mod-dialplan-xml \
       freeswitch-mod-console \
       freeswitch-mod-logfile \
       freeswitch-mod-xml-curl \
       freeswitch-mod-opus \
       freeswitch-mod-tone-stream \
       freeswitch-mod-sndfile \
       freeswitch-mod-shout \
       freeswitch-mod-native-file \
       freeswitch-mod-local-stream \
       freeswitch-mod-loopback \
       freeswitch-sounds-en-us-callie \
       freeswitch-music-default \
    && rm -rf /var/lib/apt/lists/* /etc/apt/auth.conf

# mod_audio_stream — every shared library it links against (libspandsp3,
# libsofia-sip-ua0, libspeex1, libspeexdsp1, etc.) already lands here as a
# transitive dependency of the freeswitch package above; verified via ldd
# against this exact image with zero unresolved libraries.
COPY --from=masbuilder /src/freeswitch_mod_audio_stream/build/mod_audio_stream.so /usr/lib/freeswitch/mod/mod_audio_stream.so

EXPOSE 5060/udp 5060/tcp 5080/udp 5080/tcp 8021/tcp 16384-16394/udp

ENTRYPOINT ["/usr/bin/freeswitch", "-nc", "-nf"]

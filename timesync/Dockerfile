# cc_timesync -- tiny chrony sidecar that keeps the shared Docker/WSL2 VM kernel
# clock accurate. See chrony.conf for the why. Deliberately NOT folded into the
# FreeSWITCH image: that image is a PAT-gated multi-stage build and a clock
# helper has no business triggering a rebuild of it.
FROM alpine:3.20

RUN apk add --no-cache chrony \
    && mkdir -p /var/lib/chrony /var/log/chrony

COPY chrony.conf /etc/chrony/chrony.conf

# -d: run in foreground and log to stderr so `docker logs cc_timesync` shows
# every step/slew. Clock control stays enabled (unlike -Q / -x).
ENTRYPOINT ["chronyd", "-d", "-f", "/etc/chrony/chrony.conf"]

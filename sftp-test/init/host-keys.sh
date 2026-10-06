#!/bin/bash
# Install the persistent host keys with the permissions sshd requires (Windows bind mounts are 0777).
for k in /tmp/host-keys/ssh_host_ed25519_key /tmp/host-keys/ssh_host_rsa_key; do
  install -m 600 -o root -g root "$k" /etc/ssh/$(basename "$k")
  install -m 644 -o root -g root "$k.pub" /etc/ssh/$(basename "$k").pub
done

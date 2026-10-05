# Caddy with ProjectHub's Caddyfile baked in, for installations without a repository checkout on the server
# (Portainer, docker-compose.portainer.yml, ADR 0019). Build from deploy/: docker build -f caddy.Dockerfile .
FROM caddy:2-alpine
COPY Caddyfile /etc/caddy/Caddyfile

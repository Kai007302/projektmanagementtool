# ProjectHub web app: production build served by nginx without root (ADR 0012).
# Build from src/ProjectHub.Web: docker build -t projecthub-web .
FROM node:26-alpine AS build
WORKDIR /web
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
# true only for test images: lets the end-to-end tests pick synthetic users (src/identity/devUser.ts).
ARG VITE_DEV_IDENTITY=false
RUN VITE_DEV_IDENTITY=$VITE_DEV_IDENTITY npm run build && node deploy/nginx-headers.mjs > security-headers.conf

FROM nginxinc/nginx-unprivileged:1.31-alpine
# Base image updates can lag behind Alpine's security fixes.
USER root
RUN apk upgrade --no-cache
USER 101
ENV PROJECTHUB_API_UPSTREAM=http://api:8080
COPY deploy/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /web/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 8080

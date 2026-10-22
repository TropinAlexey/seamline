FROM node:24-alpine AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

# Unprivileged variant: runs as a non-root user and listens on 8080.
FROM nginxinc/nginx-unprivileged:1.29-alpine
ENV API_UPSTREAM=api:8080
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /src/dist /usr/share/nginx/html
EXPOSE 8080

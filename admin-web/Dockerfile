FROM node:24-alpine AS build
WORKDIR /app

COPY admin-web/package.json admin-web/package-lock.json* ./
RUN npm install

COPY admin-web/ ./
RUN npm run build

FROM nginx:alpine AS runtime
COPY admin-web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --retries=10 \
  CMD wget -q -O /dev/null http://127.0.0.1:8080/healthz || exit 1

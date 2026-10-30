FROM node:20-alpine AS build
WORKDIR /app
ARG VITE_DEMO_MODE=true
ARG VITE_HR_API_BASE_URL=/hr-api
ARG VITE_LEAVE_API_BASE_URL=/leave-api
ENV VITE_DEMO_MODE=$VITE_DEMO_MODE \
    VITE_HR_API_BASE_URL=$VITE_HR_API_BASE_URL \
    VITE_LEAVE_API_BASE_URL=$VITE_LEAVE_API_BASE_URL
COPY package*.json ./
# npm install also generates a lockfile in this build context when none is supplied.
RUN npm install --no-audit --no-fund
COPY . .
RUN npm run build

FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80

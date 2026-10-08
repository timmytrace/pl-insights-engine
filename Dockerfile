# One image: the React overlay is built, then served by the ASP.NET Core API alongside the
# REST endpoints and the replay WebSocket, so everything lives on one origin.

FROM node:22-alpine AS web
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS api
WORKDIR /src
COPY backend/ ./
RUN dotnet publish src/Studio.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Studio.Api.dll"]

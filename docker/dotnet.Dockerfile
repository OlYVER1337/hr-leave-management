# Dockerfile dùng chung cho các service .NET (HR Service, Leave Service).
# Context = thư mục của service; truyền API_PROJECT và API_DLL quabuild args.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG API_PROJECT
WORKDIR /src
COPY . .
RUN dotnet restore "$API_PROJECT"
RUN dotnet publish "$API_PROJECT" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG API_DLL
ENV ASPNETCORE_URLS=http://+:8080 \
    API_DLL=${API_DLL}
WORKDIR /app
# curl phục vụ healthcheck trong docker-compose
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "dotnet $API_DLL"]

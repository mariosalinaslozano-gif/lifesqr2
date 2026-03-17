FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        tzdata \
        libicu-dev \
        libpq5 \
        postgresql-client \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        tzdata \
        libicu-dev \
        libpq-dev \
        postgresql-client \
    && rm -rf /var/lib/apt/lists/*
COPY ["backend/AspNetVbApp.vbproj", "backend/"]
RUN dotnet restore "backend/AspNetVbApp.vbproj"
COPY backend/ backend/
COPY frontend/ frontend/
RUN dotnet build "backend/AspNetVbApp.vbproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "backend/AspNetVbApp.vbproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY frontend/wwwroot ./wwwroot
ENTRYPOINT ["dotnet", "AspNetVbApp.dll"]

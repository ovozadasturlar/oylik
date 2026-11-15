# Build konteksti — repo ildizi:  docker build -f src/Oylik.Server/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Oylik.Contracts/Oylik.Contracts.csproj src/Oylik.Contracts/
COPY src/Oylik.Server/Oylik.Server.csproj src/Oylik.Server/
RUN dotnet restore src/Oylik.Server/Oylik.Server.csproj
COPY src/Oylik.Contracts/ src/Oylik.Contracts/
COPY src/Oylik.Server/ src/Oylik.Server/
RUN dotnet publish src/Oylik.Server/Oylik.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Ubuntu 24.04 asosida. Npgsql startda GSSAPI'ni sinaydi — kutubxona bo'lmasa logda "Cannot load library libgssapi_krb5.so.2".
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
# Baza — PostgreSQL: ulanish satri ConnectionStrings__Oylik env orqali (docker-compose.yml, docs/server-postgres.md).
ENV ASPNETCORE_HTTP_PORTS=5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "Oylik.Server.dll"]

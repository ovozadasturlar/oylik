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
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:5000 \
    ConnectionStrings__Oylik="Data Source=/data/oylik-server.db"
VOLUME /data
EXPOSE 5000
ENTRYPOINT ["dotnet", "Oylik.Server.dll"]

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props global.json ./
COPY src/ ./src/
RUN dotnet publish src/Lab.Worker/Lab.Worker.csproj -c Release -o /worker
RUN dotnet publish src/Lab.Api/Lab.Api.csproj -c Release -o /api

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /api ./
USER $APP_UID
ENTRYPOINT ["dotnet", "Lab.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS worker
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /worker ./
RUN mkdir -p /exports && chown $APP_UID /exports
USER $APP_UID
ENTRYPOINT ["dotnet", "Lab.Worker.dll"]
